# GitHub Copilot Custom Instructions — eShop Reference Application

> These instructions apply **repo-wide** to every Copilot suggestion, completion, and chat response.
> Directory-specific instruction files exist under `src/` and `tests/` for additional context.

---

## 1. Project Overview

This is the **eShop reference application** — a cloud-native, microservices-based .NET solution orchestrated with **.NET Aspire**. It demonstrates Domain-Driven Design (DDD), CQRS, integration events, and modern ASP.NET patterns.

### Tech Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 (`global.json` → SDK 10.0.100) |
| Orchestration | .NET Aspire (`eShop.AppHost`) |
| APIs | ASP.NET Core **Minimal APIs** with API versioning |
| ORM | **Entity Framework Core** with Npgsql (PostgreSQL + pgvector) |
| Messaging | RabbitMQ via custom `EventBus` / `EventBusRabbitMQ` abstractions |
| Auth | Duende IdentityServer, JWT Bearer |
| Mediator / CQRS | MediatR 13 |
| Validation | FluentValidation |
| Observability | OpenTelemetry (traces, metrics, logs) |
| Mobile client | .NET MAUI (`ClientApp`) |
| Testing | MSTest (unit tests), xUnit v3 (functional tests), NSubstitute (mocking) |
| Package mgmt | Central Package Management (`Directory.Packages.props`) |

---

## 2. Architecture & Project Structure

```
src/
  eShop.AppHost/          → Aspire host — orchestrates all services
  eShop.ServiceDefaults/  → Cross-cutting: OpenTelemetry, health, auth, OpenAPI
  Catalog.API/            → Minimal API microservice (simple CRUD, no DDD)
  Basket.API/             → gRPC service with Redis
  Ordering.API/           → CQRS + DDD microservice (MediatR commands/queries)
  Ordering.Domain/        → Domain model (aggregates, value objects, domain events)
  Ordering.Infrastructure/→ EF Core DbContext, repositories, entity configurations
  OrderProcessor/         → Background worker (grace period)
  PaymentProcessor/       → Background worker (payment simulation)
  Identity.API/           → Duende IdentityServer
  Webhooks.API/           → Webhook subscriptions
  EventBus/               → Abstraction (IEventBus, IntegrationEvent record)
  EventBusRabbitMQ/       → RabbitMQ implementation
  IntegrationEventLogEF/  → EF Core transactional outbox
  Shared/                 → Shared utilities (migrations, activity extensions)
  WebApp/                 → Blazor server-rendered frontend
  ClientApp/              → .NET MAUI mobile app
tests/
  Basket.UnitTests/       → MSTest + NSubstitute
  Ordering.UnitTests/     → MSTest + NSubstitute
  Ordering.FunctionalTests/ → xUnit v3 + Aspire test containers
  Catalog.FunctionalTests/  → xUnit v3 + Aspire test containers
  ClientApp.UnitTests/    → MSTest (MAUI)
```

### Key Architecture Decisions

- **Ordering** follows full DDD: Aggregates (`Order`, `Buyer`), Value Objects (`Address`), Domain Events, Repository pattern with `IUnitOfWork`.
- **Catalog** is a simpler service: direct `DbContext` usage in Minimal API handlers — no repositories, no domain layer.
- **Integration events** are published through a transactional outbox (`IntegrationEventLogEF`) to guarantee at-least-once delivery.
- **CQRS** in Ordering: Commands go through MediatR pipeline (logging → validation → transaction behaviors); Queries use `IOrderQueries` with EF Core LINQ projections.

---

## 3. Data Access — Entity Framework Core (MANDATORY)

### ✅ DO

- **Always use EF Core** `DbContext` + LINQ for data access.
- Use **async** EF APIs everywhere: `ToListAsync()`, `SingleOrDefaultAsync()`, `FindAsync()`, `SaveChangesAsync()`, `LongCountAsync()`.
- Build **composable `IQueryable<T>`** pipelines — apply filters conditionally, then materialize.
- Use `AsNoTracking()` for read-only queries.
- Use `Include()` / explicit loading only when the related data is needed.
- Use **EF entity configurations** (`IEntityTypeConfiguration<T>`) in the Infrastructure layer — never data annotations on domain entities for persistence mapping.
- Use execution strategies (`Database.CreateExecutionStrategy()`) for transactional resilience (see `TransactionBehavior`, `ResilientTransaction`).

### 🚫 DO NOT

- **Never suggest raw SQL** (`SELECT`, `INSERT`, `UPDATE`, `DELETE` statements), ADO.NET (`SqlCommand`, `NpgsqlCommand`, `DbCommand`), or micro-ORMs (Dapper) for application queries.
- **Never use** `FromSqlRaw`, `ExecuteSqlRaw`, `SqlQueryRaw` unless the user explicitly requests it for a specific performance or feature need that EF Core LINQ cannot address.
- **Never introduce** new `IDbConnection`, `NpgsqlConnection`, or `SqlConnection` usage.
- **Never suggest** stored procedures for business logic.

