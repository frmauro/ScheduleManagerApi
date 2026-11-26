using Scheduling.Domain.Common;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Users;

namespace Scheduling.UnitTests.Domain;

public sealed class FoundationDomainTests
{
    [Fact]
    public void User_Create_NormalizesEmailAndStartsActive()
    {
        var user = User.Create("  Maria Silva ", "Maria@Example.COM ", Role.Customer);

        Assert.Equal("Maria Silva", user.Name);
        Assert.Equal("maria@example.com", user.Email);
        Assert.True(user.IsActive);
        Assert.Equal(Role.Customer, user.Role);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("")]
    public void User_Create_RejectsInvalidName(string name)
    {
        var ex = Assert.Throws<DomainException>(() => User.Create(name, "a@b.com", Role.Customer));
        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
    }

    [Fact]
    public void User_Create_RejectsNameLongerThan120() =>
        Assert.Throws<DomainException>(() => User.Create(new string('a', 121), "a@b.com", Role.Customer));

    [Theory]
    [InlineData("invalid")]
    [InlineData("a@b")]
    public void User_Create_RejectsInvalidEmail(string email) =>
        Assert.Throws<DomainException>(() => User.Create("Maria", email, Role.Customer));

    [Fact]
    public void User_Deactivate_And_Activate()
    {
        var user = User.Create("Maria", "a@b.com", Role.Provider);
        user.Deactivate();
        Assert.False(user.IsActive);
        user.Activate();
        Assert.True(user.IsActive);
    }

    [Theory]
    [InlineData("11999998888")]
    [InlineData("+0119999")]
    [InlineData("+55 11 99999-8888")]
    public void Customer_RejectsNonE164Phone(string phone) =>
        Assert.Throws<DomainException>(() => Customer.Create(Guid.NewGuid(), "Maria", "a@b.com", phone));

    [Fact]
    public void Customer_AcceptsE164Phone()
    {
        var customer = Customer.Create(Guid.NewGuid(), "Maria", "A@B.com", "+5511999998888");
        Assert.Equal("+5511999998888", customer.Phone);
        Assert.Equal("a@b.com", customer.Email);
    }

    [Theory]
    [InlineData("S", "01001000")]
    [InlineData("SP", "0100100")]
    [InlineData("SP", "01001-000")]
    public void Address_RejectsInvalidStateOrPostalCode(string state, string postalCode) =>
        Assert.Throws<DomainException>(() => Address.Create("Rua", "1", null, "Centro", "SP", state, postalCode));

    [Fact]
    public void Address_NormalizesStateToUpperCase() =>
        Assert.Equal("SP", Address.Create("Rua", "1", null, "Centro", "São Paulo", "sp", "01001000").State);

    [Fact]
    public void BookingPolicy_Default_MatchesSpecAssumptions()
    {
        var policy = BookingPolicy.Default;
        Assert.False(policy.AutoConfirm);
        Assert.Equal(15, policy.SlotStepMinutes);
        Assert.Equal(0, policy.BufferMinutes);
        Assert.Equal(60, policy.MinBookingNoticeMinutes);
        Assert.Equal(60, policy.BookingHorizonDays);
        Assert.Equal(120, policy.MinCancellationNoticeMinutes);
    }

    [Theory]
    [InlineData(5, 0, 0, 1, 0, true)]
    [InlineData(60, 120, 10080, 365, 10080, true)]
    [InlineData(0, 0, 60, 60, 120, false)]   // passo < 5
    [InlineData(65, 0, 60, 60, 120, false)]  // passo > 60
    [InlineData(7, 0, 60, 60, 120, false)]   // passo não múltiplo de 5
    [InlineData(15, 125, 60, 60, 120, false)] // preparo > 120
    [InlineData(15, 7, 60, 60, 120, false)]  // preparo não múltiplo de 5
    [InlineData(15, 0, -1, 60, 120, false)]  // antecedência negativa
    [InlineData(15, 0, 10081, 60, 120, false)]
    [InlineData(15, 0, 60, 0, 120, false)]   // horizonte < 1
    [InlineData(15, 0, 60, 366, 120, false)] // horizonte > 365
    [InlineData(15, 0, 60, 60, 10081, false)]
    public void BookingPolicy_EnforcesRanges(int step, int buffer, int notice, int horizon, int cancelNotice, bool valid)
    {
        var act = () => BookingPolicy.Create(false, step, buffer, notice, horizon, cancelNotice);
        if (valid)
        {
            Assert.NotNull(act());
        }
        else
        {
            Assert.Throws<DomainException>(act);
        }
    }

    [Fact]
    public void Provider_RejectsInvalidTimeZone()
    {
        var provider = TestData.Provider();
        var ex = Assert.Throws<DomainException>(() =>
            provider.UpdateProfile("Clínica", null, ServiceCategory.Health, "a@b.com", "+5511999998888", TestData.Address(), "Mars/Olympus"));
        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
    }

    [Fact]
    public void Provider_Create_DefaultsAndActive()
    {
        var provider = TestData.Provider();
        Assert.True(provider.IsActive);
        Assert.Equal(TestData.SaoPaulo, provider.TimeZoneId);
        Assert.Equal(BookingPolicy.Default, provider.BookingPolicy);
    }

    [Fact]
    public void Provider_GetLocalToday_UsesProviderTimeZone()
    {
        var provider = TestData.Provider();

        // 02:00Z de 10/11 ainda é 09/11 em São Paulo (23:00 do dia anterior).
        Assert.Equal(new DateOnly(2026, 11, 9), provider.GetLocalToday(new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero)));
    }
}
