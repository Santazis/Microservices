using Catalog.Application.Interfaces;
using Catalog.Application.Models.Images;
using Catalog.Application.Models.Product;
using Catalog.Application.Models.Requests.Pagination;
using Catalog.Application.Models.Requests.Product;
using Catalog.Application.Models.Responses;
using Catalog.Database;
using Catalog.Domain.Common.Errors;
using Catalog.Domain.Products;
using Contracts.IntegrationEvents;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel.Common;

namespace Catalog.Application.Services.Product;

public class ProductService : IProductService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICatalogService _catalogService;
    private readonly ILogger<ProductService> _logger;
    private readonly IOutboxMessageService _outbox;

    public ProductService(ApplicationDbContext dbContext, ICatalogService catalogService,
        ILogger<ProductService> logger, IOutboxMessageService outbox)
    {
        _dbContext = dbContext;
        _catalogService = catalogService;
        _logger = logger;
        _outbox = outbox;
    }

    public async Task<Result<Guid>> CreateAsync(CreateProductRequest request, CancellationToken cancellation)
    {
        _logger.LogInformation("Creating product {name} for catalog {catalogId}", request.Name, request.CatalogId);
        var catalogExists = await _catalogService.CatalogExistsAsync(request.CatalogId, cancellation);
        if (catalogExists.IsFailure)
        {
            _logger.LogError("Catalog {catalogId} not found", request.CatalogId);
            return Result<Guid>.Failure(catalogExists.Error);
        }

        var amount = Money.Create(request.Price, request.Currency);
        var product = Domain.Products.Product.Create(request.Name, request.Description, amount, request.CatalogId,
            request.StockQuantity,
            request.MerchantId);
        await _dbContext.Products.AddAsync(product, cancellation);
        await _dbContext.SaveChangesAsync(cancellation);
        _logger.LogInformation("Product created {productId}", product.Id);
        return Result<Guid>.Success(product.Id);
    }

    public async Task<PaginatedResponse<ProductDto>> GetProductsAsync(PaginationRequest pagination,
        CancellationToken cancellation)
    {
        var count = await _dbContext.Products.CountAsync(cancellation);
        var products = await _dbContext.Products.AsNoTracking()
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(p => new ProductDto(p.Id, p.CatalogId, p.Name, p.Description, p.Price.Amount, p.Price.Currency,
                p.Images.Select(i => new ImageDto(i.Url, i.SortOrder)).ToList(), p.StockQuantity > 0, p.StockQuantity))
            .ToListAsync(cancellation);
        return new PaginatedResponse<ProductDto>(products, count);
    }

    public async Task<Result> DeleteAsync(Guid id, Guid userId, CancellationToken cancellation)
    {
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellation);
        if (product is null)
        {
            _logger.LogError("Product {productId} not found", id);
            return Result.Failure(ProductErrors.NotFound);
        }

        //temporary no auth see controller
        if (product.MerchantId != null && product.MerchantId != userId)
        {
            _logger.LogWarning("{userId} trying do delete other user product", userId);
            return Result.Failure(ProductErrors.NoPermission);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellation);
        try
        {
            _dbContext.Products.Remove(product);
            _logger.LogInformation("Product {productId} deleted", id);
            var @event = new ProductDeletedIntegrationEvent(product.Id);
            await _outbox.PublishMessagesAsync(@event, cancellation);
            await _dbContext.SaveChangesAsync(cancellation);
            await transaction.CommitAsync(cancellation);

        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(cancellation);
            _logger.LogError(e, "Error deleting product {productId}", id);
            return Result.Failure(new Error("Failed to delete product", "ProductService.DeleteAsync"));
        }
        return Result.Success;
    }

    public async Task<Result<ProductDto>> GetByIdAsync(Guid id, CancellationToken cancellation)
    {
        var product = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDto(
                p.Id,
                p.CatalogId,
                p.Name,
                p.Description,
                p.Price.Amount,
                p.Price.Currency,
                p.Images.Select(i => new ImageDto(i.Url, i.SortOrder)).ToList(),
                p.StockQuantity > 0,
                p.StockQuantity
            ))
            .FirstOrDefaultAsync(cancellation);
        if (product is null)
        {
            _logger.LogError("Product {productId} not found", id);
            return Result<ProductDto>.Failure(ProductErrors.NotFound);
        }

        return Result<ProductDto>.Success(product);
    }

    public async Task<Result<bool>> IsAvailableAsync(Guid productId, int quantity, CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            _logger.LogInformation("Product {productId} not found", productId);
            return Result<bool>.Failure(ProductErrors.NotFound);
        }

        if (product.StockQuantity == 0)
        {
            _logger.LogInformation("Product out of stock {productId}", productId);
            return Result<bool>.Failure(ProductErrors.OutOfStock);
        }

        if (product.StockQuantity < quantity)
        {
            _logger.LogInformation("Insufficient stock for product {productId}", productId);
            return Result<bool>.Failure(ProductErrors.InsufficientStock);
        }

        _logger.LogInformation("Product {productId} is available", productId);
        return Result<bool>.Success(true);
    }

    public async Task<IEnumerable<ProductDto>> GetProductsByIds(HashSet<Guid> ids, CancellationToken cancellation)
    {
        var products = await _dbContext.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new ProductDto(
                p.Id,
                p.CatalogId,
                p.Name,
                p.Description,
                p.Price.Amount,
                p.Price.Currency,
                p.Images.Select(i => new ImageDto(i.Url, i.SortOrder)).ToList(),
                p.StockQuantity > 0,
                p.StockQuantity))
            .ToListAsync(cancellation);
        return products;
    }
}