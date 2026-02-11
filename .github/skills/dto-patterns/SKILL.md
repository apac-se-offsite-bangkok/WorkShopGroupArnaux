---
name: dto-patterns
description: "Use this skill whenever creating DTOs, API models, request/response types, view models, integration event payloads, or any data-carrying type that is not a domain entity. Triggers on: creating new record or class types for data transfer, API contracts, query results, command inputs, or event payloads. In eShop, all DTOs MUST be records, not classes."
---

# DTO & Data Transfer Patterns

## Core Rule

**All DTOs, API models, request/response types, and integration events MUST be `record` types, not `class` types.** Records provide value equality, immutability, and concise syntax that align with eShop conventions.

## When to Use Records vs Classes

| Use `record` | Use `class` |
|---|---|
| DTOs and API models | Domain entities (DDD aggregates/entities) |
| Integration event payloads | DbContext implementations |
| Request/response types | Services and handlers |
| Query result types | Infrastructure concerns |
| View models | |
| Value objects (consider `record struct`) | |

## Record Patterns

### Positional Records (preferred for simple DTOs)

Use positional syntax for simple data carriers with a few properties:

```csharp
// ✅ CORRECT — Positional record
public record OrderStockItem(int ProductId, int Units);
public record ConfirmedOrderStockItem(int ProductId, bool HasStock);

// ✅ CORRECT — Positional record with defaults and attributes
public record PaginationRequest(
    [property: Description("Number of items to return")]
    [property: DefaultValue(10)]
    int PageSize = 10,

    [property: Description("The index of the page")]
    [property: DefaultValue(0)]
    int PageIndex = 0
);

// ❌ WRONG — Class for a simple DTO
public class OrderStockItem
{
    public int ProductId { get; set; }
    public int Units { get; set; }
}
```

### Records with Body (for complex DTOs)

Use record with a body when you need additional members or the type has many properties:

```csharp
// ✅ CORRECT — Record with body
public record OrderSummary
{
    public int OrderNumber { get; init; }
    public DateTime Date { get; init; }
    public string Status { get; init; }
    public decimal Total { get; init; }
}
```

### Integration Events (always records)

```csharp
// ✅ CORRECT — Record extending IntegrationEvent
public record ProductPriceChangedIntegrationEvent(
    int ProductId, decimal NewPrice, decimal OldPrice) : IntegrationEvent;

// ❌ WRONG — Class for an integration event
public class ProductPriceChangedIntegrationEvent : IntegrationEvent
{
    public int ProductId { get; set; }
    public decimal NewPrice { get; set; }
    public decimal OldPrice { get; set; }
}
```

## Naming Conventions

| Type | Pattern | Example |
|---|---|---|
| Integration events | `{Verb}{Noun}IntegrationEvent` | `OrderStartedIntegrationEvent` |
| Domain events | `{Verb}{Noun}DomainEvent` | `OrderStartedDomainEvent` |
| Commands | `{Verb}{Noun}Command` | `CancelOrderCommand` |
| Query results | Descriptive noun | `OrderSummary`, `CardType` |
| Request types | `{Noun}Request` | `PaginationRequest` |

## Common Mistakes to Avoid

1. **Never use `class` for a type that only carries data** — use `record`
2. **Never use mutable `{ get; set; }` on records** — prefer positional parameters or `{ get; init; }`
3. **Never create a separate `Models` or `Dtos` folder with classes** — records are defined close to where they are used
4. **Never duplicate record definitions** — share via the `Shared` project if needed across services
