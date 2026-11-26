using Scheduling.Domain.Common;

namespace Scheduling.Domain.Providers;

/// <summary>Faixa de hora local de parede, semiaberta [Start, End), dentro de um único dia.</summary>
public readonly record struct TimeRange
{
    public TimeRange(TimeOnly start, TimeOnly end)
    {
        if (start >= end)
        {
            throw Guard.Invalid($"O início ({start:HH\\:mm}) deve ser anterior ao fim ({end:HH\\:mm}).");
        }

        Start = start;
        End = end;
    }

    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    public TimeSpan Duration => End - Start;

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    public bool Contains(TimeOnly start, TimeSpan duration)
    {
        if (start < Start)
        {
            return false;
        }

        var endMinutes = start.ToTimeSpan() + duration;
        return endMinutes <= End.ToTimeSpan();
    }

    /// <summary>Remove <paramref name="cut"/> desta faixa, devolvendo 0, 1 ou 2 faixas restantes.</summary>
    public IEnumerable<TimeRange> Subtract(TimeRange cut)
    {
        if (!Overlaps(cut))
        {
            yield return this;
            yield break;
        }

        if (cut.Start > Start)
        {
            yield return new TimeRange(Start, cut.Start);
        }

        if (cut.End < End)
        {
            yield return new TimeRange(cut.End, End);
        }
    }
}
