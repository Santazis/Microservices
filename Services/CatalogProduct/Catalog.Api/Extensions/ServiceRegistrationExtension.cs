using Amazon.Runtime;
using Amazon.S3;
using Catalog.Application.Interfaces;
using Catalog.Application.Services;
using Catalog.Application.Services.Catalog;
using Catalog.Application.Services.Product;
using Catalog.Infrastructure.BackgroundServices;
using Catalog.Infrastructure.Consumers;
using Catalog.Infrastructure.Options;
using Catalog.Infrastructure.Services;
using MassTransit;
using Microsoft.Extensions.Options;

namespace Catalog.Api.Extensions
{
    public static class ServiceRegistrationExtension
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<ICatalogService, CatalogService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<ITempStorageService, TempStorageService>();
            services.AddScoped<IProductImageService, ProductImageService>();
            services.AddScoped<IOutboxMessageService, OutboxMessageService>();
            return services;
        }

        public static IServiceCollection AddMessageBroker(this IServiceCollection services,IConfiguration configuration)
        {
            services.AddMassTransit(conf =>
            {
                conf.AddConsumer<ImageProcessedConsumer>();
                conf.SetKebabCaseEndpointNameFormatter();
                conf.UsingRabbitMq((context, opt) =>
                {
                    
                    opt.Host(new Uri(configuration["MessageBroker:Host"]!), cfg =>
                    {
                        cfg.Username(configuration["MessageBroker:Username"]!);
                        cfg.Password(configuration["MessageBroker:Password"]!);
                    });
                    opt.ConfigureEndpoints(context);
                });
            });
            return services;
        }

        public static IServiceCollection AddAmazonS3(this IServiceCollection services,IConfiguration configuration)
        {
            services.Configure<S3Settings>(configuration.GetSection("S3Settings"));
            services.AddSingleton<IAmazonS3>(sp =>
            {
                var settings = sp.GetRequiredService<IOptions<S3Settings>>().Value;
                var cred = new BasicAWSCredentials(settings.AccessKey, settings.SecretKey);
                var config = new AmazonS3Config()
                {
                    ServiceURL = settings.ServiceUrl,
                    ForcePathStyle = true
                };
                return new AmazonS3Client(cred, config);
            });
            return services;
        }

        public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
        {
            services.AddHostedService<OutboxProcessor>();
            return services;
        }
    }
}