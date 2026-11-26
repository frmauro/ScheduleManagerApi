using Scheduling.Domain.Appointments;
using Scheduling.Domain.Availability;
using Scheduling.Domain.Common;
using Scheduling.Domain.Users;
using static Scheduling.UnitTests.TestData;

namespace Scheduling.UnitTests.Domain.Appointments;

public sealed class AppointmentCreateTests
{
    [Fact]
    public void Create_CopiesSnapshotAndComputesTimes()
    {
        var start = SaoPauloTime(NextMonday, 10);
        var appointment = Appointment(start, duration: 45, buffer: 15);

        Assert.Equal("Consulta", appointment.Service.Name);
        Assert.Equal(45, appointment.Service.DurationMinutes);
        Assert.Equal(150m, appointment.Service.Price);
        Assert.Equal(start, appointment.StartAt);
        Assert.Equal(TimeSpan.Zero, appointment.StartAt.Offset);
        Assert.Equal(start.AddMinutes(45), appointment.EndAt);
        Assert.Equal(start.AddMinutes(60), appointment.BlockedUntil);
    }

    [Theory]
    [InlineData(false, AppointmentStatus.Pending)]
    [InlineData(true, AppointmentStatus.Confirmed)]
    public void Create_InitialStatusFollowsAutoConfirm(bool autoConfirm, AppointmentStatus expected) =>
        Assert.Equal(expected, Appointment(SaoPauloTime(NextMonday, 10), autoConfirm: autoConfirm).Status);

    [Fact]
    public void Create_RecordsInitialHistoryEntry()
    {
        var appointment = Appointment(SaoPauloTime(NextMonday, 10));
        var entry = Assert.Single(appointment.History);
        Assert.Null(entry.FromStatus);
        Assert.Equal(AppointmentStatus.Pending, entry.ToStatus);
        Assert.Equal(Role.Customer, entry.ChangedByRole);
    }

    [Fact]
    public void Create_RejectsEndNotAfterStart()
    {
        var start = SaoPauloTime(NextMonday, 10);
        Assert.Throws<DomainException>(() => Scheduling.Domain.Appointments.Appointment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new ServiceSnapshot("X", 30, 1m), new TimeSlot(start, start), 0, false, null, null, CustomerActor(), Now));
    }

    [Fact]
    public void MatchesRequest_ComparesProviderServiceStartAndNotes()
    {
        var appointment = Appointment(SaoPauloTime(NextMonday, 10));
        Assert.True(appointment.MatchesRequest(appointment.ProviderId, appointment.ServiceId, SaoPauloTime(NextMonday, 10).ToUniversalTime(), " Primeira consulta "));
        Assert.False(appointment.MatchesRequest(appointment.ProviderId, appointment.ServiceId, SaoPauloTime(NextMonday, 11), "Primeira consulta"));
        Assert.False(appointment.MatchesRequest(appointment.ProviderId, appointment.ServiceId, SaoPauloTime(NextMonday, 10), "Outra"));
    }

    [Fact]
    public void Create_DoesNotShareSnapshotInstance()
    {
        var snapshot = new ServiceSnapshot("Consulta", 30, 150m);
        var start = SaoPauloTime(NextMonday, 10);
        var slot = new TimeSlot(start, start.AddMinutes(30));

        var a = Scheduling.Domain.Appointments.Appointment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), snapshot, slot, 0, false, null, null, CustomerActor(), Now);

        Assert.Equal(snapshot, a.Service);
        Assert.NotSame(snapshot, a.Service);
    }

    [Fact]
    public void StatusBlocking_OnlyPendingAndConfirmed()
    {
        Assert.True(AppointmentStatus.Pending.IsBlocking());
        Assert.True(AppointmentStatus.Confirmed.IsBlocking());
        Assert.False(AppointmentStatus.CancelledByCustomer.IsBlocking());
        Assert.False(AppointmentStatus.CancelledByProvider.IsBlocking());
        Assert.False(AppointmentStatus.Completed.IsBlocking());
    }
}

/// <summary>Cobre cada célula da máquina de estados de data-model.md.</summary>
public sealed class AppointmentStateMachineTests
{
    private const int CancelNotice = 120;
    private static readonly DateTimeOffset Start = SaoPauloTime(NextMonday, 10);

    [Fact]
    public void ProviderConfirmsPending()
    {
        var a = Appointment(Start);
        a.Confirm(ProviderActor(), Now);
        Assert.Equal(AppointmentStatus.Confirmed, a.Status);
        Assert.Equal(2, a.History.Count);
    }

    [Fact]
    public void AdminConfirmsPending()
    {
        var a = Appointment(Start);
        a.Confirm(AdminActor(), Now);
        Assert.Equal(AppointmentStatus.Confirmed, a.Status);
    }

    [Fact]
    public void CustomerCannotConfirm()
    {
        var ex = Assert.Throws<DomainException>(() => Appointment(Start).Confirm(CustomerActor(), Now));
        Assert.Equal(ErrorCodes.AuthForbidden, ex.Code);
    }

