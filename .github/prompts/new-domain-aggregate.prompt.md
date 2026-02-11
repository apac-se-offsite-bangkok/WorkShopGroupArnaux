# New Domain Entity or Aggregate

Create a new DDD domain entity or aggregate root following the Ordering.Domain patterns established
in this eShop repository.

## Architectural Context

This repository follows **Domain-Driven Design** in the Ordering bounded context. The domain layer
(`src/Ordering.Domain/`) is persistence-ignorant and contains:

- **Aggregate Roots** — entry points to consistency boundaries, implement `IAggregateRoot`
- **Entities** — have identity, inherit from `Entity` base class
- **Value Objects** — structural equality, inherit from `ValueObject` base class
- **Domain Events** — in-process notifications, implement MediatR's `INotification`

All building blocks live in `SeedWork/`:
- `Entity` — base class with `Id`, domain events list, identity-based equality
- `IAggregateRoot` — empty marker interface (repos only allowed for aggregate roots)
- `IRepository<T> where T : IAggregateRoot` — exposes `IUnitOfWork`
- `IUnitOfWork` — `SaveChangesAsync()` and `SaveEntitiesAsync()`
- `ValueObject` — structural equality via `GetEqualityComponents()`
- `Enumeration` — smart-enum base class

## Required Files

### 1. Aggregate Root — `src/Ordering.Domain/AggregatesModel/{AggregateName}Aggregate/{AggregateName}.cs`

```csharp
namespace eShop.Ordering.Domain.AggregatesModel.{AggregateName}Aggregate;

public class {AggregateName} : Entity, IAggregateRoot
{
    // Private backing fields for encapsulated collections
    private readonly List<{ChildEntity}> _items;

    // Public read-only projection of the collection
    public IReadOnlyCollection<{ChildEntity}> Items => _items.AsReadOnly();

    // Value objects composed into the aggregate
    public Address Address { get; private set; }

    // Private setters for all mutable state
    public string Name { get; private set; }
    public {StatusEnum} Status { get; private set; }

    // Constructor enforces invariants and raises creation domain event
    public {AggregateName}(string userId, string name, Address address, /* ... */)
    {
        // Validate invariants
        // Set state
        // Raise domain event
        AddDomainEvent(new {AggregateName}StartedDomainEvent(this, /* ... */));
    }

    // Protected parameterless constructor for EF Core
    protected {AggregateName}() { _items = []; }

    // Behavior methods that enforce business rules
    public void AddItem(int productId, string productName, decimal unitPrice,
        decimal discount, string pictureUrl, int units = 1)
    {
        // Check invariants
        var existingItem = _items.SingleOrDefault(o => o.ProductId == productId);
        if (existingItem != null)
        {
            // Merge logic (e.g., add units to existing item)
            existingItem.AddUnits(units);
        }
        else
        {
            _items.Add(new {ChildEntity}(productId, productName, unitPrice, discount, pictureUrl, units));
        }
    }

    // State transition methods with guard clauses and domain events
    public void SetNextStatus()
    {
        if (Status != {StatusEnum}.PreviousState)
            StatusChangeException({StatusEnum}.NextState);

        AddDomainEvent(new {AggregateName}StatusChangedDomainEvent(Id, _items));
        Status = {StatusEnum}.NextState;
    }

    private void StatusChangeException({StatusEnum} status)
    {
        throw new {DomainName}DomainException($"Not possible to change status to {status}.");
    }
}
```

**Key DDD rules enforced in this repo:**
- Collections are **private** with `IReadOnlyCollection<>` public access
- All setters are **private** — state changes only through behavior methods
- **Guard clauses** protect state transitions
- **Domain events** are raised on meaningful state changes
- **Invariant validation** happens in constructors and methods, never externally
- A protected parameterless constructor exists for EF Core materialization

### 2. Child Entity — `src/Ordering.Domain/AggregatesModel/{AggregateName}Aggregate/{ChildEntity}.cs`

```csharp
namespace eShop.Ordering.Domain.AggregatesModel.{AggregateName}Aggregate;

public class {ChildEntity} : Entity
{
    public int ProductId { get; private set; }
    public string ProductName { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Units { get; private set; }

    // Constructor with invariant validation
    public {ChildEntity}(int productId, string productName, decimal unitPrice,
        decimal discount, string pictureUrl, int units = 1)
    {
        if (units <= 0)
            throw new OrderingDomainException("Invalid number of units");

        if ((unitPrice * units) < discount)
            throw new OrderingDomainException("The total of order item is lower than applied discount");

        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Units = units;
    }

    // Behavior methods
    public void AddUnits(int units)
    {
        if (units < 0)
            throw new OrderingDomainException("Invalid units");

        Units += units;
    }
}
```

### 3. Value Object — `src/Ordering.Domain/AggregatesModel/{AggregateName}Aggregate/{ValueObjectName}.cs`

```csharp
namespace eShop.Ordering.Domain.AggregatesModel.{AggregateName}Aggregate;

public class {ValueObjectName} : ValueObject
{
    public string Street { get; private set; }
    public string City { get; private set; }
    public string State { get; private set; }

    public {ValueObjectName}(string street, string city, string state)
    {
        Street = street;
        City = city;
        State = state;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
    }
}
```