### Acceptable Exceptions

- EF Migrations (`dotnet ef migrations add`) produce generated SQL — that's fine.
- Database seeding may use provider-specific APIs where they already exist (e.g., `NpgsqlConnection.ReloadTypes()` in `CatalogContextSeed`).
- If raw SQL is truly necessary, present it as a clearly-labeled **alternative** with an explanation of why EF Core LINQ is insufficient.

### Example — Correct EF Core Query Pattern

```csharp
// ✅ Composable IQueryable with conditional filters and async materialization
public static async Task<Ok<PaginatedItems<CatalogItem>>> GetAllItems(
    [AsParameters] PaginationRequest paginationRequest,
    [AsParameters] CatalogServices services,
    string? name, int? type, int? brand)
{
    var root = (IQueryable<CatalogItem>)services.Context.CatalogItems;

    if (name is not null)
        root = root.Where(c => c.Name.StartsWith(name));
    if (type is not null)
        root = root.Where(c => c.CatalogTypeId == type);
    if (brand is not null)
        root = root.Where(c => c.CatalogBrandId == brand);

    var totalItems = await root.LongCountAsync();
    var itemsOnPage = await root
        .OrderBy(c => c.Name)
        .Skip(paginationRequest.PageSize * paginationRequest.PageIndex)
        .Take(paginationRequest.PageSize)
        .ToListAsync();

    return TypedResults.Ok(new PaginatedItems<CatalogItem>(
        paginationRequest.PageIndex, paginationRequest.PageSize, totalItems, itemsOnPage));
}
```

---

## 4. DTOs & Records — Use Records, Not Classes

### ✅ DO

- Use **`record`** types for all DTOs, view models, integration events, commands, and request/response objects.
- Use **`init`-only properties** for immutable data shapes.
- Suffix DTO types with `DTO` (match existing casing: `OrderDraftDTO`, `OrderItemDTO`).
- Use **positional record syntax** for simple, flat DTOs:
  ```csharp
  // ✅ Positional record — compact and immutable
  public record OrderStockItem(int ProductId, int Units);
  public record ConfirmedOrderStockItem(int ProductId, bool HasStock);
  ```
- Use **record with init properties** for more complex DTOs:
  ```csharp
  // ✅ Record with init-only properties
  public record OrderDraftDTO
  {
      public IEnumerable<OrderItemDTO> OrderItems { get; init; }
      public decimal Total { get; init; }
  }
  ```
- Use **explicit mapping methods** (`FromOrder(...)`, `.ToDto()`, extension methods) to convert between domain entities and DTOs.
- Keep DTOs **flat and purpose-specific** — one DTO per use case, not one shared "model."

### 🚫 DO NOT

- **Never use `class`** for new DTOs, view models, request objects, or integration events. Always use `record`.
- **Never reuse domain entities** (`Order`, `CatalogItem`, `Buyer`) as API response contracts for new endpoints — create a dedicated DTO.
- **Never put EF persistence concerns** (navigation properties, `DbSet` attributes, entity configurations) on DTOs.
- **Never create** types named `FooModel`, `FooData`, `FooRecord` — use `FooDTO` for data transfer objects, or a plain descriptive `record` name for integration events/commands.
- **Never modify** domain entities from the API layer — commands should go through MediatR and the domain model.

### Integration Events — Always Records

```csharp
// ✅ Integration events inherit from IntegrationEvent (which is itself a record)
public record ProductPriceChangedIntegrationEvent(int ProductId, decimal NewPrice, decimal OldPrice) : IntegrationEvent;
public record OrderStatusChangedToSubmittedIntegrationEvent : IntegrationEvent
{
    public int OrderId { get; init; }
    public string OrderStatus { get; init; }
    public string BuyerName { get; init; }
}
```

---

## 5. Minimal API Conventions

- Define endpoints in static classes using `MapGroup` / `NewVersionedApi` for versioned endpoint groups.
- Always use **typed results**: `Task<Ok<T>>`, `Task<Results<Ok<T>, NotFound>>`, `Task<Results<Ok<T>, NotFound, BadRequest<ProblemDetails>>>`.
- Return `TypedResults.Ok(...)`, `TypedResults.NotFound()`, `TypedResults.Created(...)`, `TypedResults.NoContent()`, `TypedResults.BadRequest<ProblemDetails>(...)`.
- Use `ProblemDetails` for error responses (not bare strings except in simple cases).
- Use `[AsParameters]` for injecting grouped services/request parameters.
- Use `[Description]` attributes on parameters for OpenAPI documentation.
- Chain `.WithName()`, `.WithSummary()`, `.WithDescription()`, `.WithTags()` on endpoint registrations.

---

## 6. Domain-Driven Design (Ordering Service)

