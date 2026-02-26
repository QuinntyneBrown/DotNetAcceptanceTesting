# Instructions: Generating Acceptance Test Pattern Documentation from Source Code

## Purpose

You are a language model tasked with analyzing .NET source code and producing a comprehensive **Acceptance Test Patterns** document for that specific codebase. The output document must give a developer everything they need to understand, implement, and maintain acceptance tests for every project in the solution — without requiring any external infrastructure to run the tests.

This instruction document teaches you:

1. How to analyze source code to extract the information you need
2. What acceptance testing patterns to look for and recommend
3. How to structure the output document
4. What level of detail to include
5. How to tailor recommendations to the specific codebase

---

## Table of Contents

1. [Your Role and Goal](#1-your-role-and-goal)
2. [Key Concepts You Must Understand](#2-key-concepts-you-must-understand)
3. [Phase 1: Source Code Analysis](#3-phase-1-source-code-analysis)
4. [Phase 2: Classify Each Project](#4-phase-2-classify-each-project)
5. [Phase 3: Identify External Boundaries](#5-phase-3-identify-external-boundaries)
6. [Phase 4: Evaluate Interface Coverage](#6-phase-4-evaluate-interface-coverage)
7. [Phase 5: Design Test Doubles](#7-phase-5-design-test-doubles)
8. [Phase 6: Design Test Factories](#8-phase-6-design-test-factories)
9. [Phase 7: Design Test Cases](#9-phase-7-design-test-cases)
10. [Phase 8: Assemble the Output Document](#10-phase-8-assemble-the-output-document)
11. [Output Document Template](#11-output-document-template)
12. [Reference: Patterns Catalog](#12-reference-patterns-catalog)
13. [Reference: Async Assertion Strategies](#13-reference-async-assertion-strategies)
14. [Reference: Anti-Patterns to Flag](#14-reference-anti-patterns-to-flag)
15. [Worked Example](#15-worked-example)
16. [Quality Checklist](#16-quality-checklist)

---

## 1. Your Role and Goal

### What you produce

Given the source code of a .NET solution, you produce a Markdown document titled **"Acceptance Testing Patterns for [Solution Name]"** that:

- Explains the acceptance testing strategy tailored to the solution's specific architecture
- Identifies every project that needs acceptance tests
- Maps every external dependency to a recommended in-memory test double
- Provides concrete code examples for test factories, test doubles, and test cases using the actual types, interfaces, and class names found in the source code
- Includes a step-by-step implementation guide
- Lists anti-patterns to avoid, specific to the patterns found in the code
- Ends with an implementation checklist

### What you do NOT produce

- You do not write or generate the actual test code files (`.cs` files). You produce documentation only.
- You do not modify or suggest modifications to the source code unless an interface is missing and must be introduced for testability.
- You do not generate integration tests or end-to-end tests. Your scope is acceptance tests only.

### Definition: Acceptance Test

An acceptance test verifies that a **single project behaves correctly as a whole** by exercising the real DI container, real middleware pipeline, real hosted services, and real business logic — with **only external boundaries** (databases, message brokers, network sockets, third-party HTTP APIs) replaced by lightweight in-memory doubles. Acceptance tests require zero external infrastructure and run entirely in-process.

---

## 2. Key Concepts You Must Understand

Before analyzing code, internalize these foundational concepts. They form the vocabulary and mental model you will use throughout your analysis.

### 2.1 The Dependency Inversion Principle (DIP)

> High-level modules should not depend on low-level modules. Both should depend on abstractions.

In practice, this means production code references interfaces (`IMessagePublisher`, `IEventRepository`) rather than concrete implementations (`RedisPubSub`, `CouchbaseEventRepository`). Test harnesses substitute in-memory implementations for those interfaces.

**When analyzing code:** Look for whether dependencies are accessed through interfaces. If a service directly instantiates a database client or HTTP client without an abstraction, flag this as a **testability gap** and recommend introducing an interface.

### 2.2 The Two Test Harness Patterns

.NET projects fall into two categories, each requiring a different test harness pattern:

| Project Type | SDK | Host | Test Harness Pattern |
|---|---|---|---|
| Web API, SignalR, gRPC, Blazor Server | `Microsoft.NET.Sdk.Web` | ASP.NET Core (Kestrel) | `WebApplicationFactory<Program>` |
| Worker Service, Console app with Generic Host | `Microsoft.NET.Sdk.Worker` or `Microsoft.NET.Sdk` | Generic Host | Manual `Host.CreateDefaultBuilder()` + `IAsyncDisposable` |

**When analyzing code:** Check the `<Project Sdk="...">` element in each `.csproj` to determine which pattern applies.

### 2.3 In-Memory Test Doubles

A test double is a lightweight, in-process implementation of an interface that replaces a production dependency. Test doubles:

- Use thread-safe collections (`ConcurrentDictionary`, `ConcurrentBag`, `Channel<T>`)
- Expose internal state via public read-only properties for test assertions
- Preserve the behavioral contract of the interface (e.g., if the production impl serializes to JSON, the test double should too)
- Are deterministic and require no external resources

### 2.4 Service Registration and Substitution

Production code registers dependencies in a `ConfigureServices` extension method on `IServiceCollection`. The test factory calls this same method, then removes and replaces infrastructure registrations using `RemoveAll<T>()` followed by `AddSingleton<T>(instance)`. Both the concrete type and the interface registration must be removed to prevent the DI container from instantiating the production implementation.

### 2.5 Async Assertion Patterns

Message-driven and event-driven systems process work asynchronously on background threads. Tests must use appropriate async assertion strategies:

- **TaskCompletionSource** with `RunContinuationsAsynchronously` for event-driven assertions
- **Polling with deadline** for state-based assertions
- **Canary message** technique for proving negatives (that something was NOT processed)
- **`WaitAsync(TimeSpan)`** on all async waits to prevent indefinite hanging

---

## 3. Phase 1: Source Code Analysis

### 3.1 Gather structural information

Analyze the solution to build a map of all projects and their relationships. For each project, extract:

| Information | Where to Find It |
|---|---|
| Project name | Directory name and `.csproj` filename |
| Project SDK | `<Project Sdk="...">` in the `.csproj` |
| Target framework | `<TargetFramework>` in the `.csproj` |
| NuGet dependencies | `<PackageReference>` elements in the `.csproj` |
| Project references | `<ProjectReference>` elements in the `.csproj` |
| Entry point | `Program.cs` |
| DI registration | Look for a `ConfigureServices.cs` or similar extension method class |
| Controllers / Endpoints | Classes inheriting `ControllerBase`, minimal API `MapGet`/`MapPost`, or SignalR `Hub` |
| Hosted services | Classes inheriting `BackgroundService` or implementing `IHostedService` |
| Interfaces | All `interface` declarations, especially for external boundaries |
| Concrete implementations | Classes implementing those interfaces |

### 3.2 Gather solution-level information

| Information | Where to Find It |
|---|---|
| Solution file | `.sln` or `.slnx` in the root |
| Solution folder structure | How projects are organized (`src/`, `tests/`, `shared/`) |
| Shared libraries | Projects referenced by multiple other projects |
| Existing test projects | Projects ending in `.Tests` or `.AcceptanceTests` |
| README or docs | `README.md`, `docs/` folder |

### 3.3 Build a dependency graph

For each project, trace the chain of project references to understand which shared libraries and abstractions are available. This determines which test doubles can be reused versus which must be created per-project.

```
Example dependency graph:

FrontendGateway.Api → Shared
ClientEventHub      → Shared
TelemetryIngest     → Shared
EventStore          → Shared

Shared provides: IMessagePublisher, IMessageSubscriber, InMemoryPubSub
```

---

## 4. Phase 2: Classify Each Project

For each project that contains business logic (exclude shared libraries and existing test projects), classify it:

### Classification Table

Produce a table with these columns for each project:

| Column | Description |
|---|---|
| **Project Name** | The project directory and assembly name |
| **Project SDK** | `Microsoft.NET.Sdk.Web` or `Microsoft.NET.Sdk.Worker` or `Microsoft.NET.Sdk` |
| **Host Type** | ASP.NET Core, Generic Host, or None |
| **Test Harness Pattern** | WebApplicationFactory or Generic Host Factory |
| **Input Channels** | How data enters the project (HTTP requests, PubSub subscriptions, UDP packets, timer triggers, file watchers) |
| **Output Channels** | How data leaves the project (PubSub publishes, database writes, HTTP responses, SignalR broadcasts, file writes) |
| **External Dependencies** | Concrete classes that talk to external systems |

### How to determine the host type

1. **ASP.NET Core**: The `.csproj` uses `Microsoft.NET.Sdk.Web`, or `Program.cs` calls `WebApplication.CreateBuilder()` or `WebHost.CreateDefaultBuilder()`
2. **Generic Host**: `Program.cs` calls `Host.CreateApplicationBuilder()` or `Host.CreateDefaultBuilder()`, and the SDK is `Microsoft.NET.Sdk.Worker` or `Microsoft.NET.Sdk`
3. **None**: Simple console app or library without a host. These are uncommon for services but may exist. They require custom test harness approaches.

### How to identify input channels

Scan for:
- Controller actions, minimal API endpoints, gRPC service methods → **HTTP input**
- `IMessageSubscriber.SubscribeAsync` calls → **PubSub input**
- `IPacketReceiver.ReceiveAsync` or socket read calls → **Network input**
- `BackgroundService.ExecuteAsync` with timer loops → **Timer input**
- File system watchers → **File input**

### How to identify output channels

Scan for:
- `IMessagePublisher.PublishAsync` calls → **PubSub output**
- Repository `.SaveAsync`, `.StoreAsync`, `.InsertAsync` calls → **Database output**
- `IHubContext.Clients.Group(...).SendAsync` → **SignalR output**
- `HttpClient.PostAsync`, `SendAsync` → **HTTP output**
- File write operations → **File output**

---

## 5. Phase 3: Identify External Boundaries

For each project, list every external boundary — the seam between your code and an external system. This is the most critical step because these boundaries are exactly what test doubles will replace.

### Boundary identification rules

An external boundary is any interaction with a system that:

1. **Requires infrastructure to be running** (database server, message broker, network endpoint)
2. **Involves network I/O** (TCP, UDP, HTTP, WebSocket)
3. **Involves file system I/O** that is non-deterministic or has side effects
4. **Involves time-dependent behavior** (system clock, timers)

### For each boundary, record

| Field | Description |
|---|---|
| **External System** | What the boundary connects to (Redis, PostgreSQL, RabbitMQ, UDP endpoint, third-party API) |
| **Direction** | Input, Output, or Both |
| **Interface** | The abstraction used (e.g., `IMessagePublisher`) — or "MISSING" if no interface exists |
| **Production Implementation** | The concrete class (e.g., `RedisPubSub`) |
| **Registration Location** | Where in the DI setup it is registered |
| **Thread Safety Needs** | Whether the test double must handle concurrent access |

### When an interface is missing

If production code directly instantiates an external client (e.g., `new HttpClient()`, `new SqlConnection(...)`) without an abstraction:

1. **Flag it as a testability gap** in your output document
2. **Recommend introducing an interface** with the minimal surface area needed
3. **Specify what the interface should look like** based on how the code currently uses the dependency
4. **Show how the production registration would change**

Example recommendation:

```
TESTABILITY GAP: OrderService directly creates SqlConnection in ProcessOrder().

Recommended interface:
    public interface IOrderRepository
    {
        Task<Order> GetByIdAsync(int orderId);
        Task SaveAsync(Order order);
    }

This interface should be extracted from the SQL operations in OrderService.ProcessOrder()
and OrderService.GetOrderStatus(). The production implementation (SqlOrderRepository)
wraps the existing SQL logic. Register via DI so the test factory can substitute it.
```

---

## 6. Phase 4: Evaluate Interface Coverage

For each external boundary, evaluate whether the existing interface is suitable for acceptance testing.

### Interface quality criteria

| Criterion | Good | Bad |
|---|---|---|
| **Granularity** | Interface covers a single external system | Interface mixes multiple concerns |
| **Async support** | All methods return `Task` or `Task<T>` | Synchronous methods that block |
| **Testability** | Methods accept and return serializable types | Methods expose infrastructure-specific types (e.g., `IDatabase` from StackExchange.Redis) |
| **Completeness** | Interface covers all operations the code needs | Production code also calls methods not on the interface |

### Shared vs per-project test doubles

If multiple projects use the same interface (e.g., `IMessagePublisher`), the test double should live in a shared location (shared library or shared test utilities project) and be reused. Do not recommend duplicating test doubles across projects.

Build a reusability map:

```
InMemoryPubSub (shared) → Used by: FrontendGateway.Api, ClientEventHub, TelemetryIngest, EventStore
InMemoryPacketReceiver   → Used by: TelemetryIngest only
InMemoryEventRepository  → Used by: EventStore only
```

---

## 7. Phase 5: Design Test Doubles

For each external boundary interface, design an in-memory test double. Your output document must include:

### 7.1 Test double specification

For each test double, describe:

| Field | Description |
|---|---|
| **Class name** | Follow the naming convention `InMemory{Concept}` (e.g., `InMemoryEventRepository`) |
| **Implements** | The interface(s) it implements |
| **Internal storage** | The thread-safe collection type and why it was chosen |
| **Exposed properties** | Public properties for test assertions and test input |
| **Behavioral fidelity** | Any production behavior the test double must replicate (e.g., JSON serialization round-trip) |
| **Location** | Whether it goes in the shared library (reused) or the specific test project |

### 7.2 Collection type selection guide

Use this decision tree when recommending a collection type:

```
Is the test double a message/event handler (pub/sub pattern)?
  → ConcurrentDictionary<string, List<Func<string, Task>>> for handler registration
  → JSON serialization round-trip to match production behavior

Is the test double a repository/store (append-only)?
  → ConcurrentBag<T> for thread-safe adds
  → Expose as IReadOnlyList<T> via .ToList() snapshot

Is the test double a streaming source (blocking read)?
  → System.Threading.Channels.Channel<T> for producer/consumer semantics
  → Expose ChannelWriter<T> for tests to push data

Is the test double a request/response client (e.g., HTTP)?
  → ConcurrentDictionary<string, T> or ConcurrentQueue<T>
  → Allow tests to pre-configure responses
```

### 7.3 Behavioral fidelity requirements

Some test doubles must replicate specific production behavior to catch real bugs:

| Production Behavior | Test Double Must | Why |
|---|---|---|
| JSON serialization | Serialize and deserialize through the same envelope/format | Catches missing properties, wrong types, enum serialization, naming policy mismatches |
| Connection string parsing | Not replicate | Not relevant to business logic |
| Retry policies | Not replicate | Tests should be deterministic |
| Batching / buffering | Replicate if business logic depends on batch boundaries | Otherwise skip |

---

## 8. Phase 6: Design Test Factories

For each project that needs acceptance tests, design the test factory.

### 8.1 WebApplicationFactory pattern (for Web projects)

```csharp
public sealed class {ProjectName}Factory : WebApplicationFactory<Program>
{
    // Declare fields for test doubles that need to be initialized during ConfigureWebHost
    private {TestDoubleType}? _{fieldName};

    // Public property to expose each test double to tests
    public {TestDoubleType} {PropertyName} =>
        _{fieldName} ?? throw new InvalidOperationException("Factory not initialized.");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // For each external boundary:
            // 1. Remove the concrete production type
            services.RemoveAll<{ConcreteProductionType}>();
            // 2. Remove the interface registration
            services.RemoveAll<{InterfaceType}>();
            // 3. Create and register the test double
            var testDouble = new {TestDoubleType}();
            _{fieldName} = testDouble;
            services.AddSingleton<{InterfaceType}>(testDouble);
        });
    }
}
```

**Document these specifics for each factory:**
- Which production types to remove (list every `RemoveAll<T>()` call)
- Which test doubles to create and register
- Any special initialization (e.g., Serilog logger for InMemoryPubSub)
- Whether `public partial class Program { }` exists in the project's `Program.cs` (flag if missing)

### 8.2 Generic Host Factory pattern (for Worker projects)

```csharp
public sealed class {ProjectName}Factory : IAsyncDisposable
{
    private IHost _host = default!;

    // Public properties for test doubles
    public {TestDoubleType} {PropertyName} { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        // Create test doubles
        {PropertyName} = new {TestDoubleType}();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Call the real ConfigureServices extension method
                services.Add{ProjectName}Services(context.Configuration);

                // Substitute each external boundary
                services.RemoveAll<{ConcreteProductionType}>();
                services.RemoveAll<{InterfaceType}>();
                services.AddSingleton<{InterfaceType}>({PropertyName});
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

**Document these specifics for each factory:**
- The exact `ConfigureServices` extension method name to call
- The complete list of `RemoveAll` and `AddSingleton` calls in order
- Why each concrete type and interface must both be removed

### 8.3 Factory design rules

1. **Always call the real ConfigureServices.** Never manually re-register business logic. The factory's only job is to substitute external boundaries.
2. **Remove both concrete and interface registrations.** If you only remove the interface, the DI container may still instantiate the concrete production class (which may attempt to connect to infrastructure).
3. **Expose test doubles as public properties.** Tests need direct access to push input data and read output state.
4. **Create fresh test doubles per factory instance.** Do not share test double instances across tests.

---

## 9. Phase 7: Design Test Cases

For each project, design the set of acceptance test cases that verify its behavior.

### 9.1 Test case derivation strategy

Derive test cases from the project's **observable behaviors**, not its internal implementation. For each input-to-output path through the project:

```
INPUT (what triggers the behavior)
  → PROCESSING (what the project does internally — you don't test this directly)
    → OUTPUT (what observable effect the behavior produces)
```

**For each path, create at least one test case.**

### 9.2 Input-to-output path analysis

Trace every path through the project:

| Input Type | How to Find Paths |
|---|---|
| HTTP endpoint | Each controller action or minimal API endpoint is a path. Include different HTTP methods (GET, POST, PUT, DELETE) and significant parameter variations. |
| PubSub subscription | Each `SubscribeAsync` call in a hosted service registers a handler. Each subscribed channel is a path. |
| Packet/stream input | Each type of packet or message the service processes is a path. Include edge cases (malformed, idle, oversized). |
| Timer tick | Each timer-triggered action is a path. |

For each path, identify:

| Field | Description |
|---|---|
| **Trigger** | What starts the behavior (HTTP request, published message, received packet) |
| **Expected output** | What the project should produce (HTTP response, published message, stored record, SignalR broadcast) |
| **Side effects** | Any additional observable effects beyond the primary output |
| **Edge cases** | Error conditions, missing data, boundary values, filtering (e.g., idle packets skipped) |

### 9.3 Test case categories

For each project, ensure you cover these categories:

#### Happy path tests
- Each input type produces the expected output
- All fields are correctly mapped and propagated

#### Routing / channel tests
- Messages are published to the correct channel
- Different input variations route to different outputs
- Correlation IDs or routing keys are handled correctly

#### Filtering / exclusion tests
- Invalid or irrelevant input is ignored (e.g., idle packets, command messages when only events should be stored)
- Use the **canary message** technique to prove the filter works

#### Multi-item / sequence tests
- Multiple inputs processed in sequence produce correct outputs
- No state leakage between processed items

#### Subscription / group isolation tests (SignalR / PubSub)
- Subscribed clients receive events
- Unsubscribed clients do not receive events
- Unsubscribing stops delivery
- Multiple clients each receive the same event

#### Error response tests (HTTP)
- Invalid input returns appropriate HTTP status codes
- Missing resources return 404
- Timeout scenarios return 504 or appropriate error

### 9.4 Test naming convention

Use descriptive names that state the scenario and expected outcome:

```
{Trigger}__{expected_outcome}

Examples:
  Post_CreateMission_publishes_command_and_returns_Accepted
  Telemetry_packet_is_parsed_and_published_to_correct_APID_channel
  Idle_packet_is_not_published
  MissionCreatedEvent_is_forwarded_to_subscribed_client
  Client_does_not_receive_events_after_unsubscribing
  Command_messages_are_not_stored
```

### 9.5 Test structure template

Every test follows Arrange-Act-Assert:

```csharp
[Test]
public async Task {Descriptive_test_name}()
{
    // ARRANGE — Set up subscriptions, prepare input data, configure expectations
    // (Subscribe to output channels BEFORE triggering input)

    // ACT — Drive input through the test double or HTTP client
    // (One action per test — the single behavior being verified)

    // ASSERT — Verify the output via test double properties or HTTP response
    // (Use async assertion patterns with timeouts)
}
```

---

## 10. Phase 8: Assemble the Output Document

Combine all analysis into a single Markdown document following the template in Section 11. The document must:

1. **Use the actual type names, interface names, and class names** from the source code. Never use placeholder names in the final output.
2. **Include code examples** that reference real types from the codebase. Test factory code examples should show the actual `RemoveAll` and `AddSingleton` calls with real type names.
3. **Be self-contained.** A developer should be able to implement all acceptance tests using only this document and the source code, with no other reference.
4. **Scale to the codebase.** If there are 2 projects, the document might be 400 lines. If there are 10 projects, it might be 1500 lines. Do not artificially constrain or pad length.
5. **Prioritize accuracy over completeness.** It is better to thoroughly document 3 projects than to superficially document 10. If the codebase is very large, focus on the projects with the most complex external boundaries.

---

## 11. Output Document Template

Use this template as the skeleton for your output. Replace all `{placeholders}` with information derived from your analysis. Add or remove sections as needed based on the specific codebase.

````markdown
# Acceptance Testing Patterns for {Solution Name}

## Table of Contents

1. [Introduction](#1-introduction)
2. [What Are Acceptance Tests?](#2-what-are-acceptance-tests)
3. [Why Acceptance Tests Over Integration or End-to-End Tests](#3-why-acceptance-tests-over-integration-or-end-to-end-tests)
4. [Technologies](#4-technologies)
5. [Core Principle: Dependency Inversion and Interface Substitution](#5-core-principle-dependency-inversion-and-interface-substitution)
6. [The Two Test Harness Patterns](#6-the-two-test-harness-patterns)
7. [{For each harness pattern used, include a dedicated section}](#7-)
8. [Building In-Memory Test Doubles](#8-building-in-memory-test-doubles)
9. [Test Structure and Lifecycle](#9-test-structure-and-lifecycle)
10. [Assertion Patterns for Asynchronous Systems](#10-assertion-patterns-for-asynchronous-systems)
11. [Step-by-Step Guide: Adding Acceptance Tests to a New Project](#11-step-by-step-guide)
12. [Reference: All Projects in This Solution](#12-reference-all-projects)
13. [Anti-Patterns to Avoid](#13-anti-patterns-to-avoid)
14. [Checklist](#14-checklist)

---

## 1. Introduction

{One or two paragraphs describing the specific solution, its architecture, and the
external systems it depends on. State that all acceptance tests run in-process with
zero infrastructure. Mention the key enabler: Dependency Inversion Principle.}

---

## 2. What Are Acceptance Tests?

{Include the standard definition and the testing pyramid table showing Unit,
Acceptance, Integration, and End-to-End test levels. This section is mostly
standard but should reference the specific external systems in this solution.}

---

## 3. Why Acceptance Tests Over Integration or End-to-End Tests

{Explain the problems with requiring real infrastructure, tailored to the specific
external systems in this solution (e.g., "every developer would need a running
Redis instance and a Couchbase cluster").

Then explain what acceptance tests provide: zero infrastructure, deterministic
results, fast feedback, refactoring confidence, documentation.

Then explain what acceptance tests do NOT replace: narrow integration tests for
the actual infrastructure adapters.}

---

## 4. Technologies

{List the specific frameworks, packages, and versions found in the codebase.
Include tables for:
- Test framework and supporting packages (NUnit/xUnit/MSTest, version, purpose)
- Test host packages (WebApplicationFactory, Generic Host)
- In-memory test doubles (one row per test double, listing what it replaces)}

---

## 5. Core Principle: Dependency Inversion and Interface Substitution

{Show the interface-to-implementation mapping for this specific codebase:

  InterfaceName → ProductionImpl (production) / InMemoryImpl (test)

Explain how substitution works in the DI container with code examples using
the actual ConfigureServices code from the codebase.

Explain why both the concrete type AND the interface must be removed.}

---

## 6. The Two Test Harness Patterns

{Summary table showing which projects use which pattern.
Only include patterns that are actually needed by the codebase.
If all projects are Web projects, omit the Generic Host pattern section.}

---

## 7+. {Pattern sections — one per harness pattern used}

{For each harness pattern (WebApplicationFactory and/or Generic Host), include:
- When to use it
- What it provides
- Complete code example of the factory class using actual type names
- How tests interact with the factory (code example)
- Required NuGet packages
- Required declarations in the project under test (e.g., public partial class Program)}

---

## N. Building In-Memory Test Doubles

{For each test double needed:
- Class name
- Interface(s) it implements
- Internal storage mechanism and why it was chosen
- Complete code skeleton or pseudocode
- Why specific design choices were made (JSON round-trip, ConcurrentBag, Channel<T>)
- Where it should live (shared vs per-test-project)}

---

## N+1. Test Structure and Lifecycle

{Show the NUnit/xUnit/MSTest lifecycle pattern with SetUp/TearDown.
Include code examples for both WebApplicationFactory and Generic Host patterns
if both are used. Emphasize: each test gets a fresh factory instance.}

---

## N+2. Assertion Patterns for Asynchronous Systems

{Include each async assertion pattern that applies to this codebase:
- TaskCompletionSource pattern (if PubSub or event-driven output exists)
- Polling pattern (if repository/state-based output exists)
- Canary message pattern (if filtering/exclusion tests are needed)
Show code examples using actual types from the codebase.}

---

## N+3. Step-by-Step Guide: Adding Acceptance Tests to a New Project

{Provide a concrete step-by-step guide:
1. Identify external boundaries
2. Create the test project (.csproj with correct packages)
3. Build in-memory test doubles (or reuse existing ones)
4. Create the test factory
5. Write acceptance tests
6. Add to solution
Include code templates with actual type names where possible.}

---

## N+4. Reference: All Projects in This Solution

{For each project, include a reference table with:
- SDK
- Purpose (one sentence)
- External dependencies
- Test harness pattern
- Test doubles needed
- What tests should verify (list of behaviors)}

---

## N+5. Anti-Patterns to Avoid

{Include anti-patterns with code examples showing the BAD way and explaining why.
Standard anti-patterns to always include:
1. Directly instantiating business logic (bypasses DI)
2. Using mocking frameworks for infrastructure boundaries (fragile, tests interactions not outcomes)
3. Skipping the real ConfigureServices (defeats purpose of acceptance testing)
4. Using real infrastructure in acceptance tests (requires running systems, flaky)
5. Not cleaning up the host (resource leaks, test interference)

Add any additional anti-patterns specific to the codebase.}

---

## N+6. Checklist

{Provide a checkbox-style checklist organized into sections:
- Project preparation
- Test project setup
- Test doubles
- Test factory
- Tests}
````

---

## 12. Reference: Patterns Catalog

Use this catalog of proven patterns when designing test doubles and test factories. Select the patterns that apply to the specific codebase you are analyzing.

### 12.1 PubSub / Message Broker Pattern

**Applies when:** The codebase uses Redis PubSub, RabbitMQ, Azure Service Bus, Kafka, or any publish/subscribe messaging system.

**Interface shape:**
```csharp
public interface IMessagePublisher
{
    Task PublishAsync<T>(string channel, T message) where T : class;
}

public interface IMessageSubscriber
{
    Task SubscribeAsync<T>(string channel, Func<T, Task> handler) where T : class;
}
```

**Test double:** `InMemoryPubSub` implementing both interfaces. Uses `ConcurrentDictionary<string, List<Func<string, Task>>>` for handler registration. Round-trips messages through JSON serialization to match production behavior.

**Key design point:** The test double must serialize and deserialize messages (not just pass object references) to catch serialization bugs.

### 12.2 Repository / Database Pattern

**Applies when:** The codebase persists data to a database (Couchbase, PostgreSQL, MongoDB, SQL Server, DynamoDB, etc.).

**Interface shape:**
```csharp
public interface IRepository<T>
{
    Task<T?> GetByIdAsync(string id);
    Task SaveAsync(T entity);
    Task DeleteAsync(string id);
}
```

**Test double:** `InMemoryRepository<T>` or `InMemory{Entity}Repository`. Uses `ConcurrentBag<T>` for append-only stores or `ConcurrentDictionary<string, T>` for key-value stores. Exposes a read-only property for test assertions.

### 12.3 Network Socket / Packet Receiver Pattern

**Applies when:** The codebase receives data over UDP, TCP, or raw sockets.

**Interface shape:**
```csharp
public interface IPacketReceiver
{
    Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
}
```

**Test double:** `InMemoryPacketReceiver` using `System.Threading.Channels.Channel<byte[]>`. The `Channel` provides the same blocking-read semantic as a real socket: `ReadAsync` awaits until data is available or cancellation is requested. Tests push data via the exposed `ChannelWriter<byte[]>`.

### 12.4 HTTP Client Pattern

**Applies when:** The codebase makes outbound HTTP calls to third-party APIs or other microservices.

**Interface shape:**
```csharp
public interface IExternalApiClient
{
    Task<ApiResponse> GetResourceAsync(string id);
    Task PostDataAsync(DataPayload payload);
}
```

**Test double:** `InMemory{Api}Client` using `ConcurrentQueue<T>` for pre-configured responses or `ConcurrentBag<T>` for capturing requests. Tests enqueue expected responses before acting, then dequeue and verify captured requests afterward.

**Alternative:** If the code uses `HttpClient` directly via `IHttpClientFactory`, use a custom `DelegatingHandler` that returns pre-configured responses.

### 12.5 File System Pattern

**Applies when:** The codebase reads from or writes to the file system.

**Interface shape:**
```csharp
public interface IFileStore
{
    Task<byte[]> ReadAsync(string path);
    Task WriteAsync(string path, byte[] content);
}
```

**Test double:** `InMemoryFileStore` using `ConcurrentDictionary<string, byte[]>`. Tests pre-populate files and verify written content.

### 12.6 Clock / Timer Pattern

**Applies when:** The codebase depends on the system clock or uses timers for scheduling.

**Interface shape:**
```csharp
public interface IClock
{
    DateTime UtcNow { get; }
}
```

**Test double:** `FakeClock` with a settable `UtcNow` property. Tests advance time explicitly to trigger time-dependent behavior deterministically.

### 12.7 SignalR Hub Pattern

**Applies when:** The codebase includes SignalR hubs that push real-time events to clients.

**Test approach:** Use `WebApplicationFactory` and create a real `HubConnection` that routes through the `TestServer`:

```csharp
var hubConnection = new HubConnectionBuilder()
    .WithUrl($"{client.BaseAddress}hubs/{hubPath}", options =>
    {
        options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
    })
    .Build();
```

**Key design point:** The `HubConnection` is a real SignalR client. It communicates over the in-memory `TestServer` handler, not over a network socket. This tests the full SignalR pipeline including hub method routing, group management, and serialization.

---

## 13. Reference: Async Assertion Strategies

### 13.1 TaskCompletionSource (event-driven output)

**Use when:** You need to wait for a specific message or event to be produced by the system under test.

```csharp
var received = new TaskCompletionSource<{MessageType}>(
    TaskCreationOptions.RunContinuationsAsynchronously);

await factory.PubSub.SubscribeAsync<{MessageType}>(channel, msg =>
{
    received.TrySetResult(msg);
    return Task.CompletedTask;
});

// ... act ...

var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
Assert.That(result.{Property}, Is.EqualTo(expected));
```

**Why `RunContinuationsAsynchronously`:** Prevents deadlocks when `TrySetResult` is called on a hosted service thread.

**Why `WaitAsync` with timeout:** Prevents indefinite hanging if the expected message is never produced.

### 13.2 Polling with deadline (state-based output)

**Use when:** You need to wait for a side effect to be observable in a test double's state (e.g., items added to a repository).

```csharp
var deadline = DateTime.UtcNow.AddSeconds(5);
while (factory.Repository.Items.Count < expectedCount
       && DateTime.UtcNow < deadline)
{
    await Task.Delay(50);
}

Assert.That(factory.Repository.Items, Has.Count.EqualTo(expectedCount));
```

**Tuning:** The polling interval (50ms) balances test speed against CPU usage. The deadline (5 seconds) provides a safety net.

### 13.3 Canary message (proving the negative)

**Use when:** You need to assert that something was NOT processed (e.g., idle packets are not published, command messages are not stored).

```csharp
// 1. Send the thing that should be ignored
await sendIgnoredInput();

// 2. Send a "canary" — something you know WILL be processed
await sendKnownGoodInput();

// 3. Wait for the canary to be observed
await waitForCanary();

// 4. Assert the ignored input was not processed
Assert.That(observedItems, Has.None.Matches<T>(x => x.IsTheIgnoredItem));
```

**Why this works:** By waiting for the canary, you know the system has had time to process the ignored input if it was going to. This eliminates false passes from timing issues.

---

## 14. Reference: Anti-Patterns to Flag

When analyzing source code, watch for these anti-patterns and flag them in your output document.

### 14.1 In source code (testability gaps)

| Anti-Pattern | What to Look For | Recommendation |
|---|---|---|
| Direct infrastructure instantiation | `new SqlConnection(...)`, `new HttpClient()`, `new RedisConnection()` in business logic | Extract an interface and register through DI |
| Static infrastructure access | `HttpContext.Current`, `DateTime.Now`, `File.ReadAllText()` in business logic | Wrap behind an injectable interface |
| Missing ConfigureServices extension method | Services registered directly in `Program.cs` without a reusable method | Extract into `public static void Add{Project}Services(this IServiceCollection, IConfiguration)` |
| Missing `public partial class Program` | Web projects without this declaration (prevents `WebApplicationFactory<Program>`) | Add the declaration at the bottom of `Program.cs` |
| Tight coupling between projects | Service A directly references and instantiates classes from Service B | Communicate through shared interfaces and messaging |

### 14.2 In test code (bad testing practices)

| Anti-Pattern | Description | Better Alternative |
|---|---|---|
| Direct instantiation of business logic | `new MyService(dep1, dep2)` in tests | Use the test factory to get a properly wired instance |
| Mocking infrastructure interfaces | `Mock<IMessagePublisher>` with `.Setup()` and `.Verify()` | Use an in-memory test double that verifies outcomes |
| Skipping real ConfigureServices | Manually registering all services in the factory | Call the real extension method, only substitute boundaries |
| Real infrastructure in tests | Connecting to an actual database or broker | Use in-memory test doubles |
| Not cleaning up hosts | Missing `DisposeAsync` or `TearDown` | Always dispose in `[TearDown]` or with `using`/`await using` |
| Shared factory across tests | One factory instance for the entire test class | Fresh factory per test for isolation |
| Missing timeouts on async waits | `await tcs.Task` without `WaitAsync` | Always use `WaitAsync(TimeSpan.FromSeconds(5))` |
| Testing internals | Accessing private fields or using reflection | Test observable outputs only |

---

## 15. Worked Example

This section walks through the complete analysis process for a hypothetical project to demonstrate how to apply each phase.

### Source code input

Suppose you receive a Worker service called `NotificationDispatcher` with these characteristics:

- **SDK:** `Microsoft.NET.Sdk.Worker`
- **External dependencies:** RabbitMQ (subscribes to events), SendGrid (sends emails), PostgreSQL (reads user preferences)
- **Hosted service:** `NotificationService` — subscribes to `UserRegistered` and `OrderCompleted` events, looks up user email preferences, sends an email via SendGrid

### Phase 2 output (classification)

| Project | SDK | Host | Harness | Input | Output | External Deps |
|---|---|---|---|---|---|---|
| NotificationDispatcher | Worker | Generic Host | Generic Host Factory | RabbitMQ subscriptions | SendGrid emails | RabbitMQ, SendGrid, PostgreSQL |

### Phase 3 output (boundaries)

| System | Direction | Interface | Production Impl | Thread Safe? |
|---|---|---|---|---|
| RabbitMQ | Input | `IMessageSubscriber` | `RabbitMqSubscriber` | Yes |
| SendGrid | Output | `IEmailSender` | `SendGridEmailSender` | Yes |
| PostgreSQL | Input (read) | `IUserPreferencesRepository` | `PostgresUserPreferencesRepository` | Yes |

### Phase 5 output (test doubles)

**InMemoryPubSub** (reuse from shared if available) — for RabbitMQ

**InMemoryEmailSender:**
```csharp
public class InMemoryEmailSender : IEmailSender
{
    private readonly ConcurrentBag<SentEmail> _sent = new();
    public IReadOnlyList<SentEmail> SentEmails => _sent.ToList();

    public Task SendAsync(string to, string subject, string body)
    {
        _sent.Add(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }
}

public record SentEmail(string To, string Subject, string Body);
```

**InMemoryUserPreferencesRepository:**
```csharp
public class InMemoryUserPreferencesRepository : IUserPreferencesRepository
{
    private readonly ConcurrentDictionary<string, UserPreferences> _prefs = new();

    public void Seed(string userId, UserPreferences prefs) => _prefs[userId] = prefs;

    public Task<UserPreferences?> GetByUserIdAsync(string userId)
    {
        _prefs.TryGetValue(userId, out var prefs);
        return Task.FromResult(prefs);
    }
}
```

### Phase 7 output (test cases)

1. `UserRegistered_event_sends_welcome_email` — Publish `UserRegistered` event, verify `InMemoryEmailSender.SentEmails` contains a welcome email to the correct address.
2. `OrderCompleted_event_sends_confirmation_email` — Publish `OrderCompleted` event with pre-seeded user preferences, verify confirmation email sent.
3. `User_with_email_disabled_does_not_receive_email` — Seed user preferences with emails disabled, publish event, use canary technique, verify no email sent.
4. `Multiple_events_each_produce_an_email` — Publish two events in sequence, verify two emails sent with correct content.
5. `Unknown_user_does_not_cause_error` — Publish event for user not in repository, verify no email sent and service continues processing.

---

## 16. Quality Checklist

Before finalizing your output document, verify:

### Completeness

- [ ] Every project with business logic has a corresponding section
- [ ] Every external boundary is identified and mapped to a test double
- [ ] Every test double has a complete specification (name, interface, storage, properties, location)
- [ ] Every test factory has a complete code example with actual type names
- [ ] Every project has a list of recommended test cases
- [ ] The step-by-step guide uses actual type names from the codebase
- [ ] The anti-patterns section includes examples relevant to this codebase

### Accuracy

- [ ] All type names, interface names, and class names match the source code exactly
- [ ] All `RemoveAll<T>` calls reference the correct concrete and interface types
- [ ] All `ConfigureServices` extension method names match the source code
- [ ] The correct test harness pattern is recommended for each project SDK
- [ ] NuGet package names and versions match the source code

### Usability

- [ ] A developer can implement acceptance tests using only this document and the source code
- [ ] Code examples are complete enough to compile (not pseudocode with `...` elisions for critical sections)
- [ ] The document flows logically from concepts to implementation
- [ ] The checklist at the end covers all implementation steps

### Testability gaps

- [ ] Any missing interfaces are flagged with recommended interface designs
- [ ] Any missing `ConfigureServices` extension methods are flagged
- [ ] Any missing `public partial class Program { }` declarations are flagged
- [ ] Any direct infrastructure access without abstraction is flagged

---

## Appendix A: Glossary

| Term | Definition |
|---|---|
| **Acceptance test** | A test that verifies a single project behaves correctly as a whole, with external boundaries replaced by in-memory doubles |
| **Test double** | An in-memory implementation of an interface that replaces a production dependency in tests |
| **Test factory** | A class that creates and configures the host under test, substituting external boundaries with test doubles |
| **External boundary** | The seam between your code and an external system (database, message broker, network socket, third-party API) |
| **WebApplicationFactory** | ASP.NET Core's built-in test host for Web projects. Creates an in-memory `TestServer` and provides `HttpClient` access |
| **Generic Host Factory** | A custom test host pattern for Worker/non-HTTP projects. Manually builds an `IHost` with substituted dependencies |
| **DIP** | Dependency Inversion Principle — high-level modules depend on abstractions, not concrete implementations |
| **Canary message** | A known-good message sent after a potentially-ignored message, used to prove that the system had time to process (or correctly ignore) the first message |
| **TaskCompletionSource** | A .NET type that creates a `Task` you can manually complete. Used in tests to bridge callback-based async (PubSub handlers) to awaitable assertions |
| **RemoveAll\<T\>** | Extension method from `Microsoft.Extensions.DependencyInjection.Extensions` that removes all DI registrations of a given type |

## Appendix B: NuGet Packages Quick Reference

| Package | Version Range | Purpose | When to Include |
|---|---|---|---|
| `NUnit` | 4.x | Test framework | Always |
| `NUnit3TestAdapter` | 6.x | Connects NUnit to `dotnet test` | Always (with NUnit) |
| `Microsoft.NET.Test.Sdk` | 17.x | Required by all .NET test projects | Always |
| `coverlet.collector` | 6.x | Code coverage | Always |
| `Microsoft.AspNetCore.Mvc.Testing` | Match target framework | WebApplicationFactory | Web projects only |
| `xUnit` | 2.x | Alternative test framework | If codebase uses xUnit |
| `xUnit.runner.visualstudio` | 2.x | xUnit test adapter | If codebase uses xUnit |
| `MSTest.TestFramework` | 3.x | Alternative test framework | If codebase uses MSTest |
| `MSTest.TestAdapter` | 3.x | MSTest test adapter | If codebase uses MSTest |

## Appendix C: Decision Flowchart

Use this flowchart when analyzing each project:

```
START: Analyze project .csproj
  │
  ├─ Is it a test project or shared library?
  │   YES → Skip (do not generate tests for tests or shared libs)
  │   NO  → Continue
  │
  ├─ What SDK does it use?
  │   ├─ Microsoft.NET.Sdk.Web → WebApplicationFactory pattern
  │   ├─ Microsoft.NET.Sdk.Worker → Generic Host Factory pattern
  │   └─ Microsoft.NET.Sdk → Check if it uses Generic Host
  │       ├─ Uses Host.CreateDefaultBuilder → Generic Host Factory pattern
  │       └─ No host → Custom test harness (document case-by-case)
  │
  ├─ Does it have a ConfigureServices extension method?
  │   YES → Reference it in the test factory
  │   NO  → Flag as testability gap, recommend creating one
  │
  ├─ Does Program.cs declare `public partial class Program { }`?
  │   (Web projects only)
  │   YES → Good
  │   NO  → Flag as testability gap, recommend adding it
  │
  ├─ For each external dependency:
  │   ├─ Is there an interface?
  │   │   YES → Design a test double for it
  │   │   NO  → Flag as testability gap, recommend an interface
  │   │
  │   ├─ Can the test double be reused from a shared location?
  │   │   YES → Reference the shared test double
  │   │   NO  → Design a new test double for this test project
  │   │
  │   └─ Is the dependency registered as both concrete type and interface?
  │       YES → RemoveAll both in the factory
  │       NO  → RemoveAll whatever is registered
  │
  └─ Design test cases covering all input-to-output paths
      ├─ Happy paths
      ├─ Routing / channel variations
      ├─ Filtering / exclusion (with canary technique)
      ├─ Multi-item sequences
      └─ Edge cases and error conditions
```
