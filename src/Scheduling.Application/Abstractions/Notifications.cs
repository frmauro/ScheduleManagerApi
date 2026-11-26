using Scheduling.Domain.Notifications;

namespace Scheduling.Application.Abstractions;

/// <summary>Canal de entrega substituível (FR-027). Nesta etapa a implementação é simulada.</summary>
public interface INotificationChannel
{
    Task SendAsync(Notification notification, CancellationToken cancellationToken = default);
}

/// <summary>Gera lembretes de 24h para agendamentos confirmados (idempotente).</summary>
public interface IReminderProcessor
{
    /// <returns>Quantidade de lembretes criados nesta execução.</returns>
    Task<int> RunOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>Envia notificações pendentes com retentativas.</summary>
public interface INotificationDispatcher
{
    /// <returns>Quantidade de notificações processadas nesta execução.</returns>
    Task<int> RunOnceAsync(CancellationToken cancellationToken = default);
}

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; set; } = true;

    public int ReminderIntervalSeconds { get; set; } = 60;

    public int DispatchIntervalSeconds { get; set; } = 15;

    public int MaxAttempts { get; set; } = 3;

    public int BatchSize { get; set; } = 50;

    /// <summary>Somente para testes: faz o canal simulado falhar.</summary>
    public bool SimulateFailure { get; set; }
}
