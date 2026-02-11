# New EF Core Entity and Data Access

Add a new entity with full EF Core data access following the established patterns in this eShop
repository.

## Architectural Rules

This repository has strict data access rules:
- **Always use EF Core** with async LINQ — never raw SQL, Dapper, or ADO.NET
- **Never** use `FromSqlRaw`, `ExecuteSqlRaw`, or `SqlQueryRaw` unless explicitly requested
- **Never** introduce `IDbConnection`, `NpgsqlConnection`, or `SqlConnection`
- Use `IEntityTypeConfiguration<T>` for configuration — never data annotations for persistence
- Use **composable `IQueryable<T>`** pipelines with conditional filters
- Always use **async** EF APIs: `ToListAsync()`, `SingleOrDefaultAsync()`, `SaveChangesAsync()`
- Use `AsNoTracking()` for read-only queries

## Required Files

### 1. Entity Model — `src/{ServiceName}.API/Model/{Entity}.cs`

For simple CRUD services (Catalog-style):

```csharp
namespace eShop.{ServiceName}.API.Model;

public class {Entity}
{
    public int Id { get; set; }

    [Required]
    public string Name { get; set; }

    public string Description { get; set; }

    public decimal Price { get; set; }

    // Navigation properties (if relationships exist)
    public int {RelatedEntity}Id { get; set; }
    public {RelatedEntity} {RelatedEntity} { get; set; }

    // Constructor for creation
    public {Entity}(string name)
    {
        Name = name;
    }
}
```

For DTOs / response records:

```csharp
// ✅ Always use record for DTOs
public record {Entity}DTO
{
    public int Id { get; init; }
    public string Name { get; init; }
    public decimal Price { get; init; }
}

// ✅ Positional record for simple, flat DTOs
public record {Entity}SummaryDTO(int Id, string Name, decimal Price);
```

### 2. Entity Configuration — `src/{ServiceName}.API/Infrastructure/EntityConfigurations/{Entity}EntityTypeConfiguration.cs`

```csharp
namespace eShop.{ServiceName}.API.Infrastructure.EntityConfigurations;

class {Entity}EntityTypeConfiguration : IEntityTypeConfiguration<{Entity}>
{
    public void Configure(EntityTypeBuilder<{Entity}> builder)
    {
        builder.ToTable("{entity}");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.Price)
            .HasColumnType("decimal(18,2)");

        // Relationship configuration
        builder.HasOne(e => e.{RelatedEntity})
            .WithMany()
            .HasForeignKey(e => e.{RelatedEntity}Id);

        // Indexes
        builder.HasIndex(e => e.Name);
    }
}
```

### 3. Add DbSet to Context — `src/{ServiceName}.API/Infrastructure/{ServiceName}Context.cs`

```csharp
public class {ServiceName}Context(DbContextOptions<{ServiceName}Context> options) : DbContext(options)
{
    // ADD this DbSet property
    public DbSet<{Entity}> {Entity}s => Set<{Entity}>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof({ServiceName}Context).Assembly);
        builder.UseIntegrationEventLogs();
    }
}
```

### 4. Query Patterns in API Handlers

