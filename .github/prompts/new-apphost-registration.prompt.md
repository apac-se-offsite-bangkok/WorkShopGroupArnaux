# Register Service in AppHost

Wire a new or existing service into the .NET Aspire AppHost orchestrator, following the established
patterns in this eShop repository.

## Architectural Context

The Aspire AppHost (`src/eShop.AppHost/`) orchestrates all microservices, infrastructure resources,
and their dependencies. Every service in this repo is registered through the AppHost.

The AppHost uses these Aspire primitives:
- `AddPostgres()` → `AddDatabase()` — PostgreSQL servers with named databases
- `AddRabbitMQ()` — message broker
- `AddRedis()` — caching
- `AddProject<T>()` — .NET project resources
- `.WithReference()` — injects connection strings / service URLs
- `.WaitFor()` — enforces startup ordering
- `.WithExternalHttpEndpoints()` — exposes to external traffic

## Required Modifications

### 1. Add Project Reference — `src/eShop.AppHost/eShop.AppHost.csproj`

```xml
<ItemGroup>
  <!-- Add alongside existing project references -->
  <ProjectReference Include="..\{ServiceName}.API\{ServiceName}.API.csproj" />
</ItemGroup>
```

### 2. Add Database (if service needs persistence) — `src/eShop.AppHost/Program.cs`

```csharp
// In the existing PostgreSQL server setup, add a new database:
var postgres = builder.AddPostgres("postgres")
    .WithImage("ankane/pgvector")
    .WithImageTag("latest")
    .WithLifetime(ContainerLifetime.Persistent);

// Existing databases...
var catalogDb = postgres.AddDatabase("catalogdb");
var orderingDb = postgres.AddDatabase("orderingdb");
// ADD: New database for the new service
var {servicename}Db = postgres.AddDatabase("{servicename}db");
```

### 3. Register the Service Project — `src/eShop.AppHost/Program.cs`

```csharp
// Add the service project with its dependencies
var {serviceName}Api = builder.AddProject<Projects.{ServiceName}_API>("{service-name}-api")
    .WithReference({servicename}Db)        // Database dependency
    .WithReference(eventBus)                // RabbitMQ event bus
    .WaitFor({servicename}Db);             // Wait for DB to be ready

// If the service requires authentication:
{serviceName}Api = {serviceName}Api
    .WithEnvironment("Identity__Url", identityEndpoint);

// If the service needs external access:
{serviceName}Api = {serviceName}Api
    .WithExternalHttpEndpoints();
```

### 4. Wire Downstream Dependencies (if other services call this one)

```csharp
// If webapp needs to call the new service:
var webApp = builder.AddProject<Projects.WebApp>("webapp")
    .WithReference({serviceName}Api)   // Add reference
    // ... existing references ...
    ;

// If mobile BFF needs to proxy to this service, update Extensions.cs:
// See the YARP proxy configuration section below.
```

### 5. Add Identity Callback URL (if service uses OAuth) — `src/eShop.AppHost/Program.cs`

```csharp
// Identity API needs to know about all client URLs for OAuth redirects
var identityApi = builder.AddProject<Projects.Identity_API>("identity-api")
    .WithReference(identityDb)
    .WithReference(eventBus)
    .WithExternalHttpEndpoints()
    // ADD: callback for new service
    .WithEnvironment("{ServiceName}ApiClient", {serviceName}Api.GetEndpoint("https"));
```

## Naming Conventions

| Concept | Convention | Example |
|---------|-----------|---------|
| Project reference | `Projects.{ServiceName}_API` | `Projects.Catalog_API` |
| Resource name (kebab-case) | `"{service-name}-api"` | `"catalog-api"` |
| Database name (lowercase) | `"{servicename}db"` | `"catalogdb"` |
| Variable name (camelCase) | `{serviceName}Api` | `catalogApi` |
| DB variable (camelCase) | `{servicename}Db` | `catalogDb` |
| Environment variable | `{ServiceName}__Url` | `Identity__Url` |

## Reference: Existing AppHost Patterns

```csharp
// Infrastructure
var redis = builder.AddRedis("redis");
var rabbitMq = builder.AddRabbitMQ("eventbus");
var postgres = builder.AddPostgres("postgres").WithPgAdmin();

// Databases on shared PostgreSQL
var catalogDb = postgres.AddDatabase("catalogdb");
var identityDb = postgres.AddDatabase("identitydb");
var orderingDb = postgres.AddDatabase("orderingdb");

// Services follow this pattern:
var catalogApi = builder.AddProject<Projects.Catalog_API>("catalog-api")
    .WithReference(eventBus)
    .WithReference(catalogDb);

var basketApi = builder.AddProject<Projects.Basket_API>("basket-api")
    .WithReference(redis)
    .WithReference(eventBus);

var orderingApi = builder.AddProject<Projects.Ordering_API>("ordering-api")
    .WithReference(eventBus)
    .WithReference(orderingDb)
    .WaitFor(orderingDb)
    .WithEnvironment("Identity__Url", identityEndpoint);
```

## Checklist

- [ ] Project reference added to `eShop.AppHost.csproj`
- [ ] Database added to the shared PostgreSQL server (if needed)
- [ ] Service registered via `AddProject<T>()` with correct resource name (kebab-case)
- [ ] `.WithReference()` chains for all dependencies (DB, event bus, other services)
- [ ] `.WaitFor()` used if service depends on infrastructure that runs migrations
- [ ] `.WithExternalHttpEndpoints()` if service needs external access
- [ ] Identity URL environment variable set if service uses authentication
- [ ] Identity API updated with callback URL if service uses OAuth
- [ ] Downstream services updated with `.WithReference()` if they call this service
- [ ] Resource name follows kebab-case convention
- [ ] Database name follows lowercase convention (no hyphens)
