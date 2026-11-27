using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Shop.Orders.Tests;

/// <summary>
/// Юнит-тесты для блока 5 (Reserve Inventory) метода PlaceOrderAsync (Задание 2).
/// </summary>
[TestFixture]
public class OrderProcessingServiceReserveInventoryTests
{
    private Mock<IOrderRepository> _orders = null!;
    private Mock<IInventoryService> _inventory = null!;
    private Mock<IPaymentGateway> _payments = null!;
    private Mock<IDiscountEngine> _discounts = null!;
    private Mock<ITaxCalculator> _tax = null!;
    private Mock<INotificationService> _notifications = null!;
    private Mock<IUnitOfWork> _uow = null!;
    private Mock<IClock> _clock = null!;
    private Mock<IDelayer> _delayer = null!;
    private Mock<ILogger<OrderProcessingService>> _logger = null!;
    private Customer _customer = null!;

    [SetUp]
    public void SetUp()
    {
        _orders = new Mock<IOrderRepository>();
        _inventory = new Mock<IInventoryService>();
        _payments = new Mock<IPaymentGateway>();
        _discounts = new Mock<IDiscountEngine>();
        _tax = new Mock<ITaxCalculator>();
        _notifications = new Mock<INotificationService>();
        _uow = new Mock<IUnitOfWork>();
        _clock = new Mock<IClock>();
        _delayer = new Mock<IDelayer>();
        _logger = new Mock<ILogger<OrderProcessingService>>();

        _customer = new Customer(Guid.NewGuid(), "user@example.com", CustomerTier.Standard, "RU");

        _orders.Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Order?)null);
        _orders.Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        _inventory.Setup(i => i.TryReserveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);
        _inventory.Setup(i => i.ReleaseAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        _payments.Setup(p => p.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new PaymentResult(true, false, "tx-1", null));

        _discounts.Setup(d => d.GetApplicableAsync(
                It.IsAny<Customer>(), It.IsAny<IReadOnlyList<OrderLine>>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Discount>());

        _tax.Setup(t => t.Calculate(It.IsAny<string>(), It.IsAny<decimal>())).Returns(0m);
        _uow.Setup(u => u.ExecuteAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task<bool>>, CancellationToken>((work, _) => work());
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
    }

    private OrderProcessingService CreateService() => new(
        _orders.Object, _inventory.Object, _payments.Object, _discounts.Object, _tax.Object,
        _notifications.Object, _uow.Object, _clock.Object, _delayer.Object,
        new OrderOptions(), _logger.Object);

    private PlaceOrderRequest CreateRequest(params OrderLine[] lines) =>
        new("key-1", _customer, lines, "tok");

    [Test]
    public async Task ReserveInventory_AllItemsAvailable_ReservesAllWithoutRelease()
    {
        var request = CreateRequest(
            new OrderLine("SKU-1", 2, 100m),
            new OrderLine("SKU-2", 1, 50m));

        var result = await CreateService().PlaceOrderAsync(request);

        Assert.That(result.IsSuccess, Is.True);
        _inventory.Verify(i => i.TryReserveAsync("SKU-1", 2, It.IsAny<CancellationToken>()), Times.Once());
        _inventory.Verify(i => i.TryReserveAsync("SKU-2", 1, It.IsAny<CancellationToken>()), Times.Once());
        _inventory.Verify(i => i.ReleaseAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Test]
    public async Task ReserveInventory_FirstItemOutOfStock_FailsWithoutReleasingOrCharging()
    {
        _inventory.Setup(i => i.TryReserveAsync("SKU-1", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var request = CreateRequest(
            new OrderLine("SKU-1", 1, 100m),
            new OrderLine("SKU-2", 1, 50m));

        var result = await CreateService().PlaceOrderAsync(request);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Failure, Is.EqualTo(FailureReason.OutOfStock));
        _inventory.Verify(i => i.TryReserveAsync("SKU-2", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
        _inventory.Verify(i => i.ReleaseAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
        _payments.Verify(p => p.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Test]
    public async Task ReserveInventory_SecondItemOutOfStock_ReleasesFirstAndStops()
    {
        _inventory.Setup(i => i.TryReserveAsync("B", 2, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var request = CreateRequest(
            new OrderLine("A", 1, 10m),
            new OrderLine("B", 2, 10m),
            new OrderLine("C", 3, 10m));

        var result = await CreateService().PlaceOrderAsync(request);

        Assert.That(result.Failure, Is.EqualTo(FailureReason.OutOfStock));
        Assert.That(result.Message, Does.Contain("B"));
        _inventory.Verify(i => i.ReleaseAsync("A", 1, CancellationToken.None), Times.Once());
        _inventory.Verify(i => i.ReleaseAsync("B", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
        _inventory.Verify(i => i.TryReserveAsync("C", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never());
        _payments.Verify(p => p.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Test]
    public async Task ReserveInventory_ReleaseThrowsError_StillReleasesNextItem()
    {
        _inventory.Setup(i => i.TryReserveAsync("C", 1, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _inventory.Setup(i => i.ReleaseAsync("A", 1, CancellationToken.None))
                  .ThrowsAsync(new InvalidOperationException("Ошибка склада"));

        var request = CreateRequest(
            new OrderLine("A", 1, 10m),
            new OrderLine("B", 1, 10m),
            new OrderLine("C", 1, 10m));

        var result = await CreateService().PlaceOrderAsync(request);

        Assert.That(result.Failure, Is.EqualTo(FailureReason.OutOfStock));
        _inventory.Verify(i => i.ReleaseAsync("B", 1, CancellationToken.None), Times.Once());
    }
}
