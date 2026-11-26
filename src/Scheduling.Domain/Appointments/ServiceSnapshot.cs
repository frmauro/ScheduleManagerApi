namespace Scheduling.Domain.Appointments;

/// <summary>Retrato do serviço no momento da reserva (FR-020).</summary>
public sealed record ServiceSnapshot(string Name, int DurationMinutes, decimal Price);
