using Contracts.IntegrationEvents;

namespace Catalog.Application.Interfaces;

public interface IOutboxMessageService
{
    Task PublishMessagesAsync(IIntegrationEvent @event,CancellationToken cancellation);
}