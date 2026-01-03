namespace Contracts.IntegrationEvents;

public record ProductDeletedIntegrationEvent(Guid Id) : IIntegrationEvent;