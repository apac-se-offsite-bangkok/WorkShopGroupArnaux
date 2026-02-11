---
name: minimal-apis
description: "Use this skill whenever creating, modifying, or reviewing HTTP API endpoints in the eShop project. Triggers on: adding new routes, creating endpoint handlers, building REST APIs, or any controller/endpoint work. eShop uses Minimal APIs exclusively — there are NO MVC controllers. All endpoints use static classes, versioned route groups, TypedResults, and [AsParameters] for DI."
---

# Minimal API Endpoint Conventions

## Core Rule

**eShop uses Minimal APIs exclusively.** Never create MVC controllers, `[ApiController]` classes, or `ControllerBase` subclasses.

## Endpoint Organization

### Static Class per Feature Area

Each API feature is a static class with an extension method that maps routes:

```csharp
public static class CatalogApi
{
    public static IEndpointRouteBuilder MapCatalogApi(this IEndpointRouteBuilder app)
    {
        var vApi = app.NewVersionedApi("Catalog");
        var api = vApi.MapGroup("api/catalog").HasApiVersion(1, 0);

        api.MapGet("/items", GetAllItems)
            .WithName("ListItems")
            .WithSummary("List catalog items")
            .WithDescription("Get a paginated list of items in the catalog.")
            .WithTags("Items");

        return app;
    }

    public static async Task<Ok<PaginatedItems<CatalogItem>>> GetAllItems(
        [AsParameters] PaginationRequest paginationRequest,
        [AsParameters] CatalogServices services)
    {
        // implementation
    }
}
```

### Rules

- **One static class per feature area** — named `{Domain}Api.cs` (e.g., `CatalogApi.cs`, `OrdersApi.cs`)
- **Extension method `Map{Domain}Api[V1]()`** — returns `IEndpointRouteBuilder` or `RouteGroupBuilder`
- **Static methods for handlers** — not lambdas, not instance methods
- **Chain route metadata** — `.WithName()`, `.WithSummary()`, `.WithDescription()`, `.WithTags()`

## API Versioning

Use `Asp.Versioning` with versioned route groups:

```csharp
var vApi = app.NewVersionedApi("Orders");
var api = vApi.MapGroup("api/orders").HasApiVersion(1, 0);
```

- Version format: `'v'VVV` (e.g., `v1.0`, `v2.0`)
- API version passed via query parameter
- Use separate route groups for different versions when endpoints differ

## Dependency Injection with [AsParameters]

Inject services via a record annotated with `[AsParameters]`:

```csharp
// ✅ CORRECT — [AsParameters] service record
public static async Task<Results<Ok, BadRequest<string>>> CancelOrderAsync(
    [FromHeader(Name = "x-requestid")] Guid requestId,
    CancelOrderCommand command,
    [AsParameters] OrderServices services)
{
    // Use services.Logger, services.Mediator, etc.
}

// ❌ WRONG — Constructor injection (this is not a controller)
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    public OrdersController(IMediator mediator) { _mediator = mediator; }
}
```

## Return Types

Always use `TypedResults` with explicit result types:

```csharp
// ✅ CORRECT — TypedResults with union return type
public static async Task<Results<Ok<Order>, NotFound>> GetOrderAsync(
    int orderId, [AsParameters] OrderServices services)
{
    var order = await services.Queries.GetOrderAsync(orderId);
    if (order is null)
        return TypedResults.NotFound();
    return TypedResults.Ok(order);
}

// ❌ WRONG — IActionResult (MVC pattern)
public IActionResult GetOrder(int orderId)
{
    return Ok(order);
}
```

## Program.cs Wiring

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();          // Always call for Aspire defaults
builder.AddApplicationServices();       // Service-specific DI registration
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning();
builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();
app.MapDefaultEndpoints();              // Health checks

var orders = app.NewVersionedApi("Orders");
orders.MapOrdersApiV1().RequireAuthorization();

app.UseDefaultOpenApi();
app.Run();
```

## Common Mistakes to Avoid

1. **Never create a Controller class** — use static classes with Minimal APIs
2. **Never return `IActionResult`** — use `TypedResults.Ok()`, `TypedResults.NotFound()`, etc.
3. **Never use `[HttpGet]`, `[HttpPost]` attributes** — use `MapGet()`, `MapPost()` fluent methods
4. **Never inject via constructor** — use `[AsParameters]` on handler parameters
5. **Never forget `.RequireAuthorization()`** — secured endpoints must opt in
6. **Never use `app.MapControllers()`** — there are no controllers to map
