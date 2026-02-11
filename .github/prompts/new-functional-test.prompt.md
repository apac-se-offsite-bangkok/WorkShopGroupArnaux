# New Functional Test

Create functional (integration) tests following the established patterns in this eShop repository.

## Architectural Context

Functional tests in this repo use **xUnit v3** with **real infrastructure** provisioned via
.NET Aspire. They run against the actual application entry point (`Program`) using
`WebApplicationFactory<Program>`, with real PostgreSQL containers (via Docker) and fake
authentication middleware.

## Required Files

### 1. Test Fixture — `tests/{ServiceName}.FunctionalTests/{ServiceName}ApiFixture.cs`

```csharp
namespace eShop.{ServiceName}.FunctionalTests;

public sealed class {ServiceName}ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly IHost _app;
    public IResourceBuilder<PostgresServerResource> Postgres { get; private set; }
    private string _postgresConnectionString;

    public {ServiceName}ApiFixture()
    {
        var options = new DistributedApplicationOptions
        {
            AssemblyName = typeof({ServiceName}ApiFixture).Assembly.FullName,
            DisableDashboard = true
        };
        var appBuilder = DistributedApplication.CreateBuilder(options);

        // Add infrastructure resources matching what the service needs
        Postgres = appBuilder.AddPostgres("{ServiceName}DB");
        // For services needing pgvector:
        // Postgres = appBuilder.AddPostgres("{ServiceName}DB")
        //     .WithImage("ankane/pgvector").WithImageTag("latest");

        _app = appBuilder.Build();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string>
            {
                { $"ConnectionStrings:{Postgres.Resource.Name}", _postgresConnectionString },
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove real event bus and replace with a no-op for isolation
            services.AddSingleton<IEventBus, NoOpEventBus>();
        });

        return base.CreateHost(builder);
    }

    public async ValueTask InitializeAsync()
    {
        await _app.StartAsync();
        _postgresConnectionString = await Postgres.Resource.GetConnectionStringAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _app.StopAsync();
        _app.Dispose();
    }

    // No-op event bus for test isolation
    private class NoOpEventBus : IEventBus
    {
        public Task PublishAsync(IntegrationEvent @event) => Task.CompletedTask;
    }
}
```

### 2. Auto-Authorize Middleware — `tests/{ServiceName}.FunctionalTests/AutoAuthorizeMiddleware.cs`

For services that require authentication:

```csharp
namespace eShop.{ServiceName}.FunctionalTests;

class AutoAuthorizeMiddleware(RequestDelegate next)
{
    public const string IDENTITY_ID = "9e3163b9-1ae6-4652-9dc6-7898ab7b7a00";

    public async Task Invoke(HttpContext httpContext)
    {
        var identity = new ClaimsIdentity("cookies");
        identity.AddClaim(new Claim("sub", IDENTITY_ID));
        identity.AddClaim(new Claim("unique_name", IDENTITY_ID));
        identity.AddClaim(new Claim(ClaimTypes.Name, IDENTITY_ID));

        httpContext.User.AddIdentity(identity);
        await next.Invoke(httpContext);
    }
}

class AutoAuthorizeStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return builder =>
        {
            builder.UseMiddleware<AutoAuthorizeMiddleware>();
            next(builder);
        };
    }
}
```

Register in the fixture's `CreateHost`:
```csharp
services.AddSingleton<IStartupFilter, AutoAuthorizeStartupFilter>();
```

### 3. Test Class — `tests/{ServiceName}.FunctionalTests/{ServiceName}ApiTests.cs`

