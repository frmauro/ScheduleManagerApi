using Scheduling.Domain.Availability;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;
using static Scheduling.UnitTests.TestData;

namespace Scheduling.UnitTests.Domain.Availability;

/// <summary>Matriz de Regras de Disponibilidade R1–R11 (spec.md).</summary>
public sealed class AvailabilityCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static List<TimeOnly> LocalStarts(IEnumerable<TimeSlot> slots) =>
        slots.Select(s => TimeOnly.FromDateTime(s.StartAt.ToOffset(SaoPauloOffset).DateTime)).ToList();

    private static List<TimeOnly> Times(params string[] values) => values.Select(TimeOnly.Parse).ToList();

    [Fact]
    public void Scenario1_MondayMorning30Min_ReturnsEvery15MinutesUntil1130()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var slots = AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 30), NextMonday, [], Now);

        Assert.Equal(
            Times("09:00", "09:15", "09:30", "09:45", "10:00", "10:15", "10:30", "10:45", "11:00", "11:15", "11:30"),
            LocalStarts(slots));
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(30), s.EndAt - s.StartAt));
    }

    [Fact]
    public void LunchBreak_NoSlotSpansTheGap()
    {
        var provider = Provider().WithHours(DayOfWeek.Tuesday, Range(8, 0, 12, 0), Range(13, 0, 18, 0));
        var starts = LocalStarts(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 60), NextTuesday, [], Now));

        Assert.Contains(new TimeOnly(11, 0), starts);
        Assert.DoesNotContain(starts, t => t > new TimeOnly(11, 0) && t < new TimeOnly(13, 0));
        Assert.Equal(new TimeOnly(13, 0), starts.First(t => t >= new TimeOnly(12, 0)));
        Assert.Equal(new TimeOnly(17, 0), starts[^1]);
    }

    [Fact]
    public void ServiceLongerThanAnyInterval_ReturnsEmpty()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0), Range(13, 0, 16, 0));
        Assert.Empty(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 240), NextMonday, [], Now));
    }

    [Fact]
    public void EndingExactlyAtIntervalEnd_IsValid_OneMinuteMoreIsNot()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var starts = LocalStarts(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 45), NextMonday, [], Now));

        Assert.Equal(new TimeOnly(11, 15), starts[^1]); // 11:15 + 45 = 12:00
        Assert.DoesNotContain(new TimeOnly(11, 30), starts);
    }

    [Fact]
    public void AdjacentAppointments_AreAllowed()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var starts = LocalStarts(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 30), NextMonday, [Busy(NextMonday, 10, 0, 30)], Now));

        Assert.Contains(new TimeOnly(9, 30), starts);  // termina 10:00
        Assert.Contains(new TimeOnly(10, 30), starts); // começa 10:30
        Assert.DoesNotContain(new TimeOnly(9, 45), starts);
        Assert.DoesNotContain(new TimeOnly(10, 0), starts);
        Assert.DoesNotContain(new TimeOnly(10, 15), starts);
    }

    [Fact]
    public void Buffer_BlocksTimeAfterEachAppointment()
    {
        var provider = Provider(policy: Policy(buffer: 15)).WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var occupied = Busy(NextMonday, 10, 0, 30, bufferMinutes: 15); // [10:00, 10:45)
        var starts = LocalStarts(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 30), NextMonday, [occupied], Now));

        Assert.DoesNotContain(new TimeOnly(10, 30), starts);
        Assert.Contains(new TimeOnly(10, 45), starts);
        Assert.DoesNotContain(new TimeOnly(9, 30), starts); // [09:30, 10:15) com preparo invade 10:00
        Assert.Contains(new TimeOnly(9, 15), starts);       // [09:15, 10:00)
    }

    [Fact]
    public void Today_ExcludesPastAndMinimumNotice()
    {
        // Agora = 09:00 local; antecedência 60 min → primeiro início às 10:00.
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(8, 0, 12, 0));
        var starts = LocalStarts(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 30), Today, [], Now));

        Assert.Equal(new TimeOnly(10, 0), starts[0]);
    }

    [Fact]
    public void PastDate_Throws()
    {
        var provider = Provider().WithHours(DayOfWeek.Sunday, Range(9, 0, 12, 0));
        var ex = Assert.Throws<DomainException>(() => AvailabilityCalculator.GetAvailableSlots(provider, Service(provider), Today.AddDays(-1), [], Now));
        Assert.Equal(ErrorCodes.AvailabilityDateInPast, ex.Code);
    }

    [Fact]
    public void BeyondHorizon_Throws_HorizonDayIsAllowed()
    {
        var provider = Provider().WithHours(DayOfWeek.Friday, Range(9, 0, 12, 0)).WithHours(DayOfWeek.Saturday, Range(9, 0, 12, 0));
        var service = Service(provider);

        Assert.NotNull(AvailabilityCalculator.GetAvailableSlots(provider, service, Today.AddDays(60), [], Now));
        var ex = Assert.Throws<DomainException>(() => AvailabilityCalculator.GetAvailableSlots(provider, service, Today.AddDays(61), [], Now));
        Assert.Equal(ErrorCodes.AvailabilityBeyondHorizon, ex.Code);
    }

    [Fact]
    public void FullDayClosure_ReturnsEmpty()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        provider.AddException(ScheduleExceptionType.Closure, NextMonday, NextMonday, null, null, "Feriado", Today);

        Assert.Empty(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider), NextMonday, [], Now));
    }

    [Fact]
    public void PartialClosure_CutsSlots()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        provider.AddException(ScheduleExceptionType.Closure, NextMonday, NextMonday, new TimeOnly(10, 0), new TimeOnly(11, 0), null, Today);

        Assert.Equal(
            Times("09:00", "09:15", "09:30", "11:00", "11:15", "11:30"),
            LocalStarts(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 30), NextMonday, [], Now)));
    }

    [Fact]
    public void SpecialHours_OnClosedWeekday_ProducesSlots()
    {
        var saturday = new DateOnly(2026, 11, 21);
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        provider.AddException(ScheduleExceptionType.SpecialHours, saturday, saturday, new TimeOnly(9, 0), new TimeOnly(13, 0), null, Today);

        var starts = LocalStarts(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 60), saturday, [], Now));
        Assert.Equal(new TimeOnly(9, 0), starts[0]);
        Assert.Equal(new TimeOnly(12, 0), starts[^1]);
    }

    [Fact]
    public void DayWithoutWorkingHours_ReturnsEmpty()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        Assert.Empty(AvailabilityCalculator.GetAvailableSlots(provider, Service(provider), NextTuesday, [], Now));
    }

    [Fact]
    public void DstSpringForward_SkipsNonexistentLocalTimes()
    {
        // Nova York, 14/03/2027: 02:00 → 03:00.
        var sunday = new DateOnly(2027, 3, 14);
        var provider = Provider(NewYork, Policy(step: 30)).WithHours(DayOfWeek.Sunday, Range(1, 0, 4, 0));
        var now = new DateTimeOffset(2027, 3, 1, 12, 0, 0, TimeSpan.Zero);

        var slots = AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 30), sunday, [], now);
        var tz = provider.GetTimeZone();
        var localStarts = slots.Select(s => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.StartAt, tz).DateTime)).ToList();

        Assert.Equal(Times("01:00", "01:30", "03:00", "03:30"), localStarts);
    }

    [Fact]
    public void DstFallBack_AmbiguousTimesAreNotDuplicated()
    {
        // Nova York, 01/11/2026: 02:00 → 01:00 (01:00–01:59 ocorre duas vezes).
        var sunday = new DateOnly(2026, 11, 1);
        var provider = Provider(NewYork, Policy(step: 30)).WithHours(DayOfWeek.Sunday, Range(0, 0, 3, 0));
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        var slots = AvailabilityCalculator.GetAvailableSlots(provider, Service(provider, 30), sunday, [], now);

        Assert.Equal(6, slots.Count);
        Assert.Equal(slots.Count, slots.Select(s => s.StartAt.UtcTicks).Distinct().Count());
        var ambiguous = slots.Single(s => s.StartAt.Hour == 1 && s.StartAt.Minute == 0);
        Assert.Equal(TimeSpan.FromHours(-4), ambiguous.StartAt.Offset); // primeiro deslocamento (EDT)
    }

    [Fact]
    public void Results_AreOrderedAndDistinct()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(13, 0, 15, 0), Range(9, 0, 11, 0));
        var slots = AvailabilityCalculator.GetAvailableSlots(provider, Service(provider), NextMonday, [], Now);

        Assert.Equal(slots.OrderBy(s => s.StartAt), slots);
        Assert.Equal(slots.Count, slots.Distinct().Count());
    }

    [Fact]
    public void InactiveProviderOrService_OrForeignService_AreRejected()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var service = Service(provider);
        var foreign = Service(Provider());

        Assert.Equal(ErrorCodes.ServiceNotFound, Assert.Throws<DomainException>(() => AvailabilityCalculator.GetAvailableSlots(provider, foreign, NextMonday, [], Now)).Code);

        service.Deactivate();
        Assert.Equal(ErrorCodes.ServiceInactive, Assert.Throws<DomainException>(() => AvailabilityCalculator.GetAvailableSlots(provider, service, NextMonday, [], Now)).Code);

        service.Activate();
        provider.Deactivate();
        Assert.Equal(ErrorCodes.ProviderInactive, Assert.Throws<DomainException>(() => AvailabilityCalculator.GetAvailableSlots(provider, service, NextMonday, [], Now)).Code);
    }

    [Fact]
    public void EnsureBookable_ValidStart_ReturnsSlot()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var start = SaoPauloTime(NextMonday, 10);

        var slot = AvailabilityCalculator.EnsureBookable(provider, Service(provider, 30), start, [], Now);

        Assert.Equal(start, slot.StartAt);
        Assert.Equal(start.AddMinutes(30), slot.EndAt);
    }

    [Fact]
    public void EnsureBookable_AcceptsSameInstantInAnotherOffset()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var startUtc = SaoPauloTime(NextMonday, 10).ToUniversalTime();

        Assert.Equal(startUtc, AvailabilityCalculator.EnsureBookable(provider, Service(provider), startUtc, [], Now).StartAt);
    }

    [Theory]
    [InlineData(10, 7, ErrorCodes.AvailabilityMisalignedStart)]
    [InlineData(7, 0, ErrorCodes.AvailabilityOutsideWorkingHours)]
    [InlineData(11, 45, ErrorCodes.AvailabilityOutsideWorkingHours)] // 11:45 + 30 > 12:00
    public void EnsureBookable_RejectsInvalidStarts(int hour, int minute, string expectedCode)
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var ex = Assert.Throws<DomainException>(() =>
            AvailabilityCalculator.EnsureBookable(provider, Service(provider, 30), SaoPauloTime(NextMonday, hour, minute), [], Now));
        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public void EnsureBookable_DayWithoutHours_IsOutsideWorkingHours()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var ex = Assert.Throws<DomainException>(() => AvailabilityCalculator.EnsureBookable(provider, Service(provider), SaoPauloTime(NextTuesday, 10), [], Now));
        Assert.Equal(ErrorCodes.AvailabilityOutsideWorkingHours, ex.Code);
    }

    [Fact]
    public void EnsureBookable_InsideClosure_IsClosed()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        provider.AddException(ScheduleExceptionType.Closure, NextMonday, NextMonday, new TimeOnly(10, 0), new TimeOnly(11, 0), null, Today);

        var ex = Assert.Throws<DomainException>(() => AvailabilityCalculator.EnsureBookable(provider, Service(provider), SaoPauloTime(NextMonday, 10), [], Now));
        Assert.Equal(ErrorCodes.AvailabilityClosed, ex.Code);
    }

    [Fact]
    public void EnsureBookable_FullDayClosure_IsClosed()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        provider.AddException(ScheduleExceptionType.Closure, NextMonday, NextMonday, null, null, "Feriado", Today);

        var ex = Assert.Throws<DomainException>(() => AvailabilityCalculator.EnsureBookable(provider, Service(provider), SaoPauloTime(NextMonday, 10), [], Now));
        Assert.Equal(ErrorCodes.AvailabilityClosed, ex.Code);
    }

    [Fact]
    public void EnsureBookable_TooSoon()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var ex = Assert.Throws<DomainException>(() => AvailabilityCalculator.EnsureBookable(provider, Service(provider), SaoPauloTime(Today, 9, 30), [], Now));
        Assert.Equal(ErrorCodes.AvailabilityTooSoon, ex.Code);
    }

    [Fact]
    public void EnsureBookable_Occupied_IsSlotUnavailable()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        var ex = Assert.Throws<DomainException>(() =>
            AvailabilityCalculator.EnsureBookable(provider, Service(provider, 30), SaoPauloTime(NextMonday, 10, 15), [Busy(NextMonday, 10, 0, 30)], Now));
        Assert.Equal(ErrorCodes.AppointmentSlotUnavailable, ex.Code);
    }

    [Fact]
    public void EnsureBookable_AgreesWithAvailability_ForEveryReturnedSlot()
    {
        // SC-004: todo horário retornado pela consulta é aceito na criação.
        var provider = Provider(policy: Policy(buffer: 10)).WithHours(DayOfWeek.Monday, Range(8, 0, 12, 0), Range(13, 0, 18, 0));
        var service = Service(provider, 40);
        Occupation[] busy = [Busy(NextMonday, 9, 0, 40, 10), Busy(NextMonday, 14, 30, 40, 10)];

        foreach (var slot in AvailabilityCalculator.GetAvailableSlots(provider, service, NextMonday, busy, Now))
        {
            Assert.Equal(slot, AvailabilityCalculator.EnsureBookable(provider, service, slot.StartAt, busy, Now));
        }
    }
}
