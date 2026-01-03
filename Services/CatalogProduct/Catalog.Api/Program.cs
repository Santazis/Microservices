using Catalog.Api.ExceptionHandlers;
using Catalog.Api.Extensions;
using Catalog.Application;
using Catalog.Database;
using Catalog.Infrastructure.ActivitySources;
using Catalog.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Observability;
using OpenTelemetry.Trace;
using ProductGrpc;

//disabling tls for grpc to work without https
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);
//for grpc
builder.WebHost.ConfigureKestrel(o => { o.ListenAnyIP(8081, l => l.Protocols = HttpProtocols.Http2); });

//for http
builder.WebHost.ConfigureKestrel(o => { o.ListenAnyIP(8080, l => l.Protocols = HttpProtocols.Http1); });

builder.Configuration.AddJsonFile("appsettings.json", false, true)
    .AddEnvironmentVariables();
builder.Services.AddDbContext<ApplicationDbContext>(o =>
{
    o.UseNpgsql(Environment.GetEnvironmentVariable("NpgsqlConnection"));
});
builder.Services.AddDbContext<OutboxDbContext>(o =>
{
    o.UseNpgsql(Environment.GetEnvironmentVariable("NpgsqlConnection"));
    o.ConfigureWarnings(w => w.Ignore(RelationalEventId.CommandExecuted));
});
builder.Services.AddAmazonS3(builder.Configuration);
builder.Services.AddMessageBroker(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddBackgroundServices();
builder.Services.AddValidatorsFromAssembly(typeof(ApplicationAssembly).Assembly, includeInternalTypes: true);
builder.Services.AddControllers();
builder.Services.AddObservability("catalog-api",
    conf => { conf.AddSource(MassTransit.Logging.DiagnosticHeaders.DefaultListenerName); });
builder.Services.AddOpenTelemetry()
    .WithTracing(tr =>
    {
        tr.AddSource(OutboxActivitySource.Name)
            .SetSampler(new ParentBasedSampler(
                new TraceIdRatioBasedSampler(0.01))).AddOtlpExporter();
    });
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.ConfigureSwaggerWithJwt();
builder.Services.AddGrpc();
var app = builder.Build();
app.UseExceptionHandler();
app.MapGrpcService<ProductGrpcService>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // app.ClearDatabase();
    // app.SeedCatalogs();
    // app.SeedProducts();
    // app.PrintDatabaseStats();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();