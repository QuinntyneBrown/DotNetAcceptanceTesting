using EventStore.Models;

namespace EventStore.Persistence;

public interface IEventRepository
{
    Task StoreAsync(StoredEvent storedEvent);
}
