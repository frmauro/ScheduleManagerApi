using Scheduling.Domain.Common;

namespace Scheduling.Domain.Providers;

/// <summary>Faixa do expediente semanal (parte do agregado Provider).</summary>
public sealed class WorkingHours : Entity
{
    private WorkingHours()
    {
    }

    public Guid ProviderId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public TimeRange Range => new(StartTime, EndTime);

    internal static WorkingHours Create(Guid providerId, DayOfWeek dayOfWeek, TimeRange range)
    {
        return new WorkingHours
        {
            ProviderId = providerId,
            DayOfWeek = dayOfWeek,
            StartTime = range.Start,
            EndTime = range.End,
        };
    }
}
