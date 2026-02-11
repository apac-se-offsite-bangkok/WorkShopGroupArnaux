# New Microservice

Scaffold a complete new microservice following the established patterns in this eShop repository.

## Architectural Context

Every microservice in this repo follows a consistent skeleton. Services are orchestrated by
.NET Aspire (`eShop.AppHost`) and share cross-cutting concerns via `eShop.ServiceDefaults`.

There are two service archetypes:

1. **Simple CRUD service** (like Catalog.API) — direct `DbContext` in Minimal API handlers, no
   domain layer, no MediatR
2. **DDD/CQRS service** (like Ordering.API) — aggregate roots, domain events, MediatR commands,
   repositories, separate Infrastructure project

Choose the archetype based on the domain complexity. **Default to the simple CRUD archetype**
unless the domain has complex business rules, state machines, or invariants.

## Files to Create — Simple CRUD Service

### 1. Project File — `src/{ServiceName}.API/{ServiceName}.API.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <RootNamespace>eShop.{ServiceName}.API</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Asp.Versioning.Http" />
    <PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\EventBusRabbitMQ\EventBusRabbitMQ.csproj" />
    <ProjectReference Include="..\eShop.ServiceDefaults\eShop.ServiceDefaults.csproj" />
    <ProjectReference Include="..\IntegrationEventLogEF\IntegrationEventLogEF.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="..\Shared\MigrateDbContextExtensions.cs" Link="Extensions\MigrateDbContextExtensions.cs" />
    <Compile Include="..\Shared\ActivityExtensions.cs" Link="Extensions\ActivityExtensions.cs" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="{ServiceName}.FunctionalTests" />
  </ItemGroup>

</Project>
```

### 2. Program.cs — `src/{ServiceName}.API/Program.cs`

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning();
builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

app.MapDefaultEndpoints();
app.Map{ServiceName}Api();
app.UseDefaultOpenApi();
app.Run();
```

Every `Program.cs` follows this exact order:
1. Service defaults
2. Application services
3. ProblemDetails
4. API versioning + OpenAPI
5. Build
6. Default endpoints (health checks)
7. Map API routes
8. OpenAPI UI
9. Run

### 3. GlobalUsings.cs — `src/{ServiceName}.API/GlobalUsings.cs`

```csharp
global using System.ComponentModel;
global using System.ComponentModel.DataAnnotations;
global using Asp.Versioning;
global using eShop.EventBus.Abstractions;
global using eShop.EventBus.Events;
global using eShop.{ServiceName}.API;
global using eShop.{ServiceName}.API.Infrastructure;
global using eShop.{ServiceName}.API.Model;
global using eShop.{ServiceName}.API.IntegrationEvents.Events;
global using eShop.{ServiceName}.API.IntegrationEvents.EventHandling;
global using eShop.IntegrationEventLogEF;
global using eShop.IntegrationEventLogEF.Services;
global using eShop.ServiceDefaults;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Options;
```

### 4. Extensions — `src/{ServiceName}.API/Extensions/Extensions.cs`

```csharp
namespace eShop.{ServiceName}.API;

public static class Extensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        // 1. Database context
        builder.AddNpgsqlDbContext<{ServiceName}Context>("{servicename}db",
            configureDbContextOptions: dbContextOptionsBuilder =>
            {
                dbContextOptionsBuilder.UseNpgsql(builder =>
                {
                    builder.UseVector();  // Only if using pgvector
                });
            });

        // 2. Migrations
        builder.Services.AddMigration<{ServiceName}Context, {ServiceName}ContextSeed>();

        // 3. Integration event services (outbox)
        builder.Services.AddTransient<IIntegrationEventLogService,
            IntegrationEventLogService<{ServiceName}Context>>();
        builder.Services.AddTransient<I{ServiceName}IntegrationEventService,
            {ServiceName}IntegrationEventService>();

        // 4. Event bus + subscriptions
        builder.AddRabbitMqEventBus("eventbus")
               .AddSubscription<SomeIntegrationEvent, SomeIntegrationEventHandler>();

        // 5. Options
        builder.Services.AddOptions<{ServiceName}Options>()
            .BindConfiguration(nameof({ServiceName}Options));

        // 6. Application services
        builder.Services.AddScoped<ISomeService, SomeService>();
    }
}
```

### 5. API Endpoints — `src/{ServiceName}.API/Apis/{ServiceName}Api.cs`

See the `new-api-endpoint.prompt.md` template for detailed endpoint patterns.

