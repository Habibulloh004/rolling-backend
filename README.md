dotnet restore Rolling.Web/Rolling.Web.csproj
dotnet build Rolling.Web/Rolling.Web.csproj -c Debug
ASPNETCORE_ENVIRONMENT=Development dotnet run --project Rolling.Web/Rolling.Web.csproj

ASPNETCORE_ENVIRONMENT=Development dotnet watch --project rolling-back/Rolling.Web/Rolling.Web.csproj



# Rolling

A clean-architecture ASP.NET Core MVC backend that exposes structured HTTP endpoints, persists events in Redis, and streams realtime updates over raw WebSockets via an in-memory event bus.

## Projects

- `Rolling.Domain` – pure domain model (entities, invariants).
- `Rolling.Application` – use-case layer with notification services, DTOs, and event contracts.
- `Rolling.Infrastructure` – Redis persistence, event bus implementation, and system clock wiring.
- `Rolling.Web` – MVC/UI host exposing HTTP + WebSocket endpoints.

## Features

- MVC UI backed by `INotificationService` to list notifications and create new ones.
- `/api/notifications` REST endpoints for programmatic access.
- Raw WebSocket endpoint at `/ws/notifications` streaming integration events to connected clients.
- Redis-backed repository that keeps the latest N notifications (configurable via `Redis:HistorySize`).
- In-memory event bus dispatching `NotificationCreatedIntegrationEvent` to the WebSocket broadcaster.

## SMS login verification

- Eskiz credentials, sender id, and SMS templates are stored in `appsettings*.json` under the `Sms` section so they can be updated without code changes.
- `/api/sms/token` refreshes the Eskiz token and returns it for health/debug purposes.
- `/api/sms/login/request` issues a four-digit verification code for a login attempt, reusing the template stored in configuration and pushing it through Eskiz; responses always include a `status` and machine-friendly `code`.
- `/api/sms/login/verify` validates the code that the user typed in and also responds with explicit status + code pairs so clients can tell whether a code was invalid, expired, or missing.

## Getting started

1. Ensure .NET 9 SDK and Redis are available locally.
2. Update `Rolling.Web/appsettings*.json` or environment variables for the `Redis` section if needed.
3. Run the web host:

   ```bash
   dotnet run --project Rolling.Web
   ```

4. Navigate to `https://localhost:5001` (or the printed URL) to use the UI. Open multiple tabs to observe realtime fan-out.
5. Use `wscat`/`curl`/etc. against `/ws/notifications` or `/api/notifications` for automated scenarios.

## Configuration

| Setting | Description | Default |
| --- | --- | --- |
| `Redis:ConnectionString` | Connection string passed to `StackExchange.Redis`. | `localhost:6379` |
| `Redis:NotificationsKey` | List key used to persist notifications. | `rolling:notifications` |
| `Redis:HistorySize` | Max number of notifications kept in Redis and displayed in the UI. | `100` |

## Testing

The solution uses integration tests via manual verification today. `dotnet build` validates the full tree. Add xUnit projects under the solution if automated coverage is required.
