using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddObservability(this IServiceCollection services, string serviceName,Action<TracerProviderBuilder>? configure = null)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(conf => conf.AddService(serviceName))
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddPrometheusExporter();
            }).WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation();
                tracing.AddEntityFrameworkCoreInstrumentation(conf=>conf.SetDbStatementForText = true);
                tracing.AddHttpClientInstrumentation();
                tracing.AddGrpcClientInstrumentation();
                tracing.AddOtlpExporter();
                configure?.Invoke(tracing);
            });
        services.AddLogging(log =>
        {
            log.AddOpenTelemetry(opt =>
            {
                opt.IncludeScopes = true;
                opt.AttachLogsToActivityEvent();
                opt.IncludeFormattedMessage = true;
                opt.AddOtlpExporter();
            });
            
        });
        return services;
    }
}