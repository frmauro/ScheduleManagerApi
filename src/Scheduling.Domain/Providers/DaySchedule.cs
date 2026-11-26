namespace Scheduling.Domain.Providers;

/// <summary>
/// Agenda efetiva de uma data local: faixas base (R3) e fechamentos (R4).
/// </summary>
public sealed record DaySchedule(DateOnly Date, IReadOnlyList<TimeRange> BaseIntervals, bool IsClosedAllDay, IReadOnlyList<TimeRange> PartialClosures)
{
    /// <summary>Faixas resultantes após aplicar os fechamentos (R4), ordenadas.</summary>
    public IReadOnlyList<TimeRange> OpenIntervals
    {
        get
        {
            if (IsClosedAllDay)
            {
                return [];
            }

            IEnumerable<TimeRange> result = BaseIntervals;
            foreach (var closure in PartialClosures)
            {
                result = result.SelectMany(interval => interval.Subtract(closure)).ToList();
            }

            return result.OrderBy(r => r.Start).ToList();
        }
    }
}
