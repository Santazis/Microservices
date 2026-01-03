using Catalog.Database.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Database;

public class ApplicationDbContext : DbContext
{
    public DbSet<Domain.Catalogs.Catalog> Catalogs { get; set; }
    public DbSet<Domain.Products.Product> Products { get; set; }
    public DbSet<Domain.Products.ProductImage> ProductImages { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public ApplicationDbContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}