using System.Diagnostics;

namespace Catalog.Infrastructure.ActivitySources;

public class OutboxActivitySource
{
    public const string Name = "Outbox";
    public static readonly ActivitySource Source = new (Name);
    
}