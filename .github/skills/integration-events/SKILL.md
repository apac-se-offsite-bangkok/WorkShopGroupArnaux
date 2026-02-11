---
name: integration-events
description: "Use this skill whenever working with inter-service communication, event-driven architecture, message publishing/subscribing, or integration events in eShop. Triggers on: creating new events between microservices, adding event handlers, subscribing to events, or working with the EventBus/RabbitMQ. Integration events are always records extending the IntegrationEvent base, use past-tense naming, and handlers implement IIntegrationEventHandler<T>."
---

# Integration Event Conventions

## Core Rule

**Inter-service communication in eShop uses integration events via the EventBus abstraction (backed by RabbitMQ).** Never use direct HTTP calls between services for event-driven workflows. Never use other message brokers.

## Event Definition

All integration events are **records** extending the `IntegrationEvent` base record:

```csharp
// ✅ CORRECT — Positional record with concise syntax
public record ProductPriceChangedIntegrationEvent(
    int ProductId, decimal NewPrice, decimal OldPrice) : IntegrationEvent;

// ✅ CORRECT — Simple event with single property
public record OrderStockConfirmedIntegrationEvent(int OrderId) : IntegrationEvent;

// ✅ CORRECT — Event with collection payload
public record OrderStatusChangedToPaidIntegrationEvent(
    int OrderId, IEnumerable<OrderStockItem> OrderStockItems) : IntegrationEvent;

// ❌ WRONG — Class instead of record
public class OrderStartedIntegrationEvent : IntegrationEvent
{
    public int OrderId { get; set; }
}

// ❌ WRONG — Not extending IntegrationEvent base
public record OrderCreated(int OrderId);
```

## The IntegrationEvent Base

The base record lives in `src/EventBus/Events/IntegrationEvent.cs`:

```csharp
public record IntegrationEvent
{
    public Guid Id { get; set; }        // Auto-generated
    public DateTime CreationDate { get; set; }  // UTC
}
```

Never override `Id` or `CreationDate` — they are set automatically.

## Naming Convention

**`{PastTenseVerb}{Noun}IntegrationEvent`**

| ✅ Correct | ❌ Wrong |
|---|---|
| `OrderStartedIntegrationEvent` | `StartOrderEvent` |
| `ProductPriceChangedIntegrationEvent` | `PriceChangeEvent` |
| `OrderStatusChangedToPaidIntegrationEvent` | `OrderPaidNotification` |
| `OrderStockConfirmedIntegrationEvent` | `StockConfirmEvent` |
| `OrderPaymentSucceededIntegrationEvent` | `PaymentDoneMessage` |

## Supporting Types

Define supporting types as records alongside the event:

```csharp
public record OrderStockItem(int ProductId, int Units);
public record ConfirmedOrderStockItem(int ProductId, bool HasStock);
```

## Event Handler

Handlers implement `IIntegrationEventHandler<TEvent>`:

```csharp
public class OrderStatusChangedToPaidIntegrationEventHandler(
    CatalogContext catalogContext,
    ILogger<OrderStatusChangedToPaidIntegrationEventHandler> logger)
    : IIntegrationEventHandler<OrderStatusChangedToPaidIntegrationEvent>
{
    public async Task Handle(OrderStatusChangedToPaidIntegrationEvent @event)
    {
        // Handle the event
    }
}
```

### Handler Naming

Name handlers `{EventName}Handler.cs` — for example: `OrderStatusChangedToPaidIntegrationEventHandler.cs`.

## Subscription Registration

Subscriptions use a fluent builder pattern:

```csharp
app.MapSubscriptionEndpoints();

// Registration happens in the service's extension methods
eventBus.Subscribe<OrderStatusChangedToPaidIntegrationEvent,
    OrderStatusChangedToPaidIntegrationEventHandler>();
```

## File Organization

```
src/{Service}/IntegrationEvents/
├── Events/
│   ├── OrderStartedIntegrationEvent.cs
│   ├── OrderStockItem.cs              // Supporting record types
│   └── ...
└── EventHandling/
    ├── OrderStartedIntegrationEventHandler.cs
    └── ...
```

## Common Mistakes to Avoid

1. **Never use a class for an integration event** — always use `record`
2. **Never call other services via HTTP for event-driven flows** — use the EventBus
3. **Never forget to extend `IntegrationEvent`** — it provides `Id` and `CreationDate`
4. **Never use present-tense naming** — always past-tense (`Changed`, `Started`, `Confirmed`)
5. **Never put event logic in the event record** — events are pure data; logic goes in handlers
