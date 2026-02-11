---
name: testing-conventions
description: "Use this skill whenever writing, modifying, or reviewing unit tests or functional tests in the eShop project. Triggers on: creating test classes, writing test methods, setting up mocks, or building test data. eShop uses MSTest for unit tests, xUnit for functional tests, NSubstitute for mocking (never Moq), and the builder pattern for complex test objects."
---

# Testing Conventions

## Core Rules

1. **MSTest** for unit tests, **xUnit v3** for functional tests
2. **NSubstitute** for mocking — never use Moq, FakeItEasy, or other mocking libraries
3. **Arrange / Act / Assert** with explicit comments
4. **Builder pattern** for constructing complex domain objects

## Unit Test Structure

### Test Class

```csharp
[TestClass]
public class OrderAggregateTest
{
    [TestMethod]
    public void Create_order_item_success()
    {
        // Arrange
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = 5;

        // Act
        var fakeOrderItem = new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units);

        // Assert
        Assert.IsNotNull(fakeOrderItem);
    }

    [TestMethod]
    public void Invalid_number_of_units()
    {
        // Arrange
        var productId = 1;
        var productName = "FakeProductName";
        var unitPrice = 12;
        var discount = 15;
        var pictureUrl = "FakeUrl";
        var units = -1;

        // Act - Assert
        Assert.ThrowsExactly<OrderingDomainException>(
            () => new OrderItem(productId, productName, unitPrice, discount, pictureUrl, units));
    }
}
```

### Rules

- Use `[TestClass]` and `[TestMethod]` attributes (MSTest)
- Include `// Arrange`, `// Act`, `// Assert` comments (or `// Act - Assert` when combined)
- Test method naming: `{Action}_when_{condition}` or descriptive scenario (e.g., `Create_order_item_success`, `Throws_when_quantity_is_zero`)
- One assertion concept per test method

## Mocking with NSubstitute

```csharp
// ✅ CORRECT — NSubstitute
var mediator = Substitute.For<IMediator>();
mediator.Send(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
    .Returns(true);

// ❌ WRONG — Moq
var mediator = new Mock<IMediator>();
mediator.Setup(m => m.Send(It.IsAny<CreateOrderCommand>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(true);

// ❌ WRONG — FakeItEasy
var mediator = A.Fake<IMediator>();
A.CallTo(() => mediator.Send(A<CreateOrderCommand>._, A<CancellationToken>._))
    .Returns(true);
```

## Builder Pattern for Test Objects

Use builders for domain objects that have complex construction:

```csharp
public class AddressBuilder
{
    public Address Build()
    {
        return new Address("street", "city", "state", "country", "zipcode");
    }
}

public class OrderBuilder
{
    private readonly Order order;

    public OrderBuilder(Address address)
    {
        order = new Order(
            "userId", "fakeName", address,
            cardTypeId: 5, cardNumber: "12",
            cardSecurityNumber: "123", cardHolderName: "name",
            cardExpiration: DateTime.UtcNow);
    }

    public OrderBuilder AddOne(
        int productId, string productName,
        decimal unitPrice, decimal discount,
        string pictureUrl, int units = 1)
    {
        order.AddOrderItem(productId, productName, unitPrice, discount, pictureUrl, units);
        return this;
    }

    public Order Build()
    {
        return order;
    }
}
```

### Using Builders in Tests

```csharp
[TestMethod]
public void Add_order_item_success()
{
    // Arrange
    var address = new AddressBuilder().Build();
    var order = new OrderBuilder(address)
        .AddOne(1, "Product", 10m, 0m, "pic.jpg", 2)
        .Build();

    // Act & Assert
    Assert.IsNotNull(order);
}
```

## Parallel Execution

MSTest projects use assembly-level parallelization:

```csharp
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
```

## Functional Tests

Functional tests use xUnit v3 with .NET Aspire's `DistributedApplicationTestingBuilder`:

- Require Docker (they spin up real PostgreSQL containers)
- Live in `tests/{Service}.FunctionalTests/`
- Test real HTTP endpoints against the full service stack

## Test Project Layout

```
tests/
├── Basket.UnitTests/        # MSTest, no Docker
├── Ordering.UnitTests/      # MSTest, no Docker, uses builders
├── Catalog.FunctionalTests/ # xUnit, requires Docker
└── Ordering.FunctionalTests/ # xUnit, requires Docker
```

## Common Mistakes to Avoid

1. **Never use Moq** — use NSubstitute (`Substitute.For<T>()`)
2. **Never skip `// Arrange / Act / Assert` comments** — they are required
3. **Never create test objects with complex constructors inline** — use the builder pattern
4. **Never mix MSTest attributes in xUnit projects** or vice versa
5. **Never add Docker-dependent tests to unit test projects** — those belong in functional tests
6. **Never suppress `TreatWarningsAsErrors`** — fix the warnings instead