### 4. Domain Exception — `src/Ordering.Domain/Exceptions/{DomainName}DomainException.cs`

```csharp
namespace eShop.Ordering.Domain.Exceptions;

public class {DomainName}DomainException : Exception
{
    public {DomainName}DomainException() { }
    public {DomainName}DomainException(string message) : base(message) { }
    public {DomainName}DomainException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

### 5. Repository Interface — `src/Ordering.Domain/AggregatesModel/{AggregateName}Aggregate/I{AggregateName}Repository.cs`

```csharp
namespace eShop.Ordering.Domain.AggregatesModel.{AggregateName}Aggregate;

// Repositories are ONLY defined for aggregate roots
public interface I{AggregateName}Repository : IRepository<{AggregateName}>
{
    {AggregateName} Add({AggregateName} entity);
    void Update({AggregateName} entity);
    Task<{AggregateName}> GetAsync(int id);
}
```

### 6. Domain Events — `src/Ordering.Domain/Events/{EventName}DomainEvent.cs`

```csharp
namespace eShop.Ordering.Domain.Events;

// Domain events implement MediatR's INotification
// They are dispatched BEFORE SaveChanges within the same transaction
public class {EventName}DomainEvent(/* parameters */) : INotification
{
    public int AggregateId { get; } = aggregateId;
    public IEnumerable<{ChildEntity}> Items { get; } = items;
}
```

### 7. Repository Implementation — `src/Ordering.Infrastructure/Repositories/{AggregateName}Repository.cs`

```csharp
namespace eShop.Ordering.Infrastructure.Repositories;

public class {AggregateName}Repository(OrderingContext context)
    : I{AggregateName}Repository
{
    public IUnitOfWork UnitOfWork => context;

    public {AggregateName} Add({AggregateName} entity)
    {
        return context.{AggregateName}s.Add(entity).Entity;
    }

    public void Update({AggregateName} entity)
    {
        context.Entry(entity).State = EntityState.Modified;
    }

    public async Task<{AggregateName}> GetAsync(int id)
    {
        var entity = await context.{AggregateName}s
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (entity != null)
        {
            await context.Entry(entity)
                .Collection(i => i.Items).LoadAsync();
        }

        return entity;
    }
}
```

### 8. EF Configuration — `src/Ordering.Infrastructure/EntityConfigurations/{AggregateName}EntityTypeConfiguration.cs`

```csharp
namespace eShop.Ordering.Infrastructure.EntityConfigurations;

class {AggregateName}EntityTypeConfiguration : IEntityTypeConfiguration<{AggregateName}>
{
    public void Configure(EntityTypeBuilder<{AggregateName}> builder)
    {
        builder.ToTable("{tablename}", OrderingContext.DEFAULT_SCHEMA);
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).UseHiLo("{tablename}seq", OrderingContext.DEFAULT_SCHEMA);

        // Value Objects as owned entities
        builder.OwnsOne(o => o.Address);

        // Ignore domain events (not persisted)
        builder.Ignore(o => o.DomainEvents);

        // Enum as string
        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        // Navigation properties
        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey("{AggregateName}Id");
    }
}
```

## Folder Structure

```
src/Ordering.Domain/
├── AggregatesModel/
│   └── {AggregateName}Aggregate/
│       ├── {AggregateName}.cs            # Aggregate root
│       ├── {ChildEntity}.cs              # Child entities
│       ├── {ValueObject}.cs              # Value objects
│       ├── I{AggregateName}Repository.cs # Repository interface
│       └── {StatusEnum}.cs               # Status enums (if applicable)
├── Events/
│   └── {EventName}DomainEvent.cs         # Domain events
├── Exceptions/
│   └── {DomainName}DomainException.cs    # Domain exceptions
└── SeedWork/
    ├── Entity.cs                          # DO NOT MODIFY
    ├── IAggregateRoot.cs                  # DO NOT MODIFY
    ├── IRepository.cs                     # DO NOT MODIFY
    ├── IUnitOfWork.cs                     # DO NOT MODIFY
    └── ValueObject.cs                     # DO NOT MODIFY
```

## Checklist

- [ ] Aggregate root extends `Entity` and implements `IAggregateRoot`
- [ ] Collections are private with `IReadOnlyCollection<>` public accessors
- [ ] All setters are `private` — mutations through behavior methods only
- [ ] Constructor validates invariants and raises a creation domain event
- [ ] Protected parameterless constructor exists for EF Core
- [ ] State transition methods have guard clauses
- [ ] Domain events raised on meaningful state changes (implement `INotification`)
- [ ] Value objects extend `ValueObject` and override `GetEqualityComponents()`
- [ ] Repository interface only defined for aggregate roots, extends `IRepository<T>`
- [ ] Repository interface lives inside the aggregate folder
- [ ] Repository implementation in `Ordering.Infrastructure/Repositories/`
- [ ] EF configuration in `Ordering.Infrastructure/EntityConfigurations/`
- [ ] Domain exception class follows `{DomainName}DomainException` naming
- [ ] No infrastructure dependencies in the domain layer
