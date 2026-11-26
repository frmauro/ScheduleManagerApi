using Scheduling.Application.Common.Paging;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Availability;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Notifications;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Application.Abstractions;

/// <summary>Confirma as alterações rastreadas em uma única transação.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executa <paramref name="operation"/> em uma transação serializada por <paramref name="lockKey"/> (lock exclusivo
    /// até o commit). Usado na criação de agendamentos para que reservas concorrentes do mesmo prestador sejam avaliadas
    /// em sequência — exatamente uma vence (research R-01).
    /// </summary>
    Task<T> ExecuteSerializedAsync<T>(Guid lockKey, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeUserId, CancellationToken cancellationToken = default);

    Task<PagedResult<User>> SearchAsync(UserSearch search, PageQuery page, CancellationToken cancellationToken = default);

    void Add(User user);
}

public sealed record UserSearch(Role? Role, bool? IsActive, string? Text);

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Customer?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    void Add(Customer customer);
}

public interface IProviderRepository
{
    Task<Provider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Provider?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Carrega o prestador com expediente semanal e as exceções que tocam o período [from, to].</summary>
    Task<Provider?> GetWithScheduleAsync(Guid id, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<PagedResult<Provider>> SearchActiveAsync(ProviderSearch search, PageQuery page, CancellationToken cancellationToken = default);

    Task<PagedResult<ScheduleException>> ListExceptionsAsync(Guid providerId, DateOnly? from, DateOnly? to, PageQuery page, CancellationToken cancellationToken = default);

    void Add(Provider provider);
}

public sealed record ProviderSearch(ServiceCategory? Category, string? City, string? Text, bool Descending);

public interface IServiceRepository
{
    Task<Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<Service>> ListByProviderAsync(Guid providerId, bool? isActive, PageQuery page, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(Guid providerId, string name, Guid? excludeServiceId, CancellationToken cancellationToken = default);

    void Add(Service service);

    void Remove(Service service);
}

public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Appointment?> GetByIdempotencyKeyAsync(Guid customerId, Guid idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Ocupações (status bloqueantes) do prestador que tocam o intervalo UTC informado.</summary>
    Task<IReadOnlyList<Occupation>> GetOccupationsAsync(Guid providerId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    Task<bool> ExistsForServiceAsync(Guid serviceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Appointment>> GetActiveFromAsync(Guid providerId, DateTimeOffset fromUtc, CancellationToken cancellationToken = default);

    Task<PagedResult<AppointmentListItem>> SearchAsync(AppointmentSearch search, PageQuery page, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppointmentListItem>> GetListItemsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    void Add(Appointment appointment);
}

public sealed record AppointmentSearch(
    Guid? ProviderId,
    Guid? CustomerId,
    Guid? ServiceId,
    IReadOnlyCollection<AppointmentStatus> Statuses,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool Descending);

/// <summary>Projeção de leitura para listagens de agendamentos.</summary>
public sealed record AppointmentListItem(
    Guid Id,
    Guid ProviderId,
    string ProviderName,
    string ProviderTimeZoneId,
    Guid CustomerId,
    string CustomerName,
    string ServiceName,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    AppointmentStatus Status);

public interface INotificationRepository
{
    void AddRange(IEnumerable<Notification> notifications);

    Task<PagedResult<Notification>> SearchAsync(NotificationSearch search, PageQuery page, CancellationToken cancellationToken = default);
}

public sealed record NotificationSearch(Guid? RecipientUserId, NotificationStatus? Status, NotificationType? Type, Guid? AppointmentId);
