using Microsoft.EntityFrameworkCore;

namespace Catalog.Database;

public class OutboxDbContext : ApplicationDbContext
{
    public OutboxDbContext(DbContextOptions options) : base(options)
    {
    }
}