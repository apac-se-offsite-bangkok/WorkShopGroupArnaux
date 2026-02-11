# New Minimal API Endpoint

Create a new API endpoint following the established patterns in this eShop repository.

## Strict Architectural Rules

This repository uses **Minimal APIs exclusively** — never generate MVC controllers. All HTTP
endpoints are static methods in a static class inside an `Apis/` folder.

## Required File Modifications

When adding a new endpoint to an **existing** API project (`src/{ServiceName}.API/`), you must
modify or create the following files:

### 1. Endpoint Definition — `src/{ServiceName}.API/Apis/{ServiceName}Api.cs`

Add the new endpoint inside the existing `Map{ServiceName}Api(...)` extension method. Follow this
exact pattern:

```csharp
// The mapping method is a static extension method on IEndpointRouteBuilder
public static class {ServiceName}Api
{
    public static IEndpointRouteBuilder Map{ServiceName}Api(this IEndpointRouteBuilder app)
    {
        // Create a versioned API group
        var vApi = app.NewVersionedApi("{ServiceName}");
        var v1 = vApi.MapGroup("api/{service-name}").HasApiVersion(1, 0);

        // Map endpoint with full OpenAPI metadata
        v1.MapGet("/{route}", GetSomething)
            .WithName("GetSomething")
            .WithSummary("Brief summary of what this endpoint does")
            .WithDescription("Detailed description of the endpoint behavior.")
            .WithTags("TagName");

        return app;
    }

    // Handler is a STATIC method on the same class
    public static async Task<Results<Ok<ResponseType>, NotFound>> GetSomething(
        [AsParameters] {ServiceName}Services services,
        [Description("Description of the parameter")] int id)
    {
        // Implementation here
    }
}
```

### 2. Handler Method Patterns

Always use **TypedResults** with explicit union return types via `Results<T1, T2, ...>`:

```csharp
// GET by ID
public static async Task<Results<Ok<ItemType>, NotFound, BadRequest<ProblemDetails>>> GetById(
    [AsParameters] {ServiceName}Services services,
    [Description("The item id")] int id)
{
    if (id <= 0)
        return TypedResults.BadRequest<ProblemDetails>(new() { Detail = "Id is not valid." });

    var item = await services.Context.Items.SingleOrDefaultAsync(i => i.Id == id);
    if (item == null) return TypedResults.NotFound();
    return TypedResults.Ok(item);
}

// GET with pagination
public static async Task<Ok<PaginatedItems<ItemType>>> GetAll(
    [AsParameters] PaginationRequest paginationRequest,
    [AsParameters] {ServiceName}Services services)
{
    var pageSize = paginationRequest.PageSize;
    var pageIndex = paginationRequest.PageIndex;
    var totalItems = await services.Context.Items.LongCountAsync();
    var itemsOnPage = await services.Context.Items
        .OrderBy(i => i.Name)
        .Skip(pageSize * pageIndex)
        .Take(pageSize)
        .ToListAsync();

    return TypedResults.Ok(new PaginatedItems<ItemType>(pageIndex, pageSize, totalItems, itemsOnPage));
}

// POST (create)
public static async Task<Created> CreateItem(
    [AsParameters] {ServiceName}Services services,
    ItemType item)
{
    services.Context.Items.Add(item);
    await services.Context.SaveChangesAsync();
    return TypedResults.Created($"/api/{service-name}/items/{item.Id}");
}

// PUT (update)
public static async Task<Results<Created, NotFound<ProblemDetails>>> UpdateItem(
    [AsParameters] {ServiceName}Services services,
    int id, ItemType updatedItem)
{
    var existing = await services.Context.Items.SingleOrDefaultAsync(i => i.Id == id);
    if (existing == null)
        return TypedResults.NotFound<ProblemDetails>(new() { Detail = $"Item with id {id} not found." });

    // Update properties...
    await services.Context.SaveChangesAsync();
    return TypedResults.Created($"/api/{service-name}/items/{id}");
}

// DELETE
public static async Task<Results<NoContent, NotFound>> DeleteItem(
    [AsParameters] {ServiceName}Services services,
    int id)
{
    var item = services.Context.Items.SingleOrDefault(x => x.Id == id);
    if (item is null) return TypedResults.NotFound();

    services.Context.Items.Remove(item);
    await services.Context.SaveChangesAsync();
    return TypedResults.NoContent();
}
```

### 3. Services Aggregate Class — `src/{ServiceName}.API/Apis/{ServiceName}Services.cs`

If it doesn't already exist, create a **primary-constructor services class** to bundle dependencies
for `[AsParameters]` injection:

```csharp
public class {ServiceName}Services(
    {ServiceName}Context context,
    IOptions<{ServiceName}Options> options,
    ILogger<{ServiceName}Services> logger)
{
    public {ServiceName}Context Context { get; } = context;
    public IOptions<{ServiceName}Options> Options { get; } = options;
    public ILogger<{ServiceName}Services> Logger { get; } = logger;
}
```

Use `[FromServices]` only when a parameter cannot be resolved automatically. All properties must be
public get-only.

### 4. Authorization

Apply authorization at the **route group level**, never per-endpoint:

```csharp
var api = vApi.MapGroup("api/{service-name}").HasApiVersion(1, 0);
// If the API requires authentication:
api.RequireAuthorization();
```

Public/anonymous APIs simply omit `.RequireAuthorization()`.

### 5. Checklist

- [ ] Endpoint is a static method in a static class under `Apis/`
- [ ] Uses `[AsParameters]` with a Services aggregate class for DI
- [ ] Uses `TypedResults` with `Results<...>` union return type
- [ ] Every endpoint has `.WithName()`, `.WithSummary()`, `.WithDescription()`, `.WithTags()`
- [ ] Parameters have `[Description("...")]` attributes for OpenAPI
- [ ] Uses API versioning via `NewVersionedApi()` and `HasApiVersion()`
- [ ] Pagination uses `PaginationRequest` record and returns `PaginatedItems<T>`
- [ ] Error responses use `ProblemDetails` (registered via `builder.Services.AddProblemDetails()`)
- [ ] No MVC controllers — Minimal APIs only
