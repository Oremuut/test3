using System.Collections.Concurrent;
using Moq;
using NUnit.Framework;

namespace async_tests.Tests;

[TestFixture]
public class PrimesAmountTests
{
    [TestCase(0)]
    [TestCase(-5)]
    public void Constructor_NonPositiveBorder_ThrowsArgumentException(int border)
    {
        Assert.Throws<ArgumentException>(() => new PrimesAmount(border));
    }

    [Test]
    public void CountPrimesAmount_BorderIsOne_ReturnsEmptyAndDoesNotCallService()
    {
        var serviceMock = new Mock<PrimeService>();
        var sut = new PrimesAmount(1);

        var result = sut.CountPrimesAmount(serviceMock.Object);

        Assert.That(result, Is.Empty);
        serviceMock.Verify(s => s.CheckIsPrime(It.IsAny<int>()), Times.Never());
        serviceMock.Verify(s => s.ClearCache(), Times.Never());
    }

    [Test]
    public void CountPrimesAmount_ValidBorder_ChecksNumbersInParallelAndClearsCache()
    {
        var expected = new[] { 2, 3, 5, 7 };
        var calledArgs = new ConcurrentBag<int>();

        var serviceMock = new Mock<PrimeService>();
        serviceMock
            .Setup(s => s.CheckIsPrime(It.IsAny<int>()))
            .Callback<int>(n => calledArgs.Add(n))
            .Returns<int>(n => expected.Contains(n));

        var sut = new PrimesAmount(10);

        ConcurrentQueue<int> result = sut.CountPrimesAmount(serviceMock.Object);

        Assert.That(result, Is.EquivalentTo(expected));
        Assert.That(calledArgs, Is.EquivalentTo(new[] { 2, 3, 4, 5, 6, 7, 8, 9 }));
        serviceMock.Verify(s => s.ClearCache(), Times.Once());
    }

    [Test]
    public void CountPrimesAmount_ServiceThrowsInThread_ClearsQueueAndCallsClearCache()
    {
        var serviceMock = new Mock<PrimeService>();
        serviceMock
            .Setup(s => s.CheckIsPrime(It.IsAny<int>()))
            .Returns<int>(n =>
            {
                if (n == 5)
                    throw new InvalidOperationException("Ошибка в одном из потоков");
                return n == 2 || n == 3;
            });

        var sut = new PrimesAmount(10);

        var result = sut.CountPrimesAmount(serviceMock.Object);

        Assert.That(result, Is.Empty);
        serviceMock.Verify(s => s.ClearCache(), Times.Once());
    }
}
