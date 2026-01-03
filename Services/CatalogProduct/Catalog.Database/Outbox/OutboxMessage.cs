namespace Catalog.Database.Outbox;

public class OutboxMessage

{
   public Guid MessageId { get; set; }
   public string MessageType { get; set; } = string.Empty;
   public string Payload { get; set; } = string.Empty;
   public DateTime Created { get; set; }
   public DateTime? PublishedDate { get; set; }
   public bool IsPublished { get; set; }
   public string? ErrorMessage { get; set; }
}