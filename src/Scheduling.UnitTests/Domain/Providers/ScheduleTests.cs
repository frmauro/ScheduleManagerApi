using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;
using static Scheduling.UnitTests.TestData;

namespace Scheduling.UnitTests.Domain.Providers;

public sealed class WorkingHoursTests
{
    [Fact]
    public void Replace_RejectsOverlappingIntervalsOnSameDay()
    {
        var provider = Provider();
        var ex = Assert.Throws<DomainException>(() => provider.WithHours(DayOfWeek.Monday, Range(8, 0, 12, 0), Range(11, 0, 14, 0)));
        Assert.Equal(ErrorCodes.ScheduleInvalidWorkingHours, ex.Code);
    }

    [Fact]
    public void Replace_AllowsAdjacentIntervals()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(8, 0, 12, 0), Range(12, 0, 14, 0));
        Assert.Equal(2, provider.WorkingHours.Count);
    }

    [Fact]
    public void Replace_AllowsSameIntervalOnDifferentDays()
    {
        var provider = Provider()
            .WithHours(DayOfWeek.Monday, Range(8, 0, 12, 0))
            .WithHours(DayOfWeek.Tuesday, Range(8, 0, 12, 0));
        Assert.Equal(2, provider.WorkingHours.Count);
    }

    [Fact]
    public void TimeRange_RequiresStartBeforeEnd()
    {
        Assert.Throws<DomainException>(() => Range(12, 0, 12, 0));
        Assert.Throws<DomainException>(() => Range(13, 0, 12, 0));
    }

    [Fact]
    public void Replace_RejectsIntervalShorterThanSlotStep()
    {
        var provider = Provider(policy: Policy(step: 30));
        var ex = Assert.Throws<DomainException>(() => provider.WithHours(DayOfWeek.Monday, Range(9, 0, 9, 20)));
        Assert.Equal(ErrorCodes.ScheduleInvalidWorkingHours, ex.Code);
    }

    [Fact]
    public void Replace_RejectsMoreThanTenIntervalsPerDay()
    {
        var ranges = Enumerable.Range(0, 11).Select(i => Range(8 + i, 0, 8 + i, 30)).ToArray();
        Assert.Throws<DomainException>(() => Provider().WithHours(DayOfWeek.Monday, ranges));
    }

    [Fact]
    public void Replace_AcceptsExactlyTenIntervalsPerDay()
    {
        var ranges = Enumerable.Range(0, 10).Select(i => Range(8 + i, 0, 8 + i, 30)).ToArray();
        Assert.Equal(10, Provider().WithHours(DayOfWeek.Monday, ranges).WorkingHours.Count);
    }

    [Fact]
    public void Replace_SubstitutesWholeSet()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(8, 0, 12, 0));
        provider.ReplaceWorkingHours([(DayOfWeek.Friday, Range(10, 0, 11, 0))]);

        var single = Assert.Single(provider.WorkingHours);
        Assert.Equal(DayOfWeek.Friday, single.DayOfWeek);
    }
}

