---
name: ef-database
description: "Use this skill whenever a task involves database access, data queries, migrations, DbContext, repositories, or data persistence in the eShop project. Triggers on: writing LINQ queries, creating or modifying DbContext classes, adding EF Core migrations, implementing repository patterns, or any data access layer work. NEVER use raw SQL, ADO.NET, or Dapper — this project exclusively uses Entity Framework Core with PostgreSQL."
---

# Entity Framework Core Database Conventions

## Core Rule

**ALL data access in eShop uses Entity Framework Core.** Never suggest raw SQL, ADO.NET, SqlConnection, SqlCommand, or Dapper.

## DbContext Pattern

Each service owns its DbContext. Follow this exact pattern:

```csharp
public class CatalogContext : DbContext
{
    public CatalogContext(DbContextOptions<CatalogContext> options, IConfiguration configuration) : base(options)
    {
    }

    public required DbSet<CatalogItem> CatalogItems { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new CatalogItemEntityTypeConfiguration());
        builder.UseIntegrationEventLogs();
    }
}
```

### Rules

- Use `required` keyword on `DbSet<T>` properties
- Apply entity configurations via `IEntityTypeConfiguration<T>` classes — never use inline fluent API in `OnModelCreating`
- Call `builder.UseIntegrationEventLogs()` if the service publishes integration events
- PostgreSQL is the only database provider — use `HasPostgresExtension()` for extensions like pgvector

## Querying Data

Use LINQ with EF Core. Never write raw SQL.

```csharp
// ✅ CORRECT — LINQ via EF Core
var items = await context.CatalogItems
    .Where(ci => ci.Name.StartsWith(name))
    .OrderBy(c => c.Name)
    .Skip(pageSize * pageIndex)
    .Take(pageSize)
    .ToListAsync();

// ❌ WRONG — Raw SQL
var items = await context.Database
    .SqlRawAsync("SELECT * FROM CatalogItems WHERE Name LIKE @name");

// ❌ WRONG — Dapper
using var connection = new NpgsqlConnection(connectionString);
var items = await connection.QueryAsync<CatalogItem>("SELECT * FROM CatalogItems");
```

## Migrations

Add migrations from within the project directory:

```bash
dotnet ef migrations add --context CatalogContext [migration-name]
```

## Entity Configuration

Use dedicated configuration classes implementing `IEntityTypeConfiguration<T>`:

```csharp
class CatalogItemEntityTypeConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("Catalog");
        builder.Property(ci => ci.Name).HasMaxLength(50).IsRequired();
    }
}
```

## Database Names

Use lowercase for database names: `catalogdb`, `orderingdb`, `identitydb`, `webhooksdb`.

## Common Mistakes to Avoid

1. **Never use `FromSqlRaw()` or `FromSqlInterpolated()`** — use LINQ
2. **Never create `SqlConnection` or `NpgsqlConnection` directly** — use DbContext
3. **Never use `Database.ExecuteSqlRaw()`** — use EF Core's change tracker and `SaveChangesAsync()`
4. **Never add connection strings in code** — they are configured via .NET Aspire in the AppHost
5. **Never use `DbContext` with `AddDbContext` directly** — use the Aspire service registration pattern
