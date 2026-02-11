# New Domain Event Handler

Create a domain event handler following the CQRS patterns established in the Ordering bounded
context of this eShop repository.

## Architectural Context

**Domain events** are in-process notifications that bridge the domain layer and application layer.
They are fundamentally different from integration events:

| Aspect | Domain Events | Integration Events |
|--------|--------------|-------------------|
| Scope | Within a single bounded context | Across microservices |
| Transport | MediatR (in-process) | RabbitMQ (out-of-process) |
| Interface | `INotification` (MediatR) | `IntegrationEvent` record |
| When dispatched | Before `SaveChanges` (same transaction) | After transaction commit |
| Consistency | Strong (same DB transaction) | Eventual (at-least-once delivery) |

In this repo, domain events are:
1. Added to aggregate roots via `AddDomainEvent()` during behavior methods
2. Dispatched by `MediatorExtension.DispatchDomainEventsAsync()` inside `SaveEntitiesAsync()`
3. Handled by `INotificationHandler<T>` implementations **within the same transaction**
4. Often used to **bridge** to integration events (enqueue them for post-commit publication)

## Required Files

### 1. Domain Event — `src/Ordering.Domain/Events/{EventName}DomainEvent.cs`

```csharp
namespace eShop.Ordering.Domain.Events;

/// <summary>
/// Domain event raised when {describe what happened in the domain}.
/// </summary>
public class {EventName}DomainEvent : INotification
{
    // Use primary constructor or explicit properties with get-only access
    public int AggregateId { get; }
    public IEnumerable<OrderItem> Items { get; }

    public {EventName}DomainEvent(int aggregateId, IEnumerable<OrderItem> items)
    {
        AggregateId = aggregateId;
        Items = items;
    }
}
```

**Naming convention:** `{Subject}{PastTenseVerb}DomainEvent`
- ✅ `OrderStartedDomainEvent`
- ✅ `OrderStatusChangedToAwaitingValidationDomainEvent`
- ✅ `BuyerAndPaymentMethodVerifiedDomainEvent`
- ❌ `ProcessOrder` (imperative — wrong)

### 2. Raise from Aggregate Root — `src/Ordering.Domain/AggregatesModel/{Aggregate}Aggregate/{Aggregate}.cs`

Domain events are raised inside aggregate behavior methods:

```csharp
public class Order : Entity, IAggregateRoot
{
    // In constructor (creation event)
    public Order(string userId, string userName, Address address, /* ... */)
    {
        // ... set state ...
        AddDomainEvent(new OrderStartedDomainEvent(this, userId, userName, cardTypeId, /* ... */));
    }

    // In state transition methods
    public void SetAwaitingValidationStatus()
    {
        if (OrderStatus != OrderStatus.Submitted)
            StatusChangeException(OrderStatus.AwaitingValidation);

        AddDomainEvent(new OrderStatusChangedToAwaitingValidationDomainEvent(Id, _orderItems));
        OrderStatus = OrderStatus.AwaitingValidation;
    }
}
```

**Rules:**
- Always raise the event **before** changing state (for handlers that need to see the old state)
  OR at the end of the method (for handlers that need the new state) — be intentional
- Events carry the data that handlers need — don't make handlers query back to the aggregate
- Only aggregate roots can raise domain events (via inherited `AddDomainEvent`)

### 3. Domain Event Handler — `src/Ordering.API/Application/DomainEventHandlers/{EventName}DomainEventHandler.cs`

```csharp
namespace eShop.Ordering.API.Application.DomainEventHandlers;

public class {EventName}DomainEventHandler(
    IOrderRepository orderRepository,
    IBuyerRepository buyerRepository,
    IOrderingIntegrationEventService orderingIntegrationEventService,
    ILogger<{EventName}DomainEventHandler> logger)
    : INotificationHandler<{EventName}DomainEvent>
{
    public async Task Handle({EventName}DomainEvent domainEvent, CancellationToken cancellationToken)
    {
        logger.LogTrace("Domain event {DomainEvent} handled, {AggregateId}",
            nameof({EventName}DomainEvent), domainEvent.AggregateId);

        // OPTION A: Update another aggregate in response
        var buyer = await buyerRepository.FindAsync(domainEvent.BuyerIdentityGuid);
        // ... modify buyer ...

        // OPTION B: Bridge to an integration event (most common pattern)
        var integrationEvent = new {EventName}IntegrationEvent(
            domainEvent.AggregateId,
            domainEvent.Items.Select(i => new OrderStockItem(i.ProductId, i.Units)));

        await orderingIntegrationEventService.AddAndSaveEventAsync(integrationEvent);

        // NOTE: Integration events are NOT published here — they are published
        // by TransactionBehavior AFTER the DB transaction commits.
    }
}
```

