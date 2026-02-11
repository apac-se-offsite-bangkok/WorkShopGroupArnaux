# New Unit Test

Create unit tests following the established patterns in this eShop repository.

## Architectural Context

This repository uses two test frameworks:

| Test Type | Framework | SDK | Location |
|-----------|-----------|-----|----------|
| Unit tests | **MSTest v3** | `MSTest.Sdk` | `tests/{ServiceName}.UnitTests/` |
| Functional tests | **xUnit v3** | `xunit.v3.mtp-v2` | `tests/{ServiceName}.FunctionalTests/` |

Both use the **Microsoft Testing Platform** runner. Mocking is done exclusively with
**NSubstitute** — never use Moq, FakeItEasy, or other mocking frameworks. Assertions use the
built-in framework assertions only — never FluentAssertions.

## Required Files

### 1. Test Class — `tests/{ServiceName}.UnitTests/{Layer}/{ClassName}Test.cs`

```csharp
namespace eShop.{ServiceName}.UnitTests.{Layer};

[TestClass]
public class {ClassName}Test
{
    // Mocks initialized in constructor (not [TestInitialize])
    private readonly IMediator _mediatorMock;
    private readonly IOrderRepository _orderRepositoryMock;
    private readonly ILogger<{ClassUnderTest}> _loggerMock;

    public {ClassName}Test()
    {
        _mediatorMock = Substitute.For<IMediator>();
        _orderRepositoryMock = Substitute.For<IOrderRepository>();
        _loggerMock = Substitute.For<ILogger<{ClassUnderTest}>>();
    }

    [TestMethod]
    public async Task {Action}_with_{condition}_{expected_result}()
    {
        // Arrange
        _mediatorMock
            .Send(Arg.Any<{CommandType}>(), default)
            .Returns(Task.FromResult(true));

        var sut = new {ClassUnderTest}(_orderRepositoryMock, _loggerMock);

        // Act
        var result = await sut.Handle(new {CommandType}(orderId: 1), default);

        // Assert
        Assert.IsTrue(result);
    }
}
```

### 2. Test Naming Conventions

Method names describe the scenario in natural language using underscores:

```csharp
// Pattern: {Action}_with_{condition}_{expected_result}  OR  {Action}_{expected_result}
Cancel_order_with_requestId_success()
Handle_return_false_if_order_is_not_persisted()
Create_buyer_item_success()
Invalid_number_of_units()
Add_new_Order_raises_new_event()

// PascalCase is also acceptable for simpler names:
GetBasketReturnsEmptyForNoUser()
GetBasketReturnsItemsForValidUserId()
```

### 3. Test Organization

Mirror the production code's architectural layers:

```
tests/{ServiceName}.UnitTests/
├── {ServiceName}.UnitTests.csproj
├── GlobalUsings.cs
├── Builders.cs                    # Test data builders (if needed)
├── Application/                   # Tests for command handlers, API handlers
│   ├── {Handler}Test.cs
│   └── {ApiEndpoint}Test.cs
└── Domain/                        # Tests for aggregates, value objects, entities
    ├── {Aggregate}Test.cs
    └── SeedWork/
        └── ValueObjectTests.cs
```

### 4. NSubstitute Patterns

```csharp
// Create a mock
var mock = Substitute.For<IService>();

// Stub return values
mock.GetAsync(Arg.Any<int>()).Returns(Task.FromResult(someEntity));
mock.Send(Arg.Any<SomeCommand>(), default).Returns(Task.FromResult(true));

// Stub exceptions
mock.GetAsync(Arg.Any<int>()).Throws(new KeyNotFoundException());

// Verify calls were received
await mock.Received().Send(Arg.Any<IRequest<bool>>(), default);

// Verify calls were NOT received
await mock.DidNotReceive().Send(Arg.Any<IRequest<bool>>(), default);
```

### 5. Testing API Endpoint Handlers

Since endpoints are static methods, test them by calling the static method directly and passing
mocked services:

```csharp
[TestMethod]
public async Task Cancel_order_with_requestId_success()
{
    // Arrange
    _mediatorMock
        .Send(Arg.Any<IdentifiedCommand<CancelOrderCommand, bool>>(), default)
        .Returns(Task.FromResult(true));

    // Create the services aggregate with mocks
    var orderServices = new OrderServices(
        _mediatorMock, _orderQueriesMock, _identityServiceMock, _loggerMock);

    // Act — call the static API handler directly
    var result = await OrdersApi.CancelOrderAsync(
        Guid.NewGuid(),
        new CancelOrderCommand(1),
        orderServices);

    // Assert
    Assert.IsInstanceOfType<Ok>(result.Result);
}
```

### 6. Testing Domain Aggregates

Test invariants, business rules, and domain event generation:

```csharp
[TestMethod]
public void Add_new_Order_raises_new_event()
{
    // Arrange
    var address = new AddressBuilder().Build();

    // Act
    var order = new Order(
        "userId", "fakeName", address,
        cardTypeId: 5, cardNumber: "12",
        cardSecurityNumber: "123", cardHolderName: "name",
        cardExpiration: DateTime.UtcNow);

    // Assert
    Assert.AreEqual(1, order.DomainEvents.Count);
}

[TestMethod]
public void Invalid_number_of_units()
{
    // Arrange & Act & Assert
    Assert.ThrowsException<OrderingDomainException>(() =>
        new OrderItem(productId: 1, productName: "cup", unitPrice: 10,
            discount: 0, pictureUrl: string.Empty, units: -1));
}
```

### 7. Test Data Builders — `tests/{ServiceName}.UnitTests/Builders.cs`

```csharp
namespace eShop.{ServiceName}.UnitTests;

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

    public Order Build() => order;
}
```

Use builders for aggregates that need complex setup. For simple values, inline construction is fine.

### 8. Parameterized Tests

```csharp
// Use [DynamicData] for parameterized MSTest tests
[TestMethod]
[DynamicData(nameof(TestData))]
public void Equals_EqualValueObjects_ReturnsTrue(ValueObject a, ValueObject b, string reason)
{
    var result = EqualityComparer<ValueObject>.Default.Equals(a, b);
    Assert.IsTrue(result, reason);
}

public static IEnumerable<object[]> TestData
{
    get
    {
        yield return [new Address("s", "c", "st", "co", "z"),
                      new Address("s", "c", "st", "co", "z"),
                      "Addresses with same values should be equal"];
    }
}
```

### 9. `.csproj` for Unit Test Project

```xml
<Project Sdk="MSTest.Sdk/3.8.3">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="NSubstitute.Analyzers.CSharp" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\{ServiceName}.API\{ServiceName}.API.csproj" />
  </ItemGroup>

</Project>
```

### 10. `GlobalUsings.cs` for Unit Tests

```csharp
global using NSubstitute;
global using NSubstitute.Core;
// Add project-specific namespaces
global using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
global using eShop.Ordering.Domain.Events;
global using eShop.Ordering.Domain.Exceptions;
```

## Assert Patterns (MSTest)

| Assertion | Usage |
|-----------|-------|
| `Assert.IsNotNull(obj)` | Null checks |
| `Assert.IsTrue(cond)` / `Assert.IsFalse(cond)` | Boolean |
| `Assert.AreEqual(expected, actual)` | Equality |
| `Assert.AreSame(expected, actual)` | Reference equality |
| `Assert.IsInstanceOfType<T>(obj)` | Type checking |
| `Assert.ThrowsException<T>(() => ...)` | Exception assertions |
| `Assert.AreEqual(n, collection.Count)` | Collection count |
| `Assert.AreEqual(0, collection.Count)` | Empty collection |

## Checklist

- [ ] Test class has `[TestClass]` attribute
- [ ] Test methods have `[TestMethod]` attribute
- [ ] Method names describe the scenario (snake_case or PascalCase)
- [ ] Follows AAA pattern (Arrange / Act / Assert)
- [ ] Uses NSubstitute for mocking — never Moq
- [ ] Mocks initialized in constructor, not `[TestInitialize]`
- [ ] Uses MSTest assertions — never FluentAssertions
- [ ] API tests call static handler methods directly
- [ ] Domain tests verify invariants, events, and exceptions
- [ ] Test data uses builders for complex objects, inline for simple values
- [ ] `.csproj` uses `MSTest.Sdk` and references the project under test
- [ ] No test base classes — each test class is self-contained
