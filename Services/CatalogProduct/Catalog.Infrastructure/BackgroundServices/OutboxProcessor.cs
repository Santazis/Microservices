using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Catalog.Database;
using Catalog.Database.Outbox;
using Catalog.Infrastructure.ActivitySources;
using Contracts.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Catalog.Infrastructure.BackgroundServices;

public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScope;
    private static readonly ConcurrentDictionary<string, Type?> _typesCache = new();
    private readonly ILogger<OutboxProcessor> _logger;
    private const int _batchSize = 2000;
    private int _totalIterations = 0;
    private int _totalProcessedCount = 0;
    private const int _maxParallelism = 5;
    private int _currentParallelism = 1;

    public OutboxProcessor(IServiceScopeFactory serviceScope, ILogger<OutboxProcessor> logger)
    {
        _serviceScope = serviceScope;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pipeline = Pipeline();
        using var activity =
            OutboxActivitySource.Source.StartActivity(name: "Outbox.Save", kind: ActivityKind.Internal);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _totalIterations = 0;
                _totalProcessedCount = 0;
                await pipeline.ExecuteAsync(async ct =>
                {
                    await Parallel.ForEachAsync(Enumerable.Range(0, _currentParallelism), new ParallelOptions()
                    {
                        CancellationToken = ct,
                        MaxDegreeOfParallelism = _currentParallelism
                    }, async (_, token) =>
                    {
                        Console.WriteLine(_currentParallelism);
                        var processedCount = await ProcessOutboxMessages(token);
                        switch (processedCount)
                        {
                            case >= _batchSize when _currentParallelism < _maxParallelism:
                                Interlocked.Increment(ref _currentParallelism);
                                break;
                            case < _batchSize when _currentParallelism > 1:
                                Interlocked.Decrement(ref _currentParallelism);
                                break;
                        }
                    });
                }, stoppingToken);
                _logger.LogInformation(
                    "Total processed: {TotalProcessed}, Total iterations: {TotalIterations}",
                    _totalProcessedCount, _totalIterations);
            }
            catch (BrokenCircuitException e)
            {
                Console.WriteLine("Circuit is broken!");
                Console.WriteLine(e);
                Interlocked.Exchange(ref _currentParallelism, 1);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error processing outbox messages!");
                Interlocked.Exchange(ref _currentParallelism, 1);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    private static ResiliencePipeline Pipeline()
    {
        return new ResiliencePipelineBuilder().AddRetry(
            new RetryStrategyOptions()
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential
            }
        ).AddCircuitBreaker(new CircuitBreakerStrategyOptions()
        {
            ShouldHandle = new PredicateBuilder().Handle<Exception>(),
            FailureRatio = 0.1,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30),
            OnOpened = args =>
            {
                Console.WriteLine("Bus is down! Circuit opened.");
                return default;
            },
            OnClosed = args =>
            {
                Console.WriteLine("Bus is back! Circuit closed.");
                return default;
            }
        }).Build();
    }

    private async Task<int> ProcessOutboxMessages(CancellationToken stoppingToken)
    {
        Interlocked.Increment(ref _totalIterations);
        using var scope = _serviceScope.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var messages = await db.OutboxMessages.FromSqlRaw(
            $@"SELECT * FROM ""OutboxMessages"" WHERE ""IsPublished"" = false 
                ORDER By ""Created"" 
                FOR UPDATE SKIP LOCKED
                 LIMIT {_batchSize}").ToListAsync(stoppingToken);
        var updateQue = new ConcurrentQueue<OutboxUpdateRequest>();
        if (messages.Any())
        {
            try
            {
                var publishTasks = messages.Select(m => PublishMessage(m, updateQue, bus));
                Interlocked.Add(ref _totalProcessedCount, messages.Count);
                await Task.WhenAll(publishTasks);
            }
            finally
            {
                await UpdateDatabase(stoppingToken, updateQue, db);
            }

            return messages.Count;
        }

        return 0;
    }

    private static async Task UpdateDatabase(CancellationToken stoppingToken,
        ConcurrentQueue<OutboxUpdateRequest> updateQue,
        ApplicationDbContext db)
    {
        if (updateQue.IsEmpty)
        {
            return;
        }
        var updates = updateQue.ToList();
        var jsonPayload = JsonSerializer.Serialize(updates);
        var updateQuery = """
                          UPDATE "OutboxMessages" m
                          SET
                              "IsPublished"   = v."IsPublished",
                              "PublishedDate" = v."PublishedDate",
                              "ErrorMessage"  = v."ErrorMessage"
                          FROM
                               jsonb_to_recordset(@json_data::jsonb)
                          AS v(
                              "MessageId" 
                              "IsPublished"  
                              "PublishedDate" 
                              "ErrorMessage"
                          )
                          WHERE m."MessageId" = v."MessageId";
                          """;

        var parameter = new NpgsqlParameter("json_data", NpgsqlTypes.NpgsqlDbType.Jsonb) 
        { 
            Value = jsonPayload 
        };
        // var valueList = string.Join(",",
        //     updateQue.Select((_, i) => $"(@MessageId{i},@IsPublished{i},@PublishedDate{i},@ErrorMessage{i})"));
        // for (int i = 0; i < updateQue.Count; i++)
        // {
        //     parameters.Add(new NpgsqlParameter($"MessageId{i}", updates[i].MessageId));
        //     parameters.Add(new NpgsqlParameter($"IsPublished{i}", updates[i].IsPublished));
        //     parameters.Add(new NpgsqlParameter($"PublishedDate{i}", updates[i].PublishedDate));
        //     parameters.Add(new NpgsqlParameter($"ErrorMessage{i}", updates[i].ErrorMessage ?? (object)DBNull.Value));
        // }

        // var formattedQuery = string.Format(updateQuery, valueList);
        await db.Database.ExecuteSqlRawAsync(updateQuery, [parameter], stoppingToken);
    }

    private static async Task PublishMessage(OutboxMessage outboxMessage,
        ConcurrentQueue<OutboxUpdateRequest> updateQue, IPublishEndpoint bus)
    {
        var updateRequest = new OutboxUpdateRequest(outboxMessage.MessageId, true, DateTime.UtcNow, null);
        try
        {
            var type = GetOrAddMessageType(outboxMessage);
            if (type is null)
            {
                updateRequest = updateRequest with
                {
                    IsPublished = false, ErrorMessage = "Event type not found"
                };
                updateQue.Enqueue(updateRequest);
                return;
            }

            var @event = JsonSerializer.Deserialize(outboxMessage.Payload, type, new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            });
            await bus.Publish(@event!);
            updateQue.Enqueue(updateRequest);
        }
        catch (Exception e)
        {
            updateRequest = updateRequest with { IsPublished = false, ErrorMessage = e.ToString() };
            updateQue.Enqueue(updateRequest);
            throw;
        }
    }

    private static Type? GetOrAddMessageType(OutboxMessage outboxMessage)
    {
        var type = _typesCache.GetOrAdd(outboxMessage.MessageType,
            t => { return typeof(IIntegrationEvent).Assembly.GetTypes().FirstOrDefault(e => e.Name == t); });
        return type;
    }

    private async Task SeedOutboxMessagesAsync(int totalMessages = 600000, int batchSize = 20000)
    {
        using var scope = _serviceScope.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        await db.OutboxMessages.ExecuteDeleteAsync();
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        int totalSeeded = 0;

        for (int batch = 0; batch < totalMessages; batch += batchSize)
        {
            var outboxMessages = new List<OutboxMessage>();
            int currentBatchSize = Math.Min(batchSize, totalMessages - batch);

            for (int i = 0; i < currentBatchSize; i++)
            {
                var message = new OutboxMessage
                {
                    MessageId = Guid.NewGuid(),
                    MessageType = "ProductDeletedIntegrationEvent",
                    Payload = JsonSerializer.Serialize(new
                    {
                        id = Guid.NewGuid()
                    }, jsonOptions),
                    Created = DateTime.UtcNow,
                    IsPublished = false
                };

                outboxMessages.Add(message);
            }

            await db.OutboxMessages.AddRangeAsync(outboxMessages);
            await db.SaveChangesAsync();
            outboxMessages.Clear();
            totalSeeded += currentBatchSize;
            _logger.LogInformation("Seeded {Count} messages. Total: {Total}", currentBatchSize, totalSeeded);
        }

        _logger.LogInformation("Seeding completed. Total messages in database: {Total}", totalSeeded);
    }

    private record OutboxUpdateRequest(Guid MessageId, bool IsPublished, DateTime PublishedDate, string? ErrorMessage);
}