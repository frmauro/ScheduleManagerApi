using Microsoft.EntityFrameworkCore;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common.Paging;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Availability;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Notifications;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Persistence.Repositories;

internal static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery page, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }

    /// <summary>Npgsql exige offset zero para parâmetros <c>timestamptz</c>.</summary>
    public static DateTimeOffset? ToUtc(this DateTimeOffset? value) => value?.ToUniversalTime();

    public static string LikePattern(string text) =>
        "%" + text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}

internal sealed class UserRepository(SchedulingDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, Guid? excludeUserId, CancellationToken cancellationToken = default) =>
        db.Users.AnyAsync(u => u.Email == normalizedEmail && (excludeUserId == null || u.Id != excludeUserId), cancellationToken);

    public Task<PagedResult<User>> SearchAsync(UserSearch search, PageQuery page, CancellationToken cancellationToken = default)
    {
        var query = db.Users.AsNoTracking();
        if (search.Role is { } role)
        {
            query = query.Where(u => u.Role == role);
        }

        if (search.IsActive is { } active)
        {
            query = query.Where(u => u.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            var pattern = PagingExtensions.LikePattern(search.Text);
            query = query.Where(u => EF.Functions.ILike(u.Name, pattern) || EF.Functions.ILike(u.Email, pattern));
        }

        return query.OrderBy(u => u.Name).ThenBy(u => u.Id).ToPagedAsync(page, cancellationToken);
    }

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class CustomerRepository(SchedulingDbContext db) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Customer?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public void Add(Customer customer) => db.Customers.Add(customer);
}

internal sealed class ProviderRepository(SchedulingDbContext db) : IProviderRepository
{
    public Task<Provider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Providers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Provider?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Providers.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public Task<Provider?> GetWithScheduleAsync(Guid id, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        db.Providers
            .Include(p => p.WorkingHours)
            .Include(p => p.Exceptions.Where(e => e.StartDate <= to && e.EndDate >= from))
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PagedResult<Provider>> SearchActiveAsync(ProviderSearch search, PageQuery page, CancellationToken cancellationToken = default)
    {
        var query = db.Providers.AsNoTracking().Where(p => p.IsActive);
        if (search.Category is { } category)
        {
            query = query.Where(p => p.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(search.City))
        {
            var city = search.City.Trim();
            query = query.Where(p => EF.Functions.ILike(p.Address.City, city));
        }

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            var pattern = PagingExtensions.LikePattern(search.Text);
            query = query.Where(p => EF.Functions.ILike(p.DisplayName, pattern));
        }

        query = search.Descending
            ? query.OrderByDescending(p => p.DisplayName).ThenBy(p => p.Id)
            : query.OrderBy(p => p.DisplayName).ThenBy(p => p.Id);

        return query.ToPagedAsync(page, cancellationToken);
    }

    public Task<PagedResult<ScheduleException>> ListExceptionsAsync(Guid providerId, DateOnly? from, DateOnly? to, PageQuery page, CancellationToken cancellationToken = default)
    {
        var query = db.ScheduleExceptions.AsNoTracking().Where(e => e.ProviderId == providerId);
        if (from is { } f)
        {
            query = query.Where(e => e.EndDate >= f);
        }

        if (to is { } t)
        {
            query = query.Where(e => e.StartDate <= t);
        }

        return query.OrderBy(e => e.StartDate).ThenBy(e => e.Id).ToPagedAsync(page, cancellationToken);
    }

    public void Add(Provider provider) => db.Providers.Add(provider);
}

internal sealed class ServiceRepository(SchedulingDbContext db) : IServiceRepository
{
    public Task<Service?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<PagedResult<Service>> ListByProviderAsync(Guid providerId, bool? isActive, PageQuery page, CancellationToken cancellationToken = default)
    {
        var query = db.Services.AsNoTracking().Where(s => s.ProviderId == providerId);
        if (isActive is { } active)
        {
            query = query.Where(s => s.IsActive == active);
        }

        return query.OrderBy(s => s.Name).ThenBy(s => s.Id).ToPagedAsync(page, cancellationToken);
    }

    public Task<bool> NameExistsAsync(Guid providerId, string name, Guid? excludeServiceId, CancellationToken cancellationToken = default)
    {
        var lowered = name.ToLower();
        return db.Services.AnyAsync(
            s => s.ProviderId == providerId && s.Name.ToLower() == lowered && (excludeServiceId == null || s.Id != excludeServiceId),
            cancellationToken);
    }

    public void Add(Service service) => db.Services.Add(service);

