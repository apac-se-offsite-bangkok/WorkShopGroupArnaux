# AGENTS.md — Copilot Coding Agent Instructions for eShop

This file tells the GitHub Copilot coding agent how to build, test, and contribute to the eShop reference application.

## Project Overview

eShop is a .NET reference e-commerce application using a **microservices architecture** orchestrated by [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/). It targets **.NET 10** (see `global.json`).

### Key Services

| Service | Path | Style | Protocol |
|---------|------|-------|----------|
| Catalog.API | `src/Catalog.API` | Minimal APIs (versioned) | HTTP REST |
| Ordering.API | `src/Ordering.API` | Minimal APIs + MediatR (CQRS) | HTTP REST |
| Basket.API | `src/Basket.API` | gRPC service | gRPC |
| Identity.API | `src/Identity.API` | ASP.NET Identity + Duende IdentityServer | HTTP |
| WebApp | `src/WebApp` | Blazor Server (Razor Components) | HTTP |
| OrderProcessor | `src/OrderProcessor` | Background worker | — |
| PaymentProcessor | `src/PaymentProcessor` | Background worker | — |
| Webhooks.API | `src/Webhooks.API` | Minimal APIs | HTTP REST |
| WebhookClient | `src/WebhookClient` | Blazor Server | HTTP |

### Shared Libraries

| Library | Path | Purpose |
|---------|------|---------|
| eShop.ServiceDefaults | `src/eShop.ServiceDefaults` | OpenTelemetry, health checks, service discovery, resilience |
| EventBus | `src/EventBus` | Integration event abstraction layer |
| EventBusRabbitMQ | `src/EventBusRabbitMQ` | RabbitMQ implementation of EventBus |
| IntegrationEventLogEF | `src/IntegrationEventLogEF` | EF Core-based integration event log |
| Shared | `src/Shared` | Shared DTOs and activity extensions |

### Infrastructure

| Technology | Usage |
|------------|-------|
| PostgreSQL (with pgvector) | Catalog, Identity, Ordering, Webhooks databases |
| Redis | Basket data caching |
| RabbitMQ | Integration event message broker |
| Entity Framework Core | ORM for PostgreSQL databases |

## Building

### Build the entire solution (web projects + tests)

```bash
dotnet build eShop.Web.slnf
```

### Build a specific project

```bash
dotnet build src/Catalog.API/Catalog.API.csproj
```

### Important build notes

- The solution file is `eShop.slnx`; the filtered solution `eShop.Web.slnf` excludes MAUI/mobile projects and is used in CI.
- `Directory.Build.props` sets `TreatWarningsAsErrors` to `true` — all warnings must be fixed.
- Package versions are centrally managed in `Directory.Packages.props` — do NOT add version attributes to individual `.csproj` files; update the central file instead.
- An `.editorconfig` at the repo root enforces formatting: 4-space indent for C#, `utf-8-bom` charset, system usings sorted first.

## Testing

### Test projects

| Project | Framework | Type | Docker Required |
|---------|-----------|------|-----------------|
| `tests/Basket.UnitTests` | MSTest | Unit | No |
| `tests/Ordering.UnitTests` | MSTest | Unit | No |
| `tests/ClientApp.UnitTests` | MSTest | Unit (MAUI) | No |
| `tests/Catalog.FunctionalTests` | xUnit v3 | Functional | **Yes** |
| `tests/Ordering.FunctionalTests` | xUnit v3 | Functional | **Yes** |

### Run all tests

```bash
dotnet test eShop.Web.slnf
```

### Run a specific test project

```bash
dotnet test tests/Ordering.UnitTests/Ordering.UnitTests.csproj
```

### Run only unit tests (no Docker needed)

```bash
dotnet test tests/Basket.UnitTests/Basket.UnitTests.csproj
dotnet test tests/Ordering.UnitTests/Ordering.UnitTests.csproj
```

### Testing conventions

