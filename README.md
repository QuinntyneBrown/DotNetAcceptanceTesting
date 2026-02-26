# DotNetAcceptanceTesting

A distributed microservices system for mission management and telemetry ingestion, demonstrating modern .NET acceptance testing patterns.

## Architecture

The system consists of three services communicating via pub/sub messaging (Redis or in-memory), following a command-query-event pattern:

```
                         ┌──────────────────────┐
  Client ──REST──────────▶  FrontendGateway.Api  │
                         └───────┬──────────────┘
                                 │ pub/sub
              ┌──────────────────┼──────────────────┐
              ▼                  ▼                   ▼
     ┌────────────────┐  ┌──────────────┐  ┌────────────────┐
     │ ClientEventHub │  │   (Backend)  │  │ TelemetryIngest│
     │   (SignalR)    │  │              │  │    (UDP)       │
     └────────────────┘  └──────────────┘  └────────────────┘
              │                                     ▲
              ▼                                     │
        Browser/Client                        UDP Packets
        (real-time events)                   (CCSDS Space Packets)
```

### Services

**FrontendGateway.Api** — REST API gateway for mission CRUD operations. Publishes commands/queries over pub/sub and waits for responses with a correlation-based request/reply pattern.

**ClientEventHub** — SignalR hub that subscribes to domain events (mission created/updated/deleted) and broadcasts them to connected clients via group-based subscriptions.

**TelemetryIngest** — Receives CCSDS space packets over UDP, parses the primary header (APID, sequence count, flags), and publishes telemetry messages to channels keyed by Application Process Identifier.

**Shared** — Common library containing message definitions (commands, queries, events, telemetry), pub/sub abstractions (`IMessagePublisher`/`IMessageSubscriber`), Redis and in-memory implementations, CCSDS packet parser, and channel constants.

## Tech Stack

- .NET 9 / C# 13
- ASP.NET Core (Controllers, SignalR, OpenAPI)
- Redis (StackExchange.Redis) for pub/sub messaging
- Serilog for structured logging
- CCSDS 133.0-B-2 space packet protocol
- xUnit + WebApplicationFactory for acceptance tests

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Redis (for running services outside of tests)

### Build

```bash
dotnet build FrontendGateway.slnx
```

### Run Tests

```bash
dotnet test FrontendGateway.slnx
```

Tests use in-memory pub/sub and test doubles — no Redis or network dependencies required.

### Run Services

Each service can be started individually:

```bash
dotnet run --project src/FrontendGateway.Api
dotnet run --project src/ClientEventHub
dotnet run --project src/TelemetryIngest
```

Requires a local Redis instance on `localhost:6379`.

## Configuration

All services share common configuration in their `appsettings.json`:

| Setting | Default | Description |
|---|---|---|
| `RedisPubSub:ConnectionString` | `localhost:6379` | Redis connection string |
| `AllowedOrigins` | `http://localhost:4200` | CORS allowed origins |

TelemetryIngest has an additional setting:

| Setting | Default | Description |
|---|---|---|
| `UdpReceiver:Port` | `6000` | UDP listening port for telemetry packets |

## Testing Approach

The project demonstrates acceptance testing of distributed services without external dependencies:

- **WebApplicationFactory** hosts each service in-process for realistic HTTP/SignalR testing
- **InMemoryPubSub** replaces Redis, keeping tests fast and deterministic
- **InMemoryPacketReceiver** replaces the UDP socket in TelemetryIngest tests, feeding packets via `Channel<byte[]>`
- **Correlation IDs** and `TaskCompletionSource<T>` enable assertion on async message flows

### Test Projects

| Project | Covers |
|---|---|
| `Shared.Tests` | CCSDS packet parsing, in-memory pub/sub behavior |
| `FrontendGateway.Api.AcceptanceTests` | REST endpoints, command publishing, request/response correlation |
| `ClientEventHub.AcceptanceTests` | SignalR event broadcasting, group subscriptions, multi-client scenarios |
| `TelemetryIngest.AcceptanceTests` | Packet ingestion, APID routing, idle packet filtering, data encoding |

## Project Structure

```
src/
├── Shared/                    # Messages, pub/sub, CCSDS parser
├── FrontendGateway.Api/       # REST API gateway
├── ClientEventHub/            # SignalR real-time event hub
└── TelemetryIngest/           # UDP telemetry receiver

tests/
├── Shared.Tests/
├── FrontendGateway.Api.AcceptanceTests/
├── ClientEventHub.AcceptanceTests/
└── TelemetryIngest.AcceptanceTests/
```