```csharp
namespace eShop.{ServiceName}.FunctionalTests;

public sealed class {ServiceName}ApiTests : IClassFixture<{ServiceName}ApiFixture>
{
    private readonly WebApplicationFactory<Program> _webApplicationFactory;
    private readonly HttpClient _httpClient;

    public {ServiceName}ApiTests({ServiceName}ApiFixture fixture)
    {
        // Configure the test client with API versioning
        var options = new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        };

        _webApplicationFactory = fixture;
        _httpClient = fixture.CreateClient(options);
    }

    [Fact]
    public async Task GetAllItemsReturnsOk()
    {
        // Arrange — nothing needed for simple GET

        // Act
        var response = await _httpClient.GetAsync(
            "/api/{service-name}/items?api-version=1.0",
            TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetItemByIdReturnsNotFoundForMissingItem()
    {
        // Act
        var response = await _httpClient.GetAsync(
            "/api/{service-name}/items/99999?api-version=1.0",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public async Task GetItemsRespectsApiVersion(double version)
    {
        // Act
        var response = await _httpClient.GetAsync(
            $"/api/{service-name}/items?api-version={version}",
            TestContext.Current.CancellationToken);

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateItemReturnsCreated()
    {
        // Arrange
        var newItem = new { Name = "Test Item", Price = 9.99 };
        var content = new StringContent(
            JsonSerializer.Serialize(newItem),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _httpClient.PostAsync(
            "/api/{service-name}/items?api-version=1.0",
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task DeleteMissingItemReturnsNotFound()
    {
        // Act
        var response = await _httpClient.DeleteAsync(
            "/api/{service-name}/items/99999?api-version=1.0",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

### 4. GlobalUsings — `tests/{ServiceName}.FunctionalTests/GlobalUsings.cs`

```csharp
global using System.Net;
global using System.Security.Claims;
global using System.Text;
global using System.Text.Json;
global using eShop.EventBus.Abstractions;
global using eShop.EventBus.Events;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Mvc.Testing;
global using Microsoft.AspNetCore.TestHost;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
```

### 5. `.csproj` for Functional Test Project

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.PostgreSQL" />
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="xunit.v3.mtp-v2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\{ServiceName}.API\{ServiceName}.API.csproj" />
  </ItemGroup>

</Project>
```

The service project must expose its internals:
```xml
<!-- In src/{ServiceName}.API/{ServiceName}.API.csproj -->
<InternalsVisibleTo Include="{ServiceName}.FunctionalTests" />
```

## Assert Patterns (xUnit)

| Assertion | Usage |
|-----------|-------|
| `Assert.Equal(expected, actual)` | Equality |
| `Assert.NotNull(obj)` | Null checks |
| `Assert.Contains(substr, str)` | String contains |
| `Assert.All(collection, predicate)` | All items match |
| `Assert.Empty(collection)` | Collection empty |
| `Assert.NotEmpty(collection)` | Collection not empty |

## Key Differences from Unit Tests

| Aspect | Unit Tests | Functional Tests |
|--------|-----------|-----------------|
| Framework | MSTest v3 | xUnit v3 |
| Mocking | NSubstitute | Real services + fake auth |
| Infrastructure | None | Docker containers via Aspire |
| Entry point | Static method calls | HTTP client via `WebApplicationFactory` |
| Auth | Not tested | `AutoAuthorizeMiddleware` fakes claims |
| Isolation | Full (mocked dependencies) | Partial (real DB, fake event bus) |
| Speed | Fast | Slower (container startup) |

## Checklist

- [ ] Fixture extends `WebApplicationFactory<Program>` and implements `IAsyncLifetime`
- [ ] Fixture provisions real infrastructure via Aspire (PostgreSQL, etc.)
- [ ] Fixture provides `InitializeAsync` / `DisposeAsync` for container lifecycle
- [ ] `AutoAuthorizeMiddleware` fakes authentication (if service requires auth)
- [ ] Test class implements `IClassFixture<{Fixture}>` for shared fixture
- [ ] Tests use `HttpClient` from `CreateClient()` — never call handlers directly
- [ ] API version is included in query string (`?api-version=1.0`)
- [ ] Uses xUnit `[Fact]` and `[Theory]` attributes — never MSTest `[TestMethod]`
- [ ] Uses `TestContext.Current.CancellationToken` for async operations
- [ ] Event bus replaced with no-op for test isolation
- [ ] `.csproj` uses xUnit v3 with Microsoft Testing Platform runner
- [ ] Service project has `InternalsVisibleTo` for the test project
