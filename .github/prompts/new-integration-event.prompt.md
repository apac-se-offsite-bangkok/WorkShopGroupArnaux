# New Integration Event

Create a new integration event for cross-service communication, following the established patterns
in this eShop repository.

## Architectural Context

This repository uses an **event bus abstraction** backed by RabbitMQ. Integration events enable
asynchronous, loosely-coupled communication between microservices. All integration events:

- Inherit from the `IntegrationEvent` base record (provides `Id` and `CreationDate` automatically)
- Use **past-tense naming** — they describe something that has already happened
- Are published via `IEventBus.PublishAsync()`
- Are handled by `IIntegrationEventHandler<TEvent>` implementations
- Are registered via `AddSubscription<TEvent, THandler>()` in the service's `AddApplicationServices`

Services that use the **outbox pattern** (Catalog.API, Ordering.API) save events to an
`IntegrationEventLog` table within the same DB transaction before publishing.

## Required Files

### 1. Event Class — `src/{ServiceName}.API/IntegrationEvents/Events/{EventName}.cs`

```csharp
namespace eShop.{ServiceName}.API.IntegrationEvents.Events;

// Name uses past tense: {Subject}{Action}IntegrationEvent
// Examples: OrderStatusChangedToPaid, OrderStockConfirmed, OrderStockRejected
public record {EventName}IntegrationEvent(
    int EntityId,
    // Include only the data the subscriber needs — keep it minimal
    // Use simple types or DTOs, never domain entities
    IEnumerable<{EventName}Item> Items) : IntegrationEvent;

// If the event carries a collection, define a companion DTO record:
public record {EventName}Item(int ProductId, int Units);
```

**Naming convention:** `{Subject}{PastTenseVerb}IntegrationEvent`
- ✅ `OrderStatusChangedToAwaitingValidationIntegrationEvent`
- ✅ `OrderStockConfirmedIntegrationEvent`
- ✅ `GracePeriodConfirmedIntegrationEvent`
- ❌ `SendOrderConfirmation` (imperative — wrong)
- ❌ `OrderEvent` (too vague — wrong)

### 2. Event Handler — `src/{ServiceName}.API/IntegrationEvents/EventHandling/{EventName}Handler.cs`

```csharp
namespace eShop.{ServiceName}.API.IntegrationEvents.EventHandling;

public class {EventName}Handler(
    // Use primary constructor for DI
    {ServiceName}Context context,
    ILogger<{EventName}Handler> logger)
    : IIntegrationEventHandler<{EventName}IntegrationEvent>
{
    public async Task Handle({EventName}IntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {IntegrationEventId} - ({@IntegrationEvent})",
            @event.Id, @event);

        // Implementation: update local state, trigger domain commands, etc.
        // For Ordering-style services, translate to a MediatR command:
        //   var command = new SomeCommand(@event.OrderId);
        //   await mediator.Send(command);
    }
}
```

### 3. Register Subscription — `src/{ServiceName}.API/Extensions/Extensions.cs`

Add the subscription inside the `AddApplicationServices` method:

```csharp
public static void AddApplicationServices(this IHostApplicationBuilder builder)
{
    // ... existing registrations ...

    builder.AddRabbitMqEventBus("eventbus")
           .AddSubscription<{EventName}IntegrationEvent, {EventName}Handler>()
           // ... other existing subscriptions ...
           ;
}
```

If there are many subscriptions, extract them into a private method:

```csharp
private static void AddEventBusSubscriptions(this IEventBusBuilder eventBus)
{
    eventBus.AddSubscription<{EventName}IntegrationEvent, {EventName}Handler>();
    // ... more subscriptions ...
}
```

### 4. Publishing the Event (in the producing service)

#### Simple pattern (fire-and-forget):

```csharp
await eventBus.PublishAsync(new {EventName}IntegrationEvent(entityId, items));
```

#### Outbox pattern (transactional consistency — used by Catalog.API and Ordering.API):

```csharp
// 1. Save event within the current DB transaction
var integrationEvent = new {EventName}IntegrationEvent(entityId, items);
await integrationEventService.SaveEventAndContextChangesAsync(integrationEvent);

// 2. Publish after transaction commits
await integrationEventService.PublishThroughEventBusAsync(integrationEvent);
```

For Ordering.API, this is handled by the `TransactionBehavior` MediatR pipeline:

```csharp
// In domain event handler — enqueue for later publication
await orderingIntegrationEventService.AddAndSaveEventAsync(integrationEvent);
// TransactionBehavior publishes all pending events after commit
```

### 5. GlobalUsings — `src/{ServiceName}.API/GlobalUsings.cs`

Ensure the following are present:

```csharp
global using eShop.EventBus.Abstractions;
global using eShop.EventBus.Events;
global using eShop.{ServiceName}.API.IntegrationEvents.Events;
global using eShop.{ServiceName}.API.IntegrationEvents.EventHandling;
```

## Checklist

- [ ] Event class is a `record` inheriting from `IntegrationEvent`
- [ ] Event name uses past tense (`{Subject}{PastTenseVerb}IntegrationEvent`)
- [ ] Event carries only primitive/DTO data — never domain entities
- [ ] Handler class uses primary constructor for DI
- [ ] Handler implements `IIntegrationEventHandler<TEvent>`
- [ ] Subscription registered via `.AddSubscription<TEvent, THandler>()` in `Extensions.cs`
- [ ] Event file is in `IntegrationEvents/Events/`
- [ ] Handler file is in `IntegrationEvents/EventHandling/`
- [ ] If using outbox pattern, event is saved within the DB transaction before publishing
- [ ] GlobalUsings updated if new namespace is introduced
