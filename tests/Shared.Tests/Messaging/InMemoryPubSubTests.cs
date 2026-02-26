using NUnit.Framework;
using Serilog;
using Shared.Messaging;

namespace Shared.Tests.Messaging;

public class InMemoryPubSubTests
{
    private InMemoryPubSub _pubSub = default!;

    [SetUp]
    public void SetUp()
    {
        var logger = new LoggerConfiguration().CreateLogger();
        _pubSub = new InMemoryPubSub(logger);
    }

    [Test]
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
        Assert.That(result.Value, Is.EqualTo(42));
        Assert.That(result.Name, Is.EqualTo("alpha"));
    }

    [Test]
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
        Assert.That(received, Is.False);
    }

    [Test]
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
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
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
        Assert.That(count, Is.EqualTo(1));

        await _pubSub.UnsubscribeAsync("unsub");

        await _pubSub.PublishAsync("unsub", new TestMessage { Value = 2 });
        await Task.Delay(50);
        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
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
        Assert.That(result.Value, Is.EqualTo(99));
    }

    [Test]
    public void PublishAsync_with_no_subscribers_does_not_throw()
    {
        Assert.DoesNotThrowAsync(() =>
            _pubSub.PublishAsync("empty", new TestMessage { Value = 1 }));
    }

    private class TestMessage
    {
        public int Value { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