    public void Remove(Service service) => db.Services.Remove(service);
}

internal sealed class AppointmentRepository(SchedulingDbContext db) : IAppointmentRepository
{
    private static readonly AppointmentStatus[] Blocking = [AppointmentStatus.Pending, AppointmentStatus.Confirmed];

    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Appointments.Include(a => a.History).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Appointment?> GetByIdempotencyKeyAsync(Guid customerId, Guid idempotencyKey, CancellationToken cancellationToken = default) =>
        db.Appointments.Include(a => a.History).FirstOrDefaultAsync(a => a.CustomerId == customerId && a.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<Occupation>> GetOccupationsAsync(Guid providerId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        var to = toUtc.ToUniversalTime();
        var rows = await db.Appointments.AsNoTracking()
            .Where(a => a.ProviderId == providerId && Blocking.Contains(a.Status) && a.StartAt < to && a.BlockedUntil > from)
            .Select(a => new { a.StartAt, a.BlockedUntil })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new Occupation(r.StartAt, r.BlockedUntil)).ToList();
    }

    public Task<bool> ExistsForServiceAsync(Guid serviceId, CancellationToken cancellationToken = default) =>
        db.Appointments.AnyAsync(a => a.ServiceId == serviceId, cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetActiveFromAsync(Guid providerId, DateTimeOffset fromUtc, CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime();
        return await db.Appointments.AsNoTracking()
            .Where(a => a.ProviderId == providerId && Blocking.Contains(a.Status) && a.StartAt >= from)
            .OrderBy(a => a.StartAt)
            .ToListAsync(cancellationToken);
    }

    public Task<PagedResult<AppointmentListItem>> SearchAsync(AppointmentSearch search, PageQuery page, CancellationToken cancellationToken = default)
    {
        var query = db.Appointments.AsNoTracking().AsQueryable();
        if (search.ProviderId is { } providerId)
        {
            query = query.Where(a => a.ProviderId == providerId);
        }

        if (search.CustomerId is { } customerId)
        {
            query = query.Where(a => a.CustomerId == customerId);
        }

        if (search.ServiceId is { } serviceId)
        {
            query = query.Where(a => a.ServiceId == serviceId);
        }

        if (search.Statuses.Count > 0)
        {
            var statuses = search.Statuses.ToArray();
            query = query.Where(a => statuses.Contains(a.Status));
        }

        if (search.From.ToUtc() is { } from)
        {
            query = query.Where(a => a.StartAt >= from);
        }

        if (search.To.ToUtc() is { } to)
        {
            query = query.Where(a => a.StartAt < to);
        }

        query = search.Descending
            ? query.OrderByDescending(a => a.StartAt).ThenBy(a => a.Id)
            : query.OrderBy(a => a.StartAt).ThenBy(a => a.Id);

        return Project(query).ToPagedAsync(page, cancellationToken);
    }

    public async Task<IReadOnlyList<AppointmentListItem>> GetListItemsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await Project(db.Appointments.AsNoTracking().Where(a => ids.Contains(a.Id))).ToListAsync(cancellationToken);

    public void Add(Appointment appointment) => db.Appointments.Add(appointment);

    private IQueryable<AppointmentListItem> Project(IQueryable<Appointment> query) =>
        from a in query
        join p in db.Providers on a.ProviderId equals p.Id
        join c in db.Customers on a.CustomerId equals c.Id
        select new AppointmentListItem(a.Id, a.ProviderId, p.DisplayName, p.TimeZoneId, a.CustomerId, c.Name, a.Service.Name, a.StartAt, a.EndAt, a.Status);
}

internal sealed class NotificationRepository(SchedulingDbContext db) : INotificationRepository
{
    public void AddRange(IEnumerable<Notification> notifications) => db.Notifications.AddRange(notifications);

    public Task<PagedResult<Notification>> SearchAsync(NotificationSearch search, PageQuery page, CancellationToken cancellationToken = default)
    {
        var query = db.Notifications.AsNoTracking().AsQueryable();
        if (search.RecipientUserId is { } recipient)
        {
            query = query.Where(n => n.RecipientUserId == recipient);
        }

        if (search.Status is { } status)
        {
            query = query.Where(n => n.Status == status);
        }

        if (search.Type is { } type)
        {
            query = query.Where(n => n.Type == type);
        }

        if (search.AppointmentId is { } appointmentId)
        {
            query = query.Where(n => n.AppointmentId == appointmentId);
        }

        return query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).ToPagedAsync(page, cancellationToken);
    }
}