```csharp
// ✅ Composable IQueryable with conditional filters
public static async Task<Ok<PaginatedItems<{Entity}>>> GetAll(
    [AsParameters] PaginationRequest paginationRequest,
    [AsParameters] {ServiceName}Services services,
    string? name,
    int? categoryId)
{
    var root = (IQueryable<{Entity}>)services.Context.{Entity}s;

    // Apply filters conditionally
    if (name is not null)
        root = root.Where(e => e.Name.StartsWith(name));
    if (categoryId is not null)
        root = root.Where(e => e.CategoryId == categoryId);

    var totalItems = await root.LongCountAsync();

    var itemsOnPage = await root
        .OrderBy(e => e.Name)
        .Skip(paginationRequest.PageSize * paginationRequest.PageIndex)
        .Take(paginationRequest.PageSize)
        .ToListAsync();

    return TypedResults.Ok(new PaginatedItems<{Entity}>(
        paginationRequest.PageIndex,
        paginationRequest.PageSize,
        totalItems,
        itemsOnPage));
}

// ✅ Include related data only when needed
public static async Task<Results<Ok<{Entity}>, NotFound>> GetById(
    [AsParameters] {ServiceName}Services services,
    [Description("The item id")] int id)
{
    var item = await services.Context.{Entity}s
        .Include(e => e.{RelatedEntity})
        .SingleOrDefaultAsync(e => e.Id == id);

    if (item == null) return TypedResults.NotFound();
    return TypedResults.Ok(item);
}

// ✅ AsNoTracking for read-only queries
public static async Task<Ok<List<{Entity}DTO>>> GetSummaries(
    [AsParameters] {ServiceName}Services services)
{
    var items = await services.Context.{Entity}s
        .AsNoTracking()
        .Select(e => new {Entity}DTO
        {
            Id = e.Id,
            Name = e.Name,
            Price = e.Price
        })
        .ToListAsync();

    return TypedResults.Ok(items);
}
```

### 5. Create Migration

After adding the entity and configuration, create a migration:

```bash
dotnet ef migrations add Add{Entity} \
    --project src/{ServiceName}.API \
    --context {ServiceName}Context
```

**Never manually edit generated migration files.**

## Query Pattern Reference

| Operation | Pattern |
|-----------|---------|
| Get all (paginated) | `IQueryable<T>` → conditional `.Where()` → `.LongCountAsync()` → `.Skip().Take().ToListAsync()` |
| Get by ID | `.SingleOrDefaultAsync(e => e.Id == id)` |
| Get by IDs | `.Where(e => ids.Contains(e.Id)).ToListAsync()` |
| Search | `.Where(e => e.Name.StartsWith(query))` |
| Count | `.LongCountAsync()` |
| Create | `context.Items.Add(item)` → `SaveChangesAsync()` |
| Update | Load → modify → `SaveChangesAsync()` |
| Delete | Load → `context.Items.Remove(item)` → `SaveChangesAsync()` |
| Read-only | `.AsNoTracking()` before materialization |
| Include related | `.Include(e => e.Related)` only when needed |
| Projection | `.Select(e => new DTO { ... })` |

## ❌ Anti-Patterns to Avoid

```csharp
// ❌ Raw SQL
await context.Database.ExecuteSqlRawAsync("UPDATE ...");
// ❌ ADO.NET
using var conn = new NpgsqlConnection(connString);
// ❌ Dapper
var items = await conn.QueryAsync<Item>("SELECT * FROM Items");
// ❌ Unbounded query (no Take/pagination)
var allItems = await context.Items.ToListAsync();
// ❌ N+1 query (loading related data in a loop)
foreach (var item in items)
    await context.Entry(item).Reference(i => i.Category).LoadAsync();
// ❌ Synchronous methods
var item = context.Items.Single(i => i.Id == id);
// ❌ String interpolation in queries (SQL injection risk)
context.Items.FromSqlRaw($"SELECT * FROM Items WHERE Name = '{name}'");
```

## Checklist

- [ ] Entity is a class in `Model/` (simple CRUD) or `AggregatesModel/` (DDD)
- [ ] DTOs are records (positional or with init properties)
- [ ] Entity configuration uses `IEntityTypeConfiguration<T>` in `Infrastructure/EntityConfigurations/`
- [ ] No data annotations for persistence on entities (only `[Required]` for validation is OK)
- [ ] `DbSet<T>` added to the `DbContext`
- [ ] All queries use async EF APIs (`ToListAsync`, `SingleOrDefaultAsync`, etc.)
- [ ] Composable `IQueryable<T>` pipelines with conditional filters
- [ ] Pagination uses `LongCountAsync` + `Skip/Take` + `PaginatedItems<T>`
- [ ] `AsNoTracking()` used for read-only queries
- [ ] `Include()` used only when related data is needed
- [ ] No raw SQL, ADO.NET, or Dapper
- [ ] Migration created after adding entity