    [Fact]
    public void ConfirmingConfirmed_IsInvalidTransition()
    {
        var a = Appointment(Start, autoConfirm: true);
        Assert.Equal(ErrorCodes.AppointmentInvalidTransition, Assert.Throws<DomainException>(() => a.Confirm(ProviderActor(), Now)).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomerCancelsWithinWindow(bool confirmed)
    {
        var a = Appointment(Start, autoConfirm: confirmed);
        a.Cancel(CustomerActor(), null, "Imprevisto", CancelNotice, Now);

        Assert.Equal(AppointmentStatus.CancelledByCustomer, a.Status);
        Assert.Equal("Imprevisto", a.CancellationReason);
        Assert.Equal("Imprevisto", a.History.Last().Reason);
    }

    [Fact]
    public void CustomerCancelAtExactWindowLimit_IsAllowed()
    {
        var a = Appointment(Start);
        a.Cancel(CustomerActor(), null, "Imprevisto", CancelNotice, Start.AddMinutes(-CancelNotice));
        Assert.Equal(AppointmentStatus.CancelledByCustomer, a.Status);
    }

    [Fact]
    public void CustomerCancelInsideNoticeWindow_IsRejected()
    {
        var a = Appointment(Start);
        var ex = Assert.Throws<DomainException>(() => a.Cancel(CustomerActor(), null, "Imprevisto", CancelNotice, Start.AddMinutes(-CancelNotice + 1)));
        Assert.Equal(ErrorCodes.AppointmentCancellationWindow, ex.Code);
        Assert.Equal(AppointmentStatus.Pending, a.Status);
    }

    [Fact]
    public void ProviderCancelsAnytimeBeforeStart()
    {
        var a = Appointment(Start, autoConfirm: true);
        a.Cancel(ProviderActor(), null, "Agenda indisponível", CancelNotice, Start.AddMinutes(-5));
        Assert.Equal(AppointmentStatus.CancelledByProvider, a.Status);
    }

    [Theory]
    [InlineData(CancellationParty.Customer, AppointmentStatus.CancelledByCustomer)]
    [InlineData(CancellationParty.Provider, AppointmentStatus.CancelledByProvider)]
    public void AdminCancelsOnBehalfOf(CancellationParty party, AppointmentStatus expected)
    {
        var a = Appointment(Start);
        a.Cancel(AdminActor(), party, "Ajuste administrativo", CancelNotice, Start.AddMinutes(-5));
        Assert.Equal(expected, a.Status);
    }

    [Fact]
    public void AdminCancelWithoutOnBehalfOf_IsRejected() =>
        Assert.Throws<DomainException>(() => Appointment(Start).Cancel(AdminActor(), null, "Ajuste", CancelNotice, Now));

    [Fact]
    public void CancelAfterStart_IsRejected()
    {
        var ex = Assert.Throws<DomainException>(() => Appointment(Start).Cancel(ProviderActor(), null, "Atraso", CancelNotice, Start));
        Assert.Equal(ErrorCodes.AppointmentAlreadyStarted, ex.Code);
    }

    [Fact]
    public void CancelRequiresReason() =>
        Assert.Throws<DomainException>(() => Appointment(Start).Cancel(ProviderActor(), null, " ", CancelNotice, Now));

    [Fact]
    public void CompleteConfirmedAfterStart()
    {
        var a = Appointment(Start, autoConfirm: true);
        a.Complete(ProviderActor(), Start.AddMinutes(1));
        Assert.Equal(AppointmentStatus.Completed, a.Status);
    }

    [Fact]
    public void CompleteBeforeStart_IsRejected()
    {
        var a = Appointment(Start, autoConfirm: true);
        Assert.Equal(ErrorCodes.AppointmentNotStarted, Assert.Throws<DomainException>(() => a.Complete(ProviderActor(), Start.AddMinutes(-1))).Code);
    }

    [Fact]
    public void CompletePending_IsInvalidTransition() =>
        Assert.Equal(
            ErrorCodes.AppointmentInvalidTransition,
            Assert.Throws<DomainException>(() => Appointment(Start).Complete(ProviderActor(), Start.AddHours(1))).Code);

    [Fact]
    public void CustomerCannotComplete() =>
        Assert.Equal(
            ErrorCodes.AuthForbidden,
            Assert.Throws<DomainException>(() => Appointment(Start, autoConfirm: true).Complete(CustomerActor(), Start.AddHours(1))).Code);

    public static TheoryData<string> FinalStates => ["CancelledByCustomer", "CancelledByProvider", "Completed"];

    [Theory]
    [MemberData(nameof(FinalStates))]
    public void FinalStates_RejectEveryTransition(string finalState)
    {
        var a = Appointment(Start, autoConfirm: true);
        switch (finalState)
        {
            case "CancelledByCustomer":
                a.Cancel(CustomerActor(), null, "Motivo", CancelNotice, Now);
                break;
            case "CancelledByProvider":
                a.Cancel(ProviderActor(), null, "Motivo", CancelNotice, Now);
                break;
            default:
                a.Complete(ProviderActor(), Start.AddHours(1));
                break;
        }

        var later = finalState == "Completed" ? Start.AddHours(2) : Now;
        Assert.Equal(ErrorCodes.AppointmentInvalidTransition, Assert.Throws<DomainException>(() => a.Confirm(ProviderActor(), later)).Code);
        Assert.Equal(ErrorCodes.AppointmentInvalidTransition, Assert.Throws<DomainException>(() => a.Complete(ProviderActor(), Start.AddHours(2))).Code);
        Assert.Equal(ErrorCodes.AppointmentInvalidTransition, Assert.Throws<DomainException>(() => a.Cancel(ProviderActor(), null, "Motivo", CancelNotice, Now)).Code);
    }
}