### Common Handler Patterns in This Repo

#### Pattern 1: Bridge Domain Event → Integration Event

The most common pattern. Domain event handlers translate domain state changes into integration
events that other microservices can consume:

```csharp
public class OrderStatusChangedToAwaitingValidationDomainEventHandler(
    IOrderRepository orderRepository,
    IBuyerRepository buyerRepository,
    IOrderingIntegrationEventService orderingIntegrationEventService,
    ILogger<OrderStatusChangedToAwaitingValidationDomainEventHandler> logger)
    : INotificationHandler<OrderStatusChangedToAwaitingValidationDomainEvent>
{
    public async Task Handle(
        OrderStatusChangedToAwaitingValidationDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        // Load related data needed for the integration event
        var order = await orderRepository.GetAsync(domainEvent.OrderId);
        var buyer = await buyerRepository.FindByIdAsync(order.GetBuyerId.Value);

        // Map to integration event DTO
        var orderStockItems = domainEvent.OrderItems
            .Select(i => new OrderStockItem(i.ProductId, i.GetUnits()));

        // Enqueue for post-commit publication
        var integrationEvent = new OrderStatusChangedToAwaitingValidationIntegrationEvent(
            order.Id, order.OrderStatus.ToString(), buyer.Name, orderStockItems);

        await orderingIntegrationEventService.AddAndSaveEventAsync(integrationEvent);
    }
}
```

#### Pattern 2: Cross-Aggregate Coordination

Domain events coordinate behavior across aggregates within the same bounded context:

```csharp
public class ValidateOrAddBuyerAggregateWhenOrderStartedDomainEventHandler(
    IBuyerRepository buyerRepository,
    IOrderingIntegrationEventService orderingIntegrationEventService,
    ILogger<ValidateOrAddBuyerAggregateWhenOrderStartedDomainEventHandler> logger)
    : INotificationHandler<OrderStartedDomainEvent>
{
    public async Task Handle(OrderStartedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        // Find or create the Buyer aggregate
        var buyer = await buyerRepository.FindAsync(domainEvent.UserIdentityGuid);
        bool buyerExisted = buyer is not null;

        if (!buyerExisted)
            buyer = new Buyer(domainEvent.UserIdentityGuid, domainEvent.UserName);

        // Execute behavior on the Buyer aggregate
        buyer.VerifyOrAddPaymentMethod(
            domainEvent.CardTypeId,
            $"Payment Method on {DateTime.UtcNow}",
            domainEvent.CardNumber,
            domainEvent.CardSecurityNumber,
            domainEvent.CardHolderName,
            domainEvent.CardExpiration,
            domainEvent.Order.Id);

        // Persist
        var buyerUpdated = buyerExisted
            ? buyerRepository.Update(buyer)
            : buyerRepository.Add(buyer);

        await buyerRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);

        // Bridge to integration event
        var integrationEvent = new OrderStatusChangedToSubmittedIntegrationEvent(
            domainEvent.Order.Id, domainEvent.Order.OrderStatus.ToString(), buyer.Name);
        await orderingIntegrationEventService.AddAndSaveEventAsync(integrationEvent);
    }
}
```

## File Organization

```
src/Ordering.Domain/Events/
└── {EventName}DomainEvent.cs             # Domain event class

src/Ordering.API/Application/DomainEventHandlers/
└── {EventName}DomainEventHandler.cs      # Handler in the Application layer
```

Domain events live in the **Domain** layer (no infrastructure dependencies).
Domain event handlers live in the **Application** layer (can use repositories, integration services).

## Checklist

- [ ] Domain event implements MediatR's `INotification`
- [ ] Event class lives in `Ordering.Domain/Events/`
- [ ] Event name uses past tense (`{Subject}{PastTenseVerb}DomainEvent`)
- [ ] Event carries all data that handlers need
- [ ] Aggregate raises the event via `AddDomainEvent()` inside a behavior method
- [ ] Handler implements `INotificationHandler<TEvent>` in the Application layer
- [ ] Handler uses primary constructor for DI
- [ ] Handler lives in `Ordering.API/Application/DomainEventHandlers/`
- [ ] If bridging to integration event: uses `AddAndSaveEventAsync()`, NOT `PublishAsync()`
- [ ] Handler does NOT call `PublishAsync` directly — `TransactionBehavior` handles that
- [ ] Logging uses structured templates with named placeholders
- [ ] No business logic in handlers — delegate to aggregate methods
