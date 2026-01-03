using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.Application.Interfaces;
using Catalog.Database;
using Catalog.Database.Outbox;
using Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Services;

public class OutboxMessageService : IOutboxMessageService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<OutboxMessageService> _logger;
    
    private readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public OutboxMessageService(ApplicationDbContext dbContext, ILogger<OutboxMessageService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task PublishMessagesAsync(IIntegrationEvent @event, CancellationToken cancellation)
    {
        var eventType = @event.GetType();
        var message = new OutboxMessage
        {
            MessageId = Guid.NewGuid(),
            MessageType = eventType.Name,
            Payload = JsonSerializer.Serialize(@event, eventType, jsonSerializerOptions),
            Created = DateTime.UtcNow,
            IsPublished = false,
        };
        _logger.LogInformation("Publishing message {MessageId} of type {MessageType}", message.MessageId,
            message.MessageType);
        await _dbContext.OutboxMessages.AddAsync(message, cancellation);
        await _dbContext.SaveChangesAsync(cancellation);
    }
}