- **Mocking**: Use **NSubstitute** (already referenced in all unit test projects). Do not introduce Moq or other mocking libraries.
- **MSTest projects** use `[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]` for parallel execution.
- **Functional tests** use .NET Aspire's `DistributedApplicationTestingBuilder` to spin up real PostgreSQL containers — these require Docker.
- **Ordering.UnitTests** uses a **builder pattern** (`OrderBuilder`, `AddressBuilder`) for constructing domain test objects.
- Follow **Arrange / Act / Assert** with `// Arrange`, `// Act`, `// Assert` comments.
- Test method naming: describe the scenario clearly (e.g., `Add_basket_item_success`, `Throws_when_quantity_is_zero`).

### E2E tests (Playwright)

End-to-end tests live in `e2e/` and use Playwright + TypeScript. These are not part of `dotnet test` and require a running application.

## Code Architecture & Patterns

### Minimal APIs

All HTTP services use **Minimal APIs** — there are no MVC controllers. Endpoints are organized as:

- A static class per feature area (e.g., `CatalogApi`, `OrdersApi`) with extension methods.
- Mapped via `app.MapGroup("/api/catalog")` in the service's `Program.cs`.
- Use `[AsParameters]` attribute for dependency injection into endpoint handler records.
- Return typed results (`TypedResults.Ok(...)`, `TypedResults.NotFound()`, etc.).

### API Versioning

- Uses `Asp.Versioning` with `ApiVersionSet` and versioned route groups.
- Version format: `'v'VVV` (e.g., `v1.0`, `v2.0`).
- API version passed via query parameter.

### CQRS (Ordering)

- Ordering.API uses **MediatR** for command/query separation.
- Commands are wrapped in `IdentifiedCommand<T>` for idempotency.
- Domain model follows DDD with aggregates in `Ordering.Domain`.

### Integration Events

- Events extend the `IntegrationEvent` base record (has `Id` and `CreationDate`).
- Name events as `{Past-tense verb}{Noun}IntegrationEvent` (e.g., `OrderStartedIntegrationEvent`).
- Handlers implement `IIntegrationEventHandler<TEvent>`.
- Subscriptions are registered via a fluent builder pattern.

### Service Defaults

When creating or modifying services:

- Call `builder.AddServiceDefaults()` for full defaults (health checks, OpenTelemetry, service discovery, HTTP resilience).
- Call `builder.AddBasicServiceDefaults()` for services without outgoing HTTP clients.
- Health check endpoints (`/health`, `/alive`) are mapped automatically.

### Authentication

- JWT Bearer authentication via the Identity.API service.
- Configured through `Identity:Url` and `Identity:Audience` configuration sections.

## Coding Style

- Follow the `.editorconfig` at the repo root.
- Use **implicit usings** (`ImplicitUsings` is enabled globally).
- Add service-specific global usings in each project's `GlobalUsings.cs`.
- Use **file-scoped namespaces**.
- Prefer **primary constructors** and records where appropriate.
- Use `var` when the type is obvious from the right-hand side.
- Keep `TreatWarningsAsErrors` clean — do not suppress warnings without justification.

## PR Guidelines

- Keep PRs focused on a single issue or concern.
- Include or update unit tests for any logic changes.
- Functional tests are expected for new API endpoints.
- Ensure `dotnet build eShop.Web.slnf` succeeds with zero warnings.
- Ensure `dotnet test tests/Basket.UnitTests` and `dotnet test tests/Ordering.UnitTests` pass (these don't require Docker).
- Do not modify `Directory.Packages.props` unless the issue explicitly requires a new dependency.
- Follow the contribution principles in `CONTRIBUTING.md`: best practices, architectural integrity, reliability, and performance.

## File & Folder Conventions

| Pattern | Convention |
|---------|-----------|
| Projects | `PascalCase` with dots (e.g., `Catalog.API`, `eShop.ServiceDefaults`) |
| API endpoint classes | `{Domain}Api.cs` (e.g., `CatalogApi.cs`) |
| Extension methods | `Add{Feature}()` / `Map{Feature}()` |
| Integration events | `{Verb}{Noun}IntegrationEvent.cs` |
| Event handlers | `{EventName}Handler.cs` |
| Aspire resource names | `kebab-case` (e.g., `basket-api`, `catalog-db`) |
| Database names | `lowercase` (e.g., `catalogdb`, `orderingdb`) |
