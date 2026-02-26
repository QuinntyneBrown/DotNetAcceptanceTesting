using Serilog;
using Shared.Messaging;

namespace Shared.Tests.Messaging;

public class InMemoryPubSubTests
{
    private readonly InMemoryPubSub _pubSub;

    public InMemoryPubSubTests()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        _pubSub = new InMemoryPubSub(logger);
    }

    [Fact]
    public async Task PublishAsync_delivers_message_to_subscriber()
    {
        var received = new TaskCompletionSource<TestMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await _pubSub.SubscribeAsync<TestMessage>("test-channel", msg =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        });

        await _pubSub.PublishAsync("test-channel", new TestMessage { Value = 42, Name = "alpha" });

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(42, result.Value);
        Assert.Equal("alpha", result.Name);
    }

    [Fact]
    public async Task PublishAsync_does_not_deliver_to_different_channel()
    {
        var received = false;

        await _pubSub.SubscribeAsync<TestMessage>("channel-a", _ =>
        {
            received = true;
            return Task.CompletedTask;
        });

        await _pubSub.PublishAsync("channel-b", new TestMessage { Value = 1 });

        await Task.Delay(100);
        Assert.False(received);
    }

    [Fact]
    public async Task PublishAsync_delivers_to_multiple_subscribers()
    {
        var count = 0;

        await _pubSub.SubscribeAsync<TestMessage>("multi", _ =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        await _pubSub.SubscribeAsync<TestMessage>("multi", _ =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        await _pubSub.PublishAsync("multi", new TestMessage { Value = 1 });

        await Task.Delay(100);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task UnsubscribeAsync_stops_delivery()
    {
        var count = 0;

        await _pubSub.SubscribeAsync<TestMessage>("unsub", _ =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        await _pubSub.PublishAsync("unsub", new TestMessage { Value = 1 });
        await Task.Delay(50);
        Assert.Equal(1, count);

        await _pubSub.UnsubscribeAsync("unsub");

        await _pubSub.PublishAsync("unsub", new TestMessage { Value = 2 });
        await Task.Delay(50);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PublishAsync_handler_exception_does_not_break_other_subscribers()
    {
        var received = new TaskCompletionSource<TestMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await _pubSub.SubscribeAsync<TestMessage>("error-test", _ =>
            throw new InvalidOperationException("boom"));

        await _pubSub.SubscribeAsync<TestMessage>("error-test", msg =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        });

        await _pubSub.PublishAsync("error-test", new TestMessage { Value = 99 });

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public async Task PublishAsync_with_no_subscribers_does_not_throw()
    {
        var exception = await Record.ExceptionAsync(() =>
            _pubSub.PublishAsync("empty", new TestMessage { Value = 1 }));

        Assert.Null(exception);
    }

    private class TestMessage
    {
        public int Value { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
