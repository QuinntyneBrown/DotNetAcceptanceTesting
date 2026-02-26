namespace EventStore.Persistence;

public class CouchbaseOptions
{
    public string ConnectionString { get; set; } = "couchbase://localhost";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string BucketName { get; set; } = "events";
    public string ScopeName { get; set; } = "_default";
    public string CollectionName { get; set; } = "events";
}
