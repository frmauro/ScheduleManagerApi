using Scheduling.Application.Notifications;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Notifications;
using Scheduling.Domain.Users;
using static Scheduling.UnitTests.TestData;

namespace Scheduling.UnitTests.Application;

public sealed class NotificationFactoryTests
{
    private readonly Scheduling.Domain.Providers.Provider _provider = Provider();
    private readonly Customer _customer = Customer.Create(Guid.NewGuid(), "Maria", "maria@ex.com", "+5511999998888");

    [Fact]
    public void Created_NotifiesCustomerAndProvider()
    {
        var a = Appointment(SaoPauloTime(NextMonday, 10));
        var list = NotificationFactory.AppointmentCreated(a, _provider, _customer, Now);

        Assert.Equal(2, list.Count);
        Assert.All(list, n => Assert.Equal(NotificationType.AppointmentCreated, n.Type));
        Assert.Contains(list, n => n.RecipientUserId == _customer.UserId);
        Assert.Contains(list, n => n.RecipientUserId == _provider.UserId);
        Assert.Contains("16/11/2026 às 10:00", list[0].Body);
        Assert.Contains("UTC-03:00", list[0].Body);
    }

    [Fact]
    public void Created_WithAutoConfirm_AlsoSendsConfirmation()
    {
        var a = Appointment(SaoPauloTime(NextMonday, 10), autoConfirm: true);
        var list = NotificationFactory.AppointmentCreated(a, _provider, _customer, Now);

        Assert.Equal(3, list.Count);
        Assert.Single(list, n => n.Type == NotificationType.AppointmentConfirmed && n.RecipientUserId == _customer.UserId);
    }

    [Fact]
    public void Confirmed_NotifiesCustomer()
    {
        var n = Assert.Single(NotificationFactory.AppointmentConfirmed(Appointment(SaoPauloTime(NextMonday, 10)), _provider, _customer, Now));
        Assert.Equal(_customer.UserId, n.RecipientUserId);
        Assert.Equal(NotificationType.AppointmentConfirmed, n.Type);
    }

    [Fact]
    public void CancelledByCustomer_NotifiesProvider()
    {
        var a = Appointment(SaoPauloTime(NextMonday, 10));
        a.Cancel(CustomerActor(), null, "Imprevisto", 120, Now);

        var n = Assert.Single(NotificationFactory.AppointmentCancelled(a, _provider, _customer, Role.Customer, Now));
        Assert.Equal(_provider.UserId, n.RecipientUserId);
        Assert.Contains("Imprevisto", n.Body);
    }

    [Fact]
    public void CancelledByProvider_NotifiesCustomer()
    {
        var a = Appointment(SaoPauloTime(NextMonday, 10));
        a.Cancel(ProviderActor(), null, "Agenda", 120, Now);

        Assert.Equal(_customer.UserId, Assert.Single(NotificationFactory.AppointmentCancelled(a, _provider, _customer, Role.Provider, Now)).RecipientUserId);
    }

    [Fact]
    public void CancelledByAdminOnBehalfOfCustomer_NotifiesProviderAndCustomer()
    {
        var a = Appointment(SaoPauloTime(NextMonday, 10));
        a.Cancel(AdminActor(), CancellationParty.Customer, "Ajuste", 120, Now);

        var recipients = NotificationFactory.AppointmentCancelled(a, _provider, _customer, Role.Admin, Now).Select(n => n.RecipientUserId).ToList();
        Assert.Equal(2, recipients.Count);
        Assert.Contains(_provider.UserId, recipients);
        Assert.Contains(_customer.UserId, recipients);
    }

    [Fact]
    public void Reminder_NotifiesCustomerInPortuguese()
    {
        var n = NotificationFactory.AppointmentReminder(Appointment(SaoPauloTime(NextMonday, 10)), _provider, _customer, Now);
        Assert.Equal(NotificationType.AppointmentReminder, n.Type);
        Assert.Equal(_customer.UserId, n.RecipientUserId);
        Assert.StartsWith("Lembrete", n.Subject);
    }
}
