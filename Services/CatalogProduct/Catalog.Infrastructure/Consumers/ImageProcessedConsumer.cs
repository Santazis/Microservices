using Catalog.Application.Interfaces;
using Catalog.Application.Models.Images;

using Catalog.Application.Models.Requests.ProductImage;
using Contracts.IntegrationEvents;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Consumers;

public class ImageProcessedConsumer : IConsumer<ImageProcessedIntegrationEvent>
{
    private readonly IProductImageService _productImageService;
    private readonly ILogger<ImageProcessedConsumer> _logger;
    public ImageProcessedConsumer(IProductImageService productImageService, ILogger<ImageProcessedConsumer> logger)
    {
        _productImageService = productImageService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ImageProcessedIntegrationEvent> context)
    {
        
        _logger.LogInformation("{imagesCount} Images processed for product {productId}",context.Message.Images.Count, context.Message.ProductId);
        var updateRequests = context.Message.Images.Where(r => r.Value.Status == ImageProcessingResultStatus.Success && r.Value.Url != null)
            .Select(r => new UpdateImageRequest(r.Key, r.Value.Url!,null));
        
        await _productImageService.UpdateImagesAsync(context.Message.ProductId, updateRequests,
            context.CancellationToken);
        _logger.LogInformation("{imagesCount} Images updated for product {productId}",context.Message.Images.Count, context.Message.ProductId);
    }
}