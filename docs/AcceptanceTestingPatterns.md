# Acceptance Testing Patterns for .NET Microservices

## Table of Contents

1. [Introduction](#1-introduction)
2. [What Are Acceptance Tests?](#2-what-are-acceptance-tests)
3. [Why Acceptance Tests Over Integration or End-to-End Tests](#3-why-acceptance-tests-over-integration-or-end-to-end-tests)
4. [Technologies](#4-technologies)
5. [Core Principle: Dependency Inversion and Interface Substitution](#5-core-principle-dependency-inversion-and-interface-substitution)
6. [The Two Test Harness Patterns](#6-the-two-test-harness-patterns)
7. [Pattern 1: WebApplicationFactory for HTTP / Web Projects](#7-pattern-1-webapplicationfactory-for-http--web-projects)
8. [Pattern 2: Generic Host Factory for Worker / Non-HTTP Projects](#8-pattern-2-generic-host-factory-for-worker--non-http-projects)
9. [Building In-Memory Test Doubles](#9-building-in-memory-test-doubles)
10. [Test Structure and Lifecycle](#10-test-structure-and-lifecycle)
11. [Assertion Patterns for Asynchronous Systems](#11-assertion-patterns-for-asynchronous-systems)
12. [Step-by-Step Guide: Adding Acceptance Tests to a New Project](#12-step-by-step-guide-adding-acceptance-tests-to-a-new-project)
13. [Reference: All Projects in This Solution](#13-reference-all-projects-in-this-solution)
14. [Anti-Patterns to Avoid](#14-anti-patterns-to-avoid)
15. [Checklist](#15-checklist)

---

## 1. Introduction

This document describes the acceptance testing patterns used across this reference solution. The solution demonstrates how to test .NET microservices that communicate via Redis PubSub, receive UDP packets, persist data to Couchbase, and push events to SignalR clients — all **without requiring any external infrastructure to run the tests**.

Every acceptance test in this solution runs entirely in-process. There are no Docker containers, no running Redis instances, no Couchbase clusters, and no network sockets. The tests start fast, run deterministically, and can execute in any CI/CD pipeline with nothing more than the .NET SDK installed.

The key enabler is a disciplined application of the **Dependency Inversion Principle (DIP)**: production code depends on abstractions, and test harnesses substitute lightweight in-memory implementations for those abstractions.

---

## 2. What Are Acceptance Tests?

Acceptance tests verify that a **project behaves correctly as a whole**. They sit between unit tests and end-to-end tests in the testing pyramid:

| Level | Scope | External Dependencies | Speed |
|-------|-------|-----------------------|-------|
| **Unit Tests** | A single class or function | None | Fastest |
| **Acceptance Tests** | A full project (all its internal wiring, DI, hosted services, middleware) | Replaced with in-memory doubles | Fast |
| **Integration Tests** | Multiple projects or real infrastructure | Real databases, message brokers | Slower |
| **End-to-End Tests** | The entire deployed system | Everything real | Slowest |

An acceptance test exercises the real DI container, the real middleware pipeline, the real hosted services, and the real business logic — everything the project configures for itself. The **only** things replaced are the **external boundaries**: the database, the message broker, the network socket.

This gives confidence that the project is wired together correctly while remaining fast and infrastructure-free.

---

## 3. Why Acceptance Tests Over Integration or End-to-End Tests

### The problem with requiring real infrastructure

In a microservices environment, each service typically depends on one or more external systems: Redis, Couchbase, PostgreSQL, RabbitMQ, UDP endpoints, etc. If acceptance tests required these systems to be running:

- **Local development friction**: Every developer needs Docker or local installations of every dependency.
- **CI/CD complexity**: Pipelines need service containers, health checks, and teardown logic.
- **Flaky tests**: Network timeouts, port conflicts, and container startup races cause non-deterministic failures.
- **Slow feedback**: Starting containers and waiting for health checks adds seconds or minutes to each test run.

### What acceptance tests give us

- **Zero infrastructure**: Tests run with `dotnet test` and nothing else.
- **Deterministic results**: No network, no timing issues, no flaky infrastructure.
- **Fast feedback**: All 41 tests in this solution complete in under 35 seconds.
- **Refactoring confidence**: If you change internal wiring, DI registrations, or service interactions, the tests catch regressions immediately.
- **Documentation**: Each test describes a concrete behaviour the project must exhibit.

### What acceptance tests do NOT replace

Acceptance tests do not verify that your Couchbase queries are correct against a real Couchbase cluster, or that your Redis Pub/Sub works with a real Redis instance. You still need a smaller number of integration tests for those boundaries. But those integration tests are narrow (testing the repository or pub/sub implementation in isolation against real infrastructure), not broad (testing your entire service end-to-end).

---

## 4. Technologies

### Test Framework: NUnit 4.x

| Component | Package | Purpose |
|-----------|---------|---------|
| Test framework | `NUnit` 4.5.0 | Test attributes (`[Test]`, `[SetUp]`, `[TearDown]`) and constraint-based assertions |
| Test adapter | `NUnit3TestAdapter` 6.1.0 | Connects NUnit to `dotnet test` and IDE test runners |
| Test SDK | `Microsoft.NET.Test.Sdk` 17.12.0 | Required by all .NET test projects |
| Coverage | `coverlet.collector` 6.0.2 | Code coverage collection |

**Why NUnit?** NUnit's constraint-based assertion model (`Assert.That(actual, Is.EqualTo(expected))`) reads naturally and produces clear failure messages. Its `[SetUp]` / `[TearDown]` lifecycle attributes support `async Task` methods natively, which is essential for the asynchronous host startup and teardown patterns used in acceptance tests.

### Test Host: WebApplicationFactory and Generic Host

| Project Type | Test Host Pattern | Package Required |
|--------------|-------------------|------------------|
| ASP.NET Core (Web API, SignalR) | `WebApplicationFactory<Program>` | `Microsoft.AspNetCore.Mvc.Testing` |
| Worker Service (Background processing) | `Host.CreateDefaultBuilder()` + `IAsyncDisposable` | None (built into `Microsoft.Extensions.Hosting`) |

### In-Memory Doubles

| Production Dependency | In-Memory Double | Mechanism |
|-----------------------|------------------|-----------|
| Redis Pub/Sub (`RedisPubSub`) | `InMemoryPubSub` | `ConcurrentDictionary` of handler lists, JSON round-trip via `MessageEnvelope<T>` |
| UDP socket (`UdpPacketReceiver`) | `InMemoryPacketReceiver` | `System.Threading.Channels.Channel<byte[]>` |
| Couchbase (`CouchbaseEventRepository`) | `InMemoryEventRepository` | `ConcurrentBag<StoredEvent>` |

---

## 5. Core Principle: Dependency Inversion and Interface Substitution

### The Dependency Inversion Principle

> High-level modules should not depend on low-level modules. Both should depend on abstractions.

Every external dependency in this solution is accessed through an **interface** defined in the consuming project or in the Shared library:

```
IMessagePublisher          → RedisPubSub (production) / InMemoryPubSub (test)
IMessageSubscriber         → RedisPubSub (production) / InMemoryPubSub (test)
IPacketReceiver            → UdpPacketReceiver (production) / InMemoryPacketReceiver (test)
IEventRepository           → CouchbaseEventRepository (production) / InMemoryEventRepository (test)
```

### Why this makes acceptance testing possible

Because the business logic depends on `IMessagePublisher` (not `RedisPubSub`), and on `IEventRepository` (not `CouchbaseEventRepository`), the test harness can **substitute** those implementations without changing a single line of production code.

This is the **Liskov Substitution Principle** in action: any implementation of the interface can be used interchangeably. The production code cannot tell the difference between the real and in-memory implementations.

### How substitution works in the DI container

Each project registers its production dependencies in a `ConfigureServices` extension method:

```csharp
// Production registration in src/EventStore/ConfigureServices.cs
public static void AddEventStoreServices(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<CouchbaseOptions>(configuration.GetSection("Couchbase"));

    services.AddSingleton<CouchbaseEventRepository>();
    services.AddSingleton<IEventRepository>(sp => sp.GetRequiredService<CouchbaseEventRepository>());

    services.AddSingleton<RedisPubSub>();
    services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RedisPubSub>());
    services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<RedisPubSub>());

    services.AddHostedService<EventPersistenceService>();
}
```

The test harness then calls this same extension method (so all the real business logic and hosted services are registered), but immediately **removes and replaces** the infrastructure registrations:

```csharp
// Test substitution in tests/EventStore.AcceptanceTests/EventStoreFactory.cs
services.AddEventStoreServices(context.Configuration);   // Register everything normally

// Remove production infrastructure implementations
services.RemoveAll<RedisPubSub>();
services.RemoveAll<IMessagePublisher>();
services.RemoveAll<IMessageSubscriber>();

// Replace with in-memory test doubles
services.AddSingleton(PubSub);
services.AddSingleton<IMessagePublisher>(PubSub);
services.AddSingleton<IMessageSubscriber>(PubSub);

// Remove Couchbase, replace with in-memory repository
services.RemoveAll<CouchbaseEventRepository>();
services.RemoveAll<IEventRepository>();
services.AddSingleton<IEventRepository>(EventRepository);
```

The `RemoveAll<T>()` method (from `Microsoft.Extensions.DependencyInjection.Extensions`) removes every registration of a given service type. We then re-register with our test doubles. This is a **last-registration-wins** pattern that the DI container supports.

### Why we remove the concrete type AND the interface

Notice that we remove both `RemoveAll<RedisPubSub>()` and `RemoveAll<IMessagePublisher>()`. This is important because the production `ConfigureServices` registers the concrete type as a singleton and then registers the interface as a factory that resolves the concrete type:

```csharp
services.AddSingleton<RedisPubSub>();                                                    // (1)
services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RedisPubSub>());    // (2)
```

If we only removed registration (2) and added our own `IMessagePublisher`, registration (1) would still exist. The DI container would still construct a `RedisPubSub` instance (attempting to connect to Redis), even though nothing resolves it by interface. Removing both ensures the production implementation is never instantiated.

---

## 6. The Two Test Harness Patterns

The test harness (or "factory") is responsible for:

1. **Starting the host** with the project's real DI configuration
2. **Substituting** external dependencies with in-memory doubles
3. **Exposing** the in-memory doubles to test methods for interaction and assertion
4. **Managing lifecycle** (start/stop/dispose)

There are two patterns, depending on the project type:

| Project SDK | Host Type | Test Harness Base | Lifecycle |
|-------------|-----------|-------------------|-----------|
| `Microsoft.NET.Sdk.Web` | ASP.NET Core (Kestrel) | `WebApplicationFactory<Program>` | Managed by the factory (implements `IDisposable`) |
| `Microsoft.NET.Sdk.Worker` | Generic Host | Manual `Host.CreateDefaultBuilder()` | Managed by `[SetUp]` / `[TearDown]` (implements `IAsyncDisposable`) |

---

## 7. Pattern 1: WebApplicationFactory for HTTP / Web Projects

### When to use

Use `WebApplicationFactory<Program>` when the project under test is an ASP.NET Core application — i.e., it serves HTTP endpoints, SignalR hubs, gRPC services, or any other Kestrel-hosted functionality.

### What WebApplicationFactory provides

`WebApplicationFactory<Program>` (from `Microsoft.AspNetCore.Mvc.Testing`) is purpose-built for testing ASP.NET Core applications. It:

- **Builds the real host** using the project's `Program.cs` entry point (the `<Program>` type parameter)
- **Creates a test server** (`TestServer`) that handles HTTP requests in-memory without binding to a real TCP port
- **Provides `CreateClient()`** which returns an `HttpClient` pre-configured to send requests to the in-memory test server
- **Provides `Server`** which exposes `CreateHandler()` for non-HTTP protocols like SignalR
- **Provides `ConfigureWebHost()`** as the extension point for substituting DI registrations

### Example: FrontendGateway.Api (HTTP API)

```csharp
public sealed class FrontendGatewayApiFactory : WebApplicationFactory<Program>
{
    private InMemoryPubSub? _pubSub;

    public InMemoryPubSub PubSub =>
        _pubSub ?? throw new InvalidOperationException("Factory not initialized. Call CreateClient() first.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove production Redis registrations
            services.RemoveAll<RedisPubSub>();
            services.RemoveAll<IMessagePublisher>();
            services.RemoveAll<IMessageSubscriber>();

            // Register InMemoryPubSub for test isolation
            var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
            var pubSub = new InMemoryPubSub(logger);
            _pubSub = pubSub;

            services.AddSingleton(pubSub);
            services.AddSingleton<IMessagePublisher>(pubSub);
            services.AddSingleton<IMessageSubscriber>(pubSub);
        });
    }
}
```

**Key points:**

- The factory inherits from `WebApplicationFactory<Program>`. The `Program` type comes from `public partial class Program { }` declared at the bottom of the project's `Program.cs`, which makes the entry point class visible to the test assembly.
- `ConfigureWebHost` is invoked **after** the project's own `Program.cs` configuration runs, so the production services are already registered when we override them.
- The `InMemoryPubSub` instance is stored in a field and exposed as a public property so tests can publish messages to it and subscribe to observe what the system published.
- `WebApplicationFactory` is `IDisposable`. Tests should call `Dispose()` (or use `using`) when finished.

### Example: ClientEventHub (SignalR)

The ClientEventHub factory follows the same pattern. The SignalR test adds one additional technique — creating a `HubConnection` that communicates over the in-memory test server:

```csharp
// In test [SetUp]
var client = _factory.CreateClient();

_hubConnection = new HubConnectionBuilder()
    .WithUrl($"{client.BaseAddress}hubs/events", options =>
    {
        options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
    })
    .Build();

await _hubConnection.StartAsync();
```

The `HttpMessageHandlerFactory` override routes the SignalR connection through the `TestServer` instead of a real HTTP connection. This allows full-fidelity testing of SignalR groups, subscriptions, and broadcasts without any network I/O.

### How tests interact with the factory

```csharp
[Test]
public async Task Post_CreateMission_publishes_command_and_returns_Accepted()
{
    using var factory = new FrontendGatewayApiFactory();
    using var client = factory.CreateClient();    // In-memory HTTP client

    // Subscribe to the in-memory PubSub to observe what the API publishes
    var commandReceived = new TaskCompletionSource<CreateMissionCommand>(...);
    await factory.PubSub.SubscribeAsync<CreateMissionCommand>(channel, cmd =>
    {
        commandReceived.TrySetResult(cmd);
        return Task.CompletedTask;
    });

    // Act — send a real HTTP request through the in-memory pipeline
    var response = await client.PostAsJsonAsync("/api/missions", request);

    // Assert — verify the HTTP response AND the PubSub side-effect
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
    var published = await commandReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.That(published.MissionName, Is.EqualTo("Artemis IV"));
}
```

### Required NuGet package

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
```

> **Version note**: Use the version matching your project's `TargetFramework`. If your project targets `net9.0`, use the 9.x version of this package.

### Required in the project under test

The project's `Program.cs` must declare the `Program` class as visible to the test assembly:

```csharp
// At the bottom of Program.cs
public partial class Program { }
```

This is needed because top-level statements generate an internal `Program` class. The `partial` declaration makes it public so `WebApplicationFactory<Program>` can reference it.

---

## 8. Pattern 2: Generic Host Factory for Worker / Non-HTTP Projects

### When to use

Use the Generic Host pattern when the project under test is a **Worker Service** (`Microsoft.NET.Sdk.Worker`) or any non-HTTP host. These projects use `Host.CreateApplicationBuilder()` or `Host.CreateDefaultBuilder()` instead of `WebApplication.CreateBuilder()`, and they do not serve HTTP endpoints.

Examples in this solution:
- **TelemetryIngest** — Listens for UDP packets, parses CCSDS, publishes to PubSub
- **EventStore** — Subscribes to PubSub events, persists to Couchbase

### Why WebApplicationFactory cannot be used

`WebApplicationFactory<Program>` requires the ASP.NET Core hosting infrastructure (`Microsoft.NET.Sdk.Web`). If you try to use it with a Worker project:

- The project doesn't reference `Microsoft.AspNetCore.*` packages
- There is no `TestServer` to create
- There is no HTTP pipeline to test through

Worker services are headless background processors. They need a different test harness.

### The Generic Host Factory pattern

Instead of inheriting from `WebApplicationFactory`, we build a plain `IHost` manually, applying the same substitution pattern:

```csharp
public sealed class TelemetryIngestFactory : IAsyncDisposable
{
    private IHost _host = default!;

    public InMemoryPubSub PubSub { get; private set; } = default!;
    public InMemoryPacketReceiver PacketReceiver { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
        PubSub = new InMemoryPubSub(logger);
        PacketReceiver = new InMemoryPacketReceiver();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register all real services
                services.AddTelemetryIngestServices(context.Configuration);

                // Substitute external dependencies
                services.RemoveAll<RedisPubSub>();
                services.RemoveAll<IMessagePublisher>();
                services.RemoveAll<IMessageSubscriber>();

                services.AddSingleton(PubSub);
                services.AddSingleton<IMessagePublisher>(PubSub);
                services.AddSingleton<IMessageSubscriber>(PubSub);

                services.RemoveAll<IPacketReceiver>();
                services.AddSingleton<IPacketReceiver>(PacketReceiver);
            })
            .Build();

        await _host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
```

**Key differences from WebApplicationFactory:**

| Aspect | WebApplicationFactory | Generic Host Factory |
|--------|----------------------|---------------------|
| Base class | `WebApplicationFactory<Program>` | None (plain class) |
| Host creation | Automatic from `Program.cs` | Manual `Host.CreateDefaultBuilder()` |
| Service override point | `ConfigureWebHost` → `ConfigureServices` | `ConfigureServices` directly |
| HTTP client | `CreateClient()` returns `HttpClient` | Not applicable |
| Lifecycle | `IDisposable` (auto-managed) | `IAsyncDisposable` (explicit `StartAsync`/`StopAsync`) |
| Host startup | Implicit on first `CreateClient()` call | Explicit `await _host.StartAsync()` |
| Required package | `Microsoft.AspNetCore.Mvc.Testing` | None |

### Why the factory calls the real ConfigureServices extension method

The factory calls `services.AddTelemetryIngestServices(context.Configuration)` before substituting. This is critical because it ensures:

1. All **real hosted services** (`TelemetryIngestService`, `EventPersistenceService`) are registered
2. All **real business logic** is wired up
3. All **real configuration bindings** (Options pattern) are established
4. Only the **infrastructure boundaries** are replaced

If you skip calling the extension method and manually wire everything, you lose confidence that the real DI configuration is correct. The whole point is to test the real wiring.

### How tests interact with the factory

```csharp
[Test]
public async Task Telemetry_packet_is_parsed_and_published_to_correct_APID_channel()
{
    // Subscribe to the in-memory PubSub to observe output
    var messageReceived = new TaskCompletionSource<CcsdsTelemetryMessage>(...);
    await _factory.PubSub.SubscribeAsync<CcsdsTelemetryMessage>(
        Channels.TelemetryForApid(42), msg =>
        {
            messageReceived.TrySetResult(msg);
            return Task.CompletedTask;
        });

    // Feed input through the in-memory receiver (replaces UDP socket)
    await _factory.PacketReceiver.Writer.WriteAsync(rawPacket);

    // Assert — verify what the service published to PubSub
    var published = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.That(published.Apid, Is.EqualTo(42));
}
```

The test drives behaviour through the **input side** (PacketReceiver) and observes results on the **output side** (PubSub). The entire processing pipeline — packet parsing, idle filtering, message mapping, channel routing — runs as it would in production.

---

## 9. Building In-Memory Test Doubles

A test double must implement the **same interface** as the production dependency. It should be simple, deterministic, and provide observability for test assertions.

### Guidelines for building test doubles

1. **Implement the interface, nothing more.** Don't add features the production implementation doesn't have.
2. **Use thread-safe collections.** `ConcurrentDictionary`, `ConcurrentBag`, or `Channel<T>` — hosted services run on background threads.
3. **Expose state for assertion.** Add a public `StoredEvents`, `PublishedMessages`, or `Writer` property that tests can read or write to.
4. **Keep it synchronous where possible.** Return `Task.CompletedTask` when there's no real async work. This eliminates timing issues.

### Example: InMemoryPubSub (Shared library)

This is the most reusable test double. It implements both `IMessagePublisher` and `IMessageSubscriber`, and lives in the Shared project so every test project can use it.

```csharp
public class InMemoryPubSub : IMessagePublisher, IMessageSubscriber
{
    private readonly ConcurrentDictionary<string, List<Func<string, Task>>> _handlers = new();
    private readonly ILogger _logger;

    // Serializes to JSON via MessageEnvelope<T> (same as RedisPubSub) to ensure
    // the test exercises the same serialization path as production.
    public async Task PublishAsync<T>(string channel, T message) where T : class
    {
        var envelope = new MessageEnvelope<T> { Payload = message };
        var json = JsonSerializer.Serialize(envelope);

        // Snapshot handlers to avoid modification during iteration
        // Invoke each handler, catching exceptions to prevent one handler
        // from breaking others (same resilience as production Redis)
    }

    public Task SubscribeAsync<T>(string channel, Func<T, Task> handler) where T : class
    {
        // Registers a handler that deserializes the JSON envelope
        // and invokes the typed handler
    }
}
```

**Why it round-trips through JSON**: The `InMemoryPubSub` serializes messages to JSON and deserializes them back, exactly as `RedisPubSub` does. This catches serialization bugs (missing properties, incorrect types, enum handling) that would be missed by a simpler implementation that just passes objects by reference.

### Example: InMemoryPacketReceiver (TelemetryIngest tests)

```csharp
public class InMemoryPacketReceiver : IPacketReceiver
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();

    public ChannelWriter<byte[]> Writer => _channel.Writer;

    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        return await _channel.Reader.ReadAsync(cancellationToken);
    }
}
```

**Why `System.Threading.Channels`**: The production `UdpPacketReceiver` blocks on `UdpClient.ReceiveAsync()`, waiting for the next packet. The `Channel<byte[]>` provides the same blocking-read semantic: `ReadAsync` will await until a test writes data to the `Writer`, or until the cancellation token fires. This models the real behaviour accurately.

### Example: InMemoryEventRepository (EventStore tests)

```csharp
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
```

**Why `ConcurrentBag`**: The `EventPersistenceService` may invoke `StoreAsync` from multiple PubSub handler callbacks concurrently. `ConcurrentBag<T>` is thread-safe for concurrent adds. The `StoredEvents` property creates a snapshot (`ToList()`) for safe iteration in test assertions.

---

## 10. Test Structure and Lifecycle

### NUnit lifecycle for acceptance tests

```csharp
public class EventStoreAcceptanceTests
{
    private EventStoreFactory _factory = default!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new EventStoreFactory();
        await _factory.InitializeAsync();    // Builds and starts the host
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();       // Stops and disposes the host
    }

    [Test]
    public async Task MissionCreatedEvent_is_stored()
    {
        // Arrange → Act → Assert using _factory.PubSub and _factory.EventRepository
    }
}
```

**Each test gets a fresh host.** The `[SetUp]` creates a new factory and starts a new host for every test. This ensures complete isolation — no shared state leaks between tests.

### WebApplicationFactory lifecycle

For Web projects, the factory can be created per-test (as shown in the FrontendGateway tests) because `WebApplicationFactory` lazily starts the host on the first `CreateClient()` call:

```csharp
[Test]
public async Task Post_CreateMission_returns_Accepted()
{
    using var factory = new FrontendGatewayApiFactory();
    using var client = factory.CreateClient();    // Host starts here

    // ... test ...
}   // factory.Dispose() stops the host
```

Or with `[SetUp]` / `[TearDown]` for shared setup (as in the ClientEventHub tests where a SignalR connection is established once per test).

---

## 11. Assertion Patterns for Asynchronous Systems

Acceptance tests for message-driven systems face a fundamental challenge: the system under test processes messages **asynchronously** on background threads. When a test publishes a message, the result may not be immediately observable.

### Pattern: TaskCompletionSource for event-driven assertions

When testing PubSub output (verifying that a service published a message):

```csharp
var messageReceived = new TaskCompletionSource<CcsdsTelemetryMessage>(
    TaskCreationOptions.RunContinuationsAsynchronously);

await _factory.PubSub.SubscribeAsync<CcsdsTelemetryMessage>(channel, msg =>
{
    messageReceived.TrySetResult(msg);
    return Task.CompletedTask;
});

// Act — trigger the system
await _factory.PacketReceiver.Writer.WriteAsync(rawPacket);

// Assert — await the async result with a timeout
var published = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
Assert.That(published.Apid, Is.EqualTo(42));
```

**Why `TaskCreationOptions.RunContinuationsAsynchronously`**: Without this flag, the continuation (the code after `await`) could run synchronously on the thread that calls `TrySetResult`. In a hosted service context, this could cause deadlocks. The flag ensures the continuation runs on a thread pool thread.

**Why `WaitAsync(TimeSpan)`**: This provides a timeout. If the system fails to produce the expected message, the test fails with a `TimeoutException` after 5 seconds rather than hanging indefinitely.

### Pattern: Polling for state-based assertions

When testing persistence (verifying that a service stored data in a repository):

```csharp
// Helper method that polls with a deadline
private async Task WaitForEventsAsync(int expectedCount)
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (_factory.EventRepository.StoredEvents.Count < expectedCount
           && DateTime.UtcNow < deadline)
    {
        await Task.Delay(50);
    }
}
```

This is used when you need to wait for a side-effect that doesn't have a natural "event" to subscribe to. The polling interval (50ms) is short enough to keep tests fast, and the deadline (5 seconds) prevents hanging.

### Pattern: Proving the negative

When asserting that something did **not** happen (e.g., idle packets are not published, commands are not stored):

```csharp
// Publish the thing that should be ignored
await _factory.PubSub.PublishAsync(Channels.CreateMissionCommand, commandMessage);

// Publish a real event to prove the service is running
await _factory.PubSub.PublishAsync(Channels.MissionCreatedEvent, realEvent);

// Wait for the real event to be stored
await WaitForEventsAsync(1);

// Assert — only the real event was stored, not the command
Assert.That(stored, Has.Count.EqualTo(1));
Assert.That(stored[0].EventType, Is.EqualTo(Channels.MissionCreatedEvent));
```

The key technique: send a "canary" message through a channel you know works, wait for it to arrive, then assert that the unwanted message was not processed. This avoids false passes from timing issues.

---

## 12. Step-by-Step Guide: Adding Acceptance Tests to a New Project

### Prerequisites

Before you can write acceptance tests, the project under test must:

1. **Define interfaces for all external dependencies** (databases, message brokers, sockets, HTTP clients)
2. **Register services through a `ConfigureServices` extension method** so the test factory can call it
3. **Declare `public partial class Program { }` in `Program.cs`** (Web projects only)

### Step 1: Identify external boundaries

List every external system the project communicates with:

| Boundary | Direction | Interface | Production Implementation |
|----------|-----------|-----------|--------------------------|
| Redis PubSub | Input/Output | `IMessagePublisher`, `IMessageSubscriber` | `RedisPubSub` |
| UDP socket | Input | `IPacketReceiver` | `UdpPacketReceiver` |
| Couchbase | Output | `IEventRepository` | `CouchbaseEventRepository` |
| HTTP API | Input | (handled by ASP.NET pipeline) | (Kestrel) |
| SignalR | Output | (handled by SignalR pipeline) | (SignalR) |

### Step 2: Create the test project

```
tests/
  YourProject.AcceptanceTests/
    YourProject.AcceptanceTests.csproj
```

The `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="NUnit" Version="4.5.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.1.0" />
    <!-- Add Microsoft.AspNetCore.Mvc.Testing ONLY for Web projects -->
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\YourProject\YourProject.csproj" />
    <ProjectReference Include="..\..\src\Shared\Shared.csproj" />
  </ItemGroup>
</Project>
```

### Step 3: Build in-memory test doubles

For each external boundary identified in Step 1, create an in-memory implementation:

```csharp
public class InMemoryYourDependency : IYourDependency
{
    // Thread-safe collection for storing calls
    private readonly ConcurrentBag<YourData> _data = new();

    // Public property for test assertions
    public IReadOnlyList<YourData> Data => _data.ToList();

    // Interface implementation
    public Task SaveAsync(YourData data)
    {
        _data.Add(data);
        return Task.CompletedTask;
    }
}
```

**If the Shared library already provides a test double** (like `InMemoryPubSub`), use it directly — don't duplicate it.

### Step 4: Create the test factory

**For a Web project:**

```csharp
public sealed class YourProjectFactory : WebApplicationFactory<Program>
{
    // Expose test doubles as public properties
    public InMemoryPubSub PubSub { get; } = new InMemoryPubSub(logger);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 1. Call the real ConfigureServices (already done by Program.cs)
            // 2. Remove production implementations
            services.RemoveAll<ProductionType>();
            services.RemoveAll<IInterfaceType>();
            // 3. Register test doubles
            services.AddSingleton<IInterfaceType>(TestDouble);
        });
    }
}
```

**For a Worker project:**

```csharp
public sealed class YourProjectFactory : IAsyncDisposable
{
    private IHost _host = default!;

    // Expose test doubles as public properties
    public InMemoryPubSub PubSub { get; private set; } = default!;
    public InMemoryYourDependency YourDependency { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        PubSub = new InMemoryPubSub(logger);
        YourDependency = new InMemoryYourDependency();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // 1. Call the real ConfigureServices
                services.AddYourProjectServices(context.Configuration);
                // 2. Remove production implementations
                services.RemoveAll<ProductionType>();
                services.RemoveAll<IInterfaceType>();
                // 3. Register test doubles
                services.AddSingleton<IInterfaceType>(YourDependency);
            })
            .Build();

        await _host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
```

### Step 5: Write acceptance tests

```csharp
public class YourProjectAcceptanceTests
{
    private YourProjectFactory _factory = default!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new YourProjectFactory();
        await _factory.InitializeAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task When_event_received_then_it_is_persisted()
    {
        // Arrange — set up subscriptions or expectations

        // Act — drive input through the in-memory test double
        await _factory.PubSub.PublishAsync(channel, eventMessage);

        // Assert — verify the output via the other in-memory test double
        await WaitForCondition();
        Assert.That(_factory.YourDependency.Data, Has.Count.EqualTo(1));
    }
}
```

### Step 6: Add to solution

```bash
dotnet sln FrontendGateway.slnx add tests/YourProject.AcceptanceTests/YourProject.AcceptanceTests.csproj --solution-folder tests
```

---

## 13. Reference: All Projects in This Solution

### FrontendGateway.Api

| Aspect | Detail |
|--------|--------|
| SDK | `Microsoft.NET.Sdk.Web` |
| Purpose | HTTP API gateway — receives Angular frontend requests, publishes PubSub commands, performs request/response queries |
| External dependencies | Redis PubSub |
| Test harness | `WebApplicationFactory<Program>` |
| Test doubles | `InMemoryPubSub` (replaces `RedisPubSub`) |
| What tests verify | HTTP status codes, command messages published with correct fields, query response routing via correlation IDs |

### ClientEventHub

| Aspect | Detail |
|--------|--------|
| SDK | `Microsoft.NET.Sdk.Web` |
| Purpose | Bridges PubSub events to SignalR clients — subscribes to event channels, forwards to SignalR groups |
| External dependencies | Redis PubSub |
| Test harness | `WebApplicationFactory<Program>` |
| Test doubles | `InMemoryPubSub` (replaces `RedisPubSub`) |
| Additional test setup | Creates real `HubConnection` routed through `TestServer.CreateHandler()` |
| What tests verify | Events forwarded to subscribed SignalR clients, group isolation, unsubscribe behaviour, multi-client delivery |

### TelemetryIngest

| Aspect | Detail |
|--------|--------|
| SDK | `Microsoft.NET.Sdk.Worker` |
| Purpose | Receives raw CCSDS Space Packets via UDP, parses them, publishes typed messages to PubSub routed by APID |
| External dependencies | Redis PubSub, UDP socket |
| Test harness | Generic Host (`Host.CreateDefaultBuilder()`) + `IAsyncDisposable` |
| Test doubles | `InMemoryPubSub` (replaces `RedisPubSub`), `InMemoryPacketReceiver` (replaces `UdpPacketReceiver`) |
| What tests verify | Packet parsing, APID routing, idle packet filtering, sequence processing, command vs telemetry flags |

### EventStore

| Aspect | Detail |
|--------|--------|
| SDK | `Microsoft.NET.Sdk.Worker` |
| Purpose | Subscribes to event channels on PubSub, persists each event to a Couchbase collection |
| External dependencies | Redis PubSub, Couchbase |
| Test harness | Generic Host (`Host.CreateDefaultBuilder()`) + `IAsyncDisposable` |
| Test doubles | `InMemoryPubSub` (replaces `RedisPubSub`), `InMemoryEventRepository` (replaces `CouchbaseEventRepository`) |
| What tests verify | Events stored with correct type and payload, unique IDs, all event channels covered, command messages excluded |

---

## 14. Anti-Patterns to Avoid

### Directly instantiating business logic in tests

```csharp
// BAD — bypasses DI, doesn't test wiring
var service = new EventPersistenceService(mockSubscriber, mockRepo, mockLogger);
await service.StartAsync(CancellationToken.None);
```

This tests the service class in isolation but misses DI misconfiguration, missing registrations, incorrect lifetimes, and hosted service startup ordering.

### Using mocking frameworks for infrastructure boundaries

```csharp
// BAD — fragile, tests implementation details
var mockPubSub = new Mock<IMessagePublisher>();
mockPubSub.Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<object>()))
    .Returns(Task.CompletedTask);
```

Mocks verify that specific methods were called with specific arguments. If the implementation changes how it calls the interface (e.g., batching, different overload), the mock breaks even though the behaviour is correct. In-memory test doubles verify **outcomes**, not **interactions**.

### Skipping the real ConfigureServices

```csharp
// BAD — manually wiring everything defeats the purpose
_host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IEventRepository>(EventRepository);
        services.AddHostedService<EventPersistenceService>();
        // ... manually registering everything
    })
    .Build();
```

If someone adds a new dependency to the production `ConfigureServices` and forgets to add it here, the test passes but production fails. Always call the real extension method.

### Using real infrastructure in acceptance tests

```csharp
// BAD — requires running Redis, fails in CI without Docker
services.AddSingleton<IMessagePublisher>(new RedisPubSub(realOptions));
```

This couples the test to infrastructure availability and introduces network-related flakiness. Reserve real infrastructure for narrow integration tests of the specific adapter (e.g., testing `RedisPubSub` against a real Redis).

### Not cleaning up the host

```csharp
// BAD — hosted services keep running, ports leak, tests interfere
[Test]
public async Task MyTest()
{
    var factory = new MyFactory();
    await factory.InitializeAsync();
    // ... assertions ...
    // Missing: await factory.DisposeAsync();
}
```

Always use `[TearDown]`, `using`, or `await using` to ensure the host is stopped and disposed.

---

## 15. Checklist

Use this checklist when adding acceptance tests to a new project.

**Project preparation:**

- [ ] All external dependencies are accessed through interfaces
- [ ] Services are registered via a `ConfigureServices` extension method on `IServiceCollection`
- [ ] `public partial class Program { }` is declared in `Program.cs` (Web projects only)
- [ ] The `ConfigureServices` method registers the concrete production implementation AND the interface

**Test project setup:**

- [ ] Test project created as `tests/YourProject.AcceptanceTests/`
- [ ] NuGet packages: `NUnit`, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`
- [ ] `Microsoft.AspNetCore.Mvc.Testing` added (Web projects only)
- [ ] Project references: the project under test and Shared
- [ ] Added to solution file under `/tests/` folder

**Test doubles:**

- [ ] In-memory implementation created for each external dependency interface
- [ ] Test doubles use thread-safe collections (`Concurrent*`, `Channel<T>`)
- [ ] Test doubles expose state for assertion (public read-only properties)
- [ ] `InMemoryPubSub` reused from Shared (not duplicated)

**Test factory:**

- [ ] Factory calls the real `ConfigureServices` extension method
- [ ] Factory removes **both** the concrete type AND the interface registrations
- [ ] Factory registers test double instances as singletons
- [ ] Factory exposes test double instances as public properties
- [ ] Factory manages host lifecycle (`StartAsync` / `StopAsync` or `CreateClient` / `Dispose`)

**Tests:**

- [ ] Each test gets a fresh factory instance (no shared state between tests)
- [ ] Async assertions use `TaskCompletionSource` with `RunContinuationsAsynchronously`
- [ ] Timeouts are applied to all async waits (`WaitAsync(TimeSpan.FromSeconds(5))`)
- [ ] Negative assertions use a "canary" message to prove the system is running
- [ ] Tests verify observable **outcomes**, not internal implementation details
