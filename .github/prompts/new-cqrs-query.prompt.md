# New Query (CQRS Read Side)

Create a new query following the CQRS read-side patterns established in the Ordering bounded
context of this eShop repository.

## Architectural Context

In the CQRS pattern used by the Ordering service, reads and writes use separate paths:

| Aspect | Commands (Write Side) | Queries (Read Side) |
|--------|-----------------------|---------------------|
| Pipeline | MediatR with behaviors | Direct method call |
| Data access | Repository → Aggregate | `IOrderQueries` → EF Core DbContext |
| Return type | `bool` | Flat read-model DTOs (records) |
| Transactions | Yes (TransactionBehavior) | No (read-only) |
| Models | Rich domain entities | Flat projection records |

**Key design decision:** Queries bypass MediatR entirely. They go through an `IOrderQueries`
interface directly, using EF Core LINQ projections to flat read-model records. This is a
simplified CQRS approach using the same database but different models.

## Required Files

### 1. Query Interface — `src/Ordering.API/Application/Queries/I{ServiceName}Queries.cs`

Add methods to the existing interface (or create a new one for a different service):

```csharp
namespace eShop.Ordering.API.Application.Queries;

public interface IOrderQueries
{
    // Existing methods...
    Task<Order> GetOrderAsync(int id);
    Task<IEnumerable<OrderSummary>> GetOrdersFromUserAsync(string userId);
    Task<IEnumerable<CardType>> GetCardTypesAsync();

    // ADD: New query method
    Task<{ReturnType}> Get{Something}Async({parameters});
}
```

### 2. Read-Model Records — `src/Ordering.API/Application/Queries/I{ServiceName}Queries.cs`

Define flat DTO records in the **same file** as the interface (this is the existing convention):

```csharp
// Flat read-model records — purpose-specific, not shared
public record OrderSummary
{
    public int OrderNumber { get; init; }
    public DateTime Date { get; init; }
    public string Status { get; init; }
    public double Total { get; init; }
}

public record Order
{
    public int OrderNumber { get; init; }
    public DateTime Date { get; init; }
    public string Status { get; init; }
    public string Description { get; init; }
    public string Street { get; init; }
    public string City { get; init; }
    public string State { get; init; }
    public string ZipCode { get; init; }
    public string Country { get; init; }
    public List<OrderItem> OrderItems { get; init; }
    public decimal Total { get; init; }
}

// ADD: New read model for your query
public record {ReadModelName}
{
    public int Id { get; init; }
    public string Name { get; init; }
    // Keep it flat — no nested aggregates or navigation properties
}
```

### 3. Query Implementation — `src/Ordering.API/Application/Queries/{ServiceName}Queries.cs`

```csharp
namespace eShop.Ordering.API.Application.Queries;

public class OrderQueries(OrderingContext context) : IOrderQueries
{
    // Existing methods...

    // ADD: New query implementation
    public async Task<{ReturnType}> Get{Something}Async({parameters})
    {
        // Use EF Core LINQ projections directly — NOT through repositories
        var result = await context.Orders
            .AsNoTracking()                    // Read-only, no change tracking
            .Where(o => o.SomeProperty == value)
            .Select(o => new {ReadModelName}   // Project to flat DTO
            {
                Id = o.Id,
                Name = o.Name,
                // Flatten nested data
                City = o.Address.City,
                Total = o.OrderItems.Sum(i => i.UnitPrice * i.Units)
            })
            .SingleOrDefaultAsync()
            ?? throw new KeyNotFoundException();

        return result;
    }
}
```

### 4. Wire to API Endpoint — `src/Ordering.API/Apis/OrdersApi.cs`

```csharp
// Queries go through the services aggregate, NOT through MediatR
public static async Task<Results<Ok<{ReadModelName}>, NotFound>> Get{Something}(
    [AsParameters] OrderServices services,
    [Description("The order id")] int orderId)
{
    try
    {
        var result = await services.Queries.Get{Something}Async(orderId);
        return TypedResults.Ok(result);
    }
    catch (KeyNotFoundException)
    {
        return TypedResults.NotFound();
    }
}
```

### 5. Register in DI — `src/Ordering.API/Extensions/Extensions.cs`

The queries are already registered if modifying `IOrderQueries`. For a new interface:

```csharp
builder.Services.AddScoped<I{ServiceName}Queries, {ServiceName}Queries>();
```

## Query Pattern Guidelines

### ✅ DO

```csharp
// Project to flat DTOs
.Select(o => new OrderSummary { OrderNumber = o.Id, Status = o.OrderStatus.ToString() })

// Use AsNoTracking for all queries
.AsNoTracking()

// Compose conditionally
var root = context.Orders.AsQueryable();
if (status is not null)
    root = root.Where(o => o.OrderStatus == status);

// Paginate server-side
.Skip(pageSize * pageIndex).Take(pageSize)

// Count separately
var total = await root.LongCountAsync();
```

### ❌ DON'T

```csharp
// ❌ Don't return domain entities from queries
return await context.Orders.SingleOrDefaultAsync(o => o.Id == id);

// ❌ Don't use repositories for queries (that's the write side)
return await orderRepository.GetAsync(id);

// ❌ Don't use MediatR for queries (use direct method calls)
var result = await mediator.Send(new GetOrderQuery(id));

// ❌ Don't load everything then filter in memory
var all = await context.Orders.ToListAsync();
return all.Where(o => o.Status == status);

// ❌ Don't use Include when Select can flatten
.Include(o => o.OrderItems).Include(o => o.Buyer)
```

## For Simple CRUD Services (Catalog-style)

Simple services don't need the `IQueries` abstraction. Query directly in the API handler:

```csharp
public static async Task<Ok<PaginatedItems<CatalogItem>>> GetAll(
    [AsParameters] PaginationRequest paginationRequest,
    [AsParameters] CatalogServices services)
{
    var root = (IQueryable<CatalogItem>)services.Context.CatalogItems;
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

## Checklist

- [ ] Query interface defined or extended (for DDD services)
- [ ] Read-model records are flat `record` types with `init` properties
- [ ] Read-model records defined alongside the interface (same file)
- [ ] Query implementation uses primary constructor for `DbContext` injection
- [ ] All queries use `AsNoTracking()`
- [ ] Projections use `.Select()` to flat DTOs — no returning domain entities
- [ ] No MediatR — queries called directly via `services.Queries.{Method}()`
- [ ] Pagination uses `LongCountAsync()` + `Skip/Take` pattern
- [ ] `KeyNotFoundException` thrown for missing entities, caught in API handler
- [ ] API handler maps exception to `TypedResults.NotFound()`
- [ ] No raw SQL or ADO.NET