```csharp
namespace eShop.{ServiceName}.API;

public static class {ServiceName}Api
{
    public static IEndpointRouteBuilder Map{ServiceName}Api(this IEndpointRouteBuilder app)
    {
        var vApi = app.NewVersionedApi("{ServiceName}");
        var v1 = vApi.MapGroup("api/{service-name}").HasApiVersion(1, 0);

        // Map CRUD endpoints with OpenAPI metadata
        v1.MapGet("/items", GetAllItems)
            .WithName("List{ServiceName}Items")
            .WithSummary("List all items")
            .WithDescription("Get a paginated list of items.")
            .WithTags("Items");

        v1.MapGet("/items/{id:int}", GetItemById)
            .WithName("Get{ServiceName}ItemById")
            .WithSummary("Get item by ID")
            .WithDescription("Get a single item by its identifier.")
            .WithTags("Items");

        // ... more endpoints

        return app;
    }

    // Handler methods follow (see new-api-endpoint.prompt.md)
}
```

### 6. Services Aggregate — `src/{ServiceName}.API/Apis/{ServiceName}Services.cs`

```csharp
namespace eShop.{ServiceName}.API;

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

### 7. DbContext — `src/{ServiceName}.API/Infrastructure/{ServiceName}Context.cs`

```csharp
namespace eShop.{ServiceName}.API.Infrastructure;

public class {ServiceName}Context(DbContextOptions<{ServiceName}Context> options) : DbContext(options)
{
    public DbSet<{Entity}> {Entity}s => Set<{Entity}>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof({ServiceName}Context).Assembly);

        // If using outbox pattern:
        builder.UseIntegrationEventLogs();
    }
}
```

### 8. Entity Configuration — `src/{ServiceName}.API/Infrastructure/EntityConfigurations/{Entity}EntityTypeConfiguration.cs`

```csharp
namespace eShop.{ServiceName}.API.Infrastructure.EntityConfigurations;

class {Entity}EntityTypeConfiguration : IEntityTypeConfiguration<{Entity}>
{
    public void Configure(EntityTypeBuilder<{Entity}> builder)
    {
        builder.ToTable("{entity}");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).IsRequired().HasMaxLength(100);
        // ... more column configuration
    }
}
```

### 9. Domain Model — `src/{ServiceName}.API/Model/{Entity}.cs`

```csharp
namespace eShop.{ServiceName}.API.Model;

public class {Entity}
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; }

    public string Description { get; set; }

    public decimal Price { get; set; }

    // Constructor for creation
    public {Entity}(string name)
    {
        Name = name;
    }
}
```

### 10. Options — `src/{ServiceName}.API/{ServiceName}Options.cs`

```csharp
namespace eShop.{ServiceName}.API;

public class {ServiceName}Options
{
    public string SomeConfigValue { get; set; }
}
```

### 11. App Settings — `src/{ServiceName}.API/appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### 12. AppHost Registration — `src/eShop.AppHost/Program.cs`

```csharp
// Add database
var {servicename}Db = postgres.AddDatabase("{servicename}db");

// Add service project
var {serviceName}Api = builder.AddProject<Projects.{ServiceName}_API>("{service-name}-api")
    .WithReference({servicename}Db)
    .WithReference(eventBus);
```

See the `new-apphost-registration.prompt.md` template for full details.

## Folder Structure

```
src/{ServiceName}.API/
├── Program.cs
├── GlobalUsings.cs
├── {ServiceName}.API.csproj
├── {ServiceName}Options.cs
├── appsettings.json
├── appsettings.Development.json
├── Apis/
│   ├── {ServiceName}Api.cs
│   └── {ServiceName}Services.cs
├── Extensions/
│   └── Extensions.cs
├── Infrastructure/
│   ├── {ServiceName}Context.cs
│   └── EntityConfigurations/
│       └── {Entity}EntityTypeConfiguration.cs
├── IntegrationEvents/
│   ├── Events/
│   └── EventHandling/
├── Model/
│   └── {Entity}.cs
└── Properties/
    └── launchSettings.json
```

## Checklist

- [ ] `.csproj` uses `Microsoft.NET.Sdk.Web` with `RootNamespace` set to `eShop.{ServiceName}.API`
- [ ] References `eShop.ServiceDefaults`, `EventBusRabbitMQ`, `IntegrationEventLogEF`
- [ ] Links shared files (`MigrateDbContextExtensions.cs`, `ActivityExtensions.cs`)
- [ ] `InternalsVisibleTo` set for functional test project
- [ ] `Program.cs` follows the exact 9-step order
- [ ] `Extensions.cs` has `AddApplicationServices` on `IHostApplicationBuilder`
- [ ] `DbContext` uses primary constructor and `ApplyConfigurationsFromAssembly`
- [ ] Entity configurations use `IEntityTypeConfiguration<T>` (not data annotations on entities)
- [ ] Service registered in AppHost with `.WithReference()` for dependencies
- [ ] Database added to PostgreSQL server in AppHost
- [ ] Uses file-scoped namespaces throughout
- [ ] All packages reference versions via central package management (no Version in csproj)
