# New CQRS Command

Create a new command (write operation) following the CQRS + MediatR patterns established in the
Ordering bounded context of this eShop repository.

## Architectural Context

The Ordering service uses **CQRS** (Command Query Responsibility Segregation):

- **Commands** (writes) go through MediatR with a pipeline of behaviors:
  `LoggingBehavior → ValidatorBehavior → TransactionBehavior → CommandHandler`
- **Queries** (reads) bypass MediatR and query EF Core directly with flat read-model DTOs
- Domain events are dispatched **before** `SaveChanges` (within the same transaction)
- Integration events are published **after** transaction commit (outbox pattern)

## Required Files

### 1. Command — `src/Ordering.API/Application/Commands/{CommandName}Command.cs`

Use a **record** for simple commands:

```csharp
namespace eShop.Ordering.API.Application.Commands;

public record {CommandName}Command(int OrderId) : IRequest<bool>;
```

Use a **class** for complex commands that need `DataMemberAttribute` for serialization:

```csharp
namespace eShop.Ordering.API.Application.Commands;

[DataContract]
public class {CommandName}Command : IRequest<bool>
{
    [DataMember]
    public int OrderId { get; init; }

    [DataMember]
    public List<OrderItemDTO> OrderItems { get; init; }

    public {CommandName}Command(int orderId, List<OrderItemDTO> orderItems)
    {
        OrderId = orderId;
        OrderItems = orderItems;
    }
}
```

### 2. Command Handler — `src/Ordering.API/Application/Commands/{CommandName}CommandHandler.cs`

```csharp
namespace eShop.Ordering.API.Application.Commands;

public class {CommandName}CommandHandler(
    IOrderRepository orderRepository,
    IOrderingIntegrationEventService orderingIntegrationEventService,
    ILogger<{CommandName}CommandHandler> logger)
    : IRequestHandler<{CommandName}Command, bool>
{
    public async Task<bool> Handle({CommandName}Command command, CancellationToken cancellationToken)
    {
        // 1. (Optional) Save integration event to outbox within current transaction
        var integrationEvent = new {EventName}IntegrationEvent(command.OrderId);
        await orderingIntegrationEventService.AddAndSaveEventAsync(integrationEvent);

        // 2. Load aggregate from repository
        var order = await orderRepository.GetAsync(command.OrderId);
        if (order == null)
            return false;

        // 3. Execute domain behavior (state changes happen inside the aggregate)
        order.SetSomeStatus();

        // 4. Persist via Unit of Work
        return await orderRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        // SaveEntitiesAsync dispatches domain events, THEN saves changes
    }
}
```

**Key patterns:**
- Use **primary constructor** for dependency injection
- Load aggregate via repository, **never** directly through DbContext
- Call **behavior methods** on the aggregate — don't set properties directly
- Return `bool` to indicate success/failure
- `SaveEntitiesAsync` automatically dispatches domain events before saving

### 3. Command Validator — `src/Ordering.API/Application/Validations/{CommandName}CommandValidator.cs`

```csharp
namespace eShop.Ordering.API.Application.Validations;

public class {CommandName}CommandValidator : AbstractValidator<{CommandName}Command>
{
    public {CommandName}CommandValidator(ILogger<{CommandName}CommandValidator> logger)
    {
        RuleFor(command => command.OrderId).NotEmpty().WithMessage("No orderId found");

        // Add rules matching the command's data contract
        // Examples from the repo:
        // RuleFor(command => command.City).NotEmpty();
        // RuleFor(command => command.CardNumber).NotEmpty().Length(12, 19);
        // RuleFor(command => command.OrderItems).Must(ContainOrderItems)
        //     .WithMessage("No order items found");

        logger.LogTrace("INSTANCE CREATED - {ClassName}", GetType().Name);
    }

    // Private predicate methods for complex rules
    // private static bool ContainOrderItems(IEnumerable<OrderItemDTO> orderItems)
    // {
    //     return orderItems.Any();
    // }
}
```

Validators are auto-discovered by the `ValidatorBehavior` pipeline — just register FluentValidation
in DI (already done in this repo).

### 4. Idempotent Command Wrapper (if needed)

For commands that may be retried (e.g., from integration events), wrap with `IdentifiedCommand`:

```csharp
// The wrapper exists in the repo — use it, don't recreate it
// src/Ordering.API/Application/Commands/IdentifiedCommand.cs
public class IdentifiedCommand<T, R>(T command, Guid id) : IRequest<R>
    where T : IRequest<R>
{
    public T Command { get; } = command;
    public Guid Id { get; } = id;
}
```

The `IdentifiedCommandHandler` checks a `ClientRequest` table for duplicate request IDs before
delegating to the real handler.

### 5. Wire to API Endpoint — `src/Ordering.API/Apis/OrdersApi.cs`

```csharp
// In the handler method, dispatch via MediatR through the services aggregate:
public static async Task<Results<Ok, BadRequest<string>>> {ActionName}Async(
    [AsParameters] OrderServices services,
    {CommandName}Command command)
{
    var requestId = Guid.NewGuid(); // or from an x-requestid header

    // Wrap in IdentifiedCommand for idempotency
    var idempotentCommand = new IdentifiedCommand<{CommandName}Command, bool>(command, requestId);

    services.Logger.LogInformation("Sending command: {CommandName} ({RequestId})",
        nameof(idempotentCommand), requestId);

    var result = await services.Mediator.Send(idempotentCommand);

    if (result)
        return TypedResults.Ok();
    else
        return TypedResults.BadRequest("Command failed.");
}
```

### 6. Integration Event Handler → Command Bridge (if triggered by an event)

When a command is triggered by an incoming integration event from another service:

```csharp
// src/Ordering.API/IntegrationEvents/EventHandling/{EventName}Handler.cs
public class {EventName}Handler(
    IMediator mediator,
    ILogger<{EventName}Handler> logger)
    : IIntegrationEventHandler<{EventName}IntegrationEvent>
{
    public async Task Handle({EventName}IntegrationEvent @event)
    {
        logger.LogInformation("Handling integration event: {EventId} - ({@Event})",
            @event.Id, @event);

        // Translate integration event → domain command
        var command = new {CommandName}Command(@event.OrderId);
        await mediator.Send(command);
    }
}
```

## MediatR Pipeline (already configured — do not recreate)

The following pipeline behaviors are already registered in `Extensions.cs` and apply automatically
to every command:

1. **LoggingBehavior** — Logs command name before/after handling
2. **ValidatorBehavior** — Runs all `AbstractValidator<TCommand>` validators, throws
   `OrderingDomainException` on failure
3. **TransactionBehavior** — Wraps the handler in a DB transaction, publishes integration events
   after commit

## Checklist

- [ ] Command is a `record` (simple) or `class` with `[DataContract]` (complex)
- [ ] Command implements `IRequest<bool>` (or appropriate return type)
- [ ] Handler uses primary constructor for DI
- [ ] Handler implements `IRequestHandler<TCommand, TResult>`
- [ ] Handler loads aggregates through repository, not DbContext
- [ ] Handler calls aggregate behavior methods, not property setters
- [ ] Handler returns `bool` via `SaveEntitiesAsync()`
- [ ] Validator extends `AbstractValidator<TCommand>` with FluentValidation rules
- [ ] Validator constructor logs trace with class name
- [ ] API endpoint dispatches via `services.Mediator.Send()`
- [ ] If idempotency needed, wrap with `IdentifiedCommand<T, R>`
- [ ] If triggered by integration event, handler translates event → command
- [ ] Files are in the correct `Application/Commands/` or `Application/Validations/` folders