- **Aggregates** (`Order`, `Buyer`) encapsulate business logic and enforce invariants — mutations only through aggregate methods.
- **Value Objects** extend `ValueObject` base class and override `GetEqualityComponents()`.
- **Domain Events** (`INotification`) are dispatched via MediatR after `SaveChangesAsync`.
- **Repositories** implement `IRepository<T>` where `T : IAggregateRoot` and expose `IUnitOfWork`.
- **Never** bypass the aggregate root to directly modify child entities.
- **Never** add business logic to API handlers — route through commands/domain methods.

---

## 7. Commands & Queries (CQRS with MediatR)

- **Commands** implement `IRequest<TResponse>` and are handled by `IRequestHandler<TCommand, TResponse>`.
- Prefer `record` for commands: `public record CancelOrderCommand(int OrderNumber) : IRequest<bool>;`
- Use **FluentValidation** validators (e.g., `CreateOrderCommandValidator`) registered via the `ValidatorBehavior` pipeline.
- **Queries** go through `IOrderQueries` interface → `OrderQueries` implementation using EF Core LINQ projections.
- Query results use `record` view models (`Order`, `OrderSummary`, `CardType` in the Queries namespace).

---

## 8. Logging

- Use `ILogger<T>` with **structured logging** — message templates with named placeholders:
  ```csharp
  logger.LogInformation("Sending command: {CommandName} - {IdProperty}: {CommandId}", ...);
  ```
- **Never** use string interpolation (`$"..."`) in log messages.
- Guard verbose logging with `logger.IsEnabled(LogLevel.Trace)` or `LogLevel.Debug`.

---

## 9. Dependency Injection & Service Registration

- Register services in `Extensions.cs` files using `IHostApplicationBuilder` extension methods.
- Use `AddDbContext<T>` or Aspire's `AddNpgsqlDbContext<T>` for EF contexts.
- Use `AddMigration<TContext, TSeeder>()` for database migration as a hosted service.
- Register MediatR with `AddMediatR` including open behaviors (Logging, Validator, Transaction).
- Register FluentValidation validators with `AddValidatorsFromAssemblyContaining<T>()`.
- Register repositories as scoped: `services.AddScoped<IOrderRepository, OrderRepository>()`.

---

## 10. Code Style

- Follow `.editorconfig` rules: 4-space indent, `var` preferred, braces required, PascalCase for constants.
- Use **file-scoped namespaces** (`namespace Foo;`) — match existing convention.
- Use **`global using`** directives in `GlobalUsings.cs` per project.
- Use **primary constructors** where the project already uses them (e.g., `WebhooksContext`, `OrderQueries`).
- Use **collection expressions** (`[item1, item2]`) for inline list initialization where appropriate.
- Prefer `is null` / `is not null` over `== null` / `!= null`.

---

## 11. Files to EXCLUDE — Do Not Modify or Suggest Changes To

Generated and scaffolded code that should **never** be manually edited:

- `**/Migrations/*.cs` — EF Core migration files (all `Designer.cs`, snapshot files, migration `.cs` files)
- `**/bin/**` and `**/obj/**` — build output
- `**/node_modules/**` — npm packages
- `**/*.Designer.cs` — auto-generated designer files
- `**/Properties/launchSettings.json` — generally not edited in code suggestions
- `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` — only edit when explicitly asked about package versions or build configuration
- `global.json` — only edit when explicitly asked about SDK version

---

## 12. External Best Practices to Follow

- **EF Core**: Follow [Microsoft EF Core documentation](https://learn.microsoft.com/en-us/ef/core/) patterns — no anti-patterns like N+1 queries, unbounded result sets, or misuse of `Include()`.
- **Aspire**: Follow [.NET Aspire documentation](https://learn.microsoft.com/en-us/dotnet/aspire/) for service defaults, health checks, and resource provisioning.
- **Minimal APIs**: Follow [ASP.NET Core Minimal API docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) for endpoint design.
- **DDD**: Follow [Domain-Driven Design](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/) patterns as implemented in the Ordering bounded context.
- **Testing**: Follow [Microsoft Testing Platform](https://learn.microsoft.com/en-us/dotnet/core/testing/) guidance; use the Arrange-Act-Assert pattern.

---

## Quick Reference — What a Senior Dev Would Write

| Scenario | ✅ Correct | 🚫 Wrong |
|---|---|---|
| Query data | `await context.Items.Where(...).ToListAsync()` | `SELECT * FROM Items WHERE ...` |
| New DTO | `public record ItemDTO { ... }` | `public class ItemDTO { ... }` |
| Integration event | `public record FooEvent(...) : IntegrationEvent;` | `public class FooEvent : IntegrationEvent { }` |
| API return type | `Task<Results<Ok<T>, NotFound>>` | `Task<IActionResult>` |
| Error response | `TypedResults.BadRequest<ProblemDetails>(...)` | `return BadRequest("error")` |
| Logging | `logger.LogInformation("Order {OrderId}", id)` | `logger.LogInformation($"Order {id}")` |
| Command | `public record CancelOrderCommand(int OrderNumber) : IRequest<bool>;` | `public class CancelOrderCommand { public int OrderNumber { get; set; } }` |
