using System.Collections.Concurrent;
using EventStore.Models;
using EventStore.Persistence;

namespace EventStore.AcceptanceTests;

public class InMemoryEventRepository : IEventRepository
{
    private readonly ConcurrentBag<StoredEvent> _events = new();

    public IReadOnlyList<StoredEvent> StoredEvents => _events.ToList();

    public Task StoreAsync(StoredEvent storedEvent)
    {
        _events.Add(storedEvent);
        return Task.CompletedTask;
    }
}
