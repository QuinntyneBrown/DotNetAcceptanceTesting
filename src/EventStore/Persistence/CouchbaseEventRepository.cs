using Couchbase;
using Couchbase.KeyValue;
using EventStore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventStore.Persistence;

public class CouchbaseEventRepository : IEventRepository, IAsyncDisposable
{
    private readonly CouchbaseOptions _options;
    private readonly ILogger<CouchbaseEventRepository> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private ICluster? _cluster;
    private ICouchbaseCollection? _collection;

    public CouchbaseEventRepository(
        IOptions<CouchbaseOptions> options,
        ILogger<CouchbaseEventRepository> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task StoreAsync(StoredEvent storedEvent)
    {
        var collection = await GetCollectionAsync();
        await collection.UpsertAsync(storedEvent.Id, storedEvent);
        _logger.LogDebug("Stored event {EventId} in Couchbase", storedEvent.Id);
    }

    private async Task<ICouchbaseCollection> GetCollectionAsync()
    {
        if (_collection is not null) return _collection;

        await _initLock.WaitAsync();
        try
        {
            if (_collection is not null) return _collection;

            _cluster = await Cluster.ConnectAsync(
                _options.ConnectionString,
                _options.Username,
                _options.Password);

            var bucket = await _cluster.BucketAsync(_options.BucketName);
            var scope = bucket.Scope(_options.ScopeName);
            _collection = scope.Collection(_options.CollectionName);

            _logger.LogInformation(
                "Connected to Couchbase bucket {Bucket}/{Scope}/{Collection}",
                _options.BucketName, _options.ScopeName, _options.CollectionName);

            return _collection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.DisposeAsync();
        }
    }
}
