using Scheduling.Domain.Users;

namespace Scheduling.Domain.Appointments;

/// <summary>Quem executa uma ação sobre o agendamento.</summary>
public readonly record struct Actor(Guid UserId, Role Role);
