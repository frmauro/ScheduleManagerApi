using Scheduling.Domain.Common;
using Scheduling.Domain.Services;

namespace Scheduling.UnitTests.Domain.Services;

public sealed class ServiceTests
{
    private static readonly Guid ProviderId = Guid.NewGuid();

    [Theory]
    [InlineData(5)]
    [InlineData(45)]
    [InlineData(720)]
    public void AcceptsValidDurations(int duration) =>
        Assert.Equal(duration, Service.Create(ProviderId, "Corte", null, duration, 60m).DurationMinutes);

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(725)]
    public void RejectsInvalidDurations(int duration) =>
        Assert.Throws<DomainException>(() => Service.Create(ProviderId, "Corte", null, duration, 60m));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100000)]
    [InlineData(10.555)]
    public void RejectsInvalidPrices(decimal price) =>
        Assert.Throws<DomainException>(() => Service.Create(ProviderId, "Corte", null, 30, price));

    [Theory]
    [InlineData(0)]
    [InlineData(99999.99)]
    public void AcceptsPriceBounds(decimal price) =>
        Assert.Equal(price, Service.Create(ProviderId, "Corte", null, 30, price).Price);

    [Fact]
    public void RejectsNameLongerThan100() =>
        Assert.Throws<DomainException>(() => Service.Create(ProviderId, new string('x', 101), null, 30, 10m));

    [Fact]
    public void ActivateAndDeactivate()
    {
        var service = Service.Create(ProviderId, "Corte", "Desc", 45, 60m);
        Assert.True(service.IsActive);
        service.Deactivate();
        Assert.False(service.IsActive);
        service.Activate();
        Assert.True(service.IsActive);
    }
}