public sealed class ScheduleExceptionTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    [Fact]
    public void Closure_WithoutTimes_IsFullDay()
    {
        var provider = Provider();
        var exception = provider.AddException(ScheduleExceptionType.Closure, Today, Today, null, null, "Feriado", Today);
        Assert.True(exception.IsFullDay);
    }

    [Fact]
    public void Closure_WithTimes_IsPartial()
    {
        var exception = Provider().AddException(ScheduleExceptionType.Closure, Today, Today, new TimeOnly(10, 0), new TimeOnly(11, 0), null, Today);
        Assert.False(exception.IsFullDay);
    }

    [Fact]
    public void RejectsEndBeforeStart()
    {
        var ex = Assert.Throws<DomainException>(() => Provider().AddException(ScheduleExceptionType.Closure, Today.AddDays(2), Today.AddDays(1), null, null, null, Today));
        Assert.Equal(ErrorCodes.ScheduleInvalidException, ex.Code);
    }

    [Fact]
    public void RejectsPeriodLongerThan366Days() =>
        Assert.Throws<DomainException>(() => Provider().AddException(ScheduleExceptionType.Closure, Today, Today.AddDays(366), null, null, null, Today));

    [Fact]
    public void AcceptsPeriodOfExactly366Days() =>
        Assert.NotNull(Provider().AddException(ScheduleExceptionType.Closure, Today, Today.AddDays(365), null, null, null, Today));

    [Fact]
    public void RejectsOnlyOneTimeInformed() =>
        Assert.Throws<DomainException>(() => Provider().AddException(ScheduleExceptionType.Closure, Today, Today, new TimeOnly(10, 0), null, null, Today));

    [Fact]
    public void SpecialHours_RequiresTimes() =>
        Assert.Throws<DomainException>(() => Provider().AddException(ScheduleExceptionType.SpecialHours, Today, Today, null, null, null, Today));

    [Fact]
    public void RejectsStartDateInPast()
    {
        var ex = Assert.Throws<DomainException>(() => Provider().AddException(ScheduleExceptionType.Closure, Today.AddDays(-1), Today, null, null, null, Today));
        Assert.Equal(ErrorCodes.ScheduleExceptionInPast, ex.Code);
    }

    [Fact]
    public void RejectsOverlappingSpecialHoursOnSameDate()
    {
        var provider = Provider();
        provider.AddException(ScheduleExceptionType.SpecialHours, Today, Today.AddDays(2), new TimeOnly(9, 0), new TimeOnly(13, 0), null, Today);

        var ex = Assert.Throws<DomainException>(() =>
            provider.AddException(ScheduleExceptionType.SpecialHours, Today.AddDays(1), Today.AddDays(1), new TimeOnly(12, 0), new TimeOnly(15, 0), null, Today));
        Assert.Equal(ErrorCodes.ScheduleSpecialHoursOverlap, ex.Code);
    }

    [Fact]
    public void AllowsNonOverlappingSpecialHoursOnSameDate()
    {
        var provider = Provider();
        provider.AddException(ScheduleExceptionType.SpecialHours, Today, Today, new TimeOnly(9, 0), new TimeOnly(12, 0), null, Today);
        provider.AddException(ScheduleExceptionType.SpecialHours, Today, Today, new TimeOnly(13, 0), new TimeOnly(15, 0), null, Today);

        Assert.Equal(2, provider.GetDaySchedule(Today).BaseIntervals.Count);
    }

    [Fact]
    public void RemoveException_UnknownId_ThrowsNotFound()
    {
        var ex = Assert.Throws<DomainException>(() => Provider().RemoveException(Guid.NewGuid()));
        Assert.Equal(ErrorCodes.ScheduleExceptionNotFound, ex.Code);
    }

    [Fact]
    public void DaySchedule_SpecialHoursReplaceWeeklyHours()
    {
        var saturday = new DateOnly(2026, 11, 21);
        var provider = Provider().WithHours(DayOfWeek.Saturday, Range(8, 0, 10, 0));
        provider.AddException(ScheduleExceptionType.SpecialHours, saturday, saturday, new TimeOnly(9, 0), new TimeOnly(13, 0), null, Today);

        var interval = Assert.Single(provider.GetDaySchedule(saturday).OpenIntervals);
        Assert.Equal(Range(9, 0, 13, 0), interval);
    }

    [Fact]
    public void DaySchedule_PartialClosureCutsInterval()
    {
        var provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
        provider.AddException(ScheduleExceptionType.Closure, NextMonday, NextMonday, new TimeOnly(10, 0), new TimeOnly(11, 0), null, Today);

        Assert.Equal([Range(9, 0, 10, 0), Range(11, 0, 12, 0)], provider.GetDaySchedule(NextMonday).OpenIntervals);
    }
}
