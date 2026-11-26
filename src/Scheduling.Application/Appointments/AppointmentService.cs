using Scheduling.Application.Abstractions;
using Scheduling.Application.Availability;
using Scheduling.Application.Common;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Notifications;
using Scheduling.Application.Providers;
using Scheduling.Application.Services;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Availability;
using Scheduling.Domain.Common;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Users;

namespace Scheduling.Application.Appointments;

public sealed record CreateAppointmentResult(AppointmentDto Appointment, bool IsReplay);

/// <summary>Motor de agendamento: criação sem overbooking e ciclo de vida (FR-015–FR-024).</summary>
public sealed class AppointmentService(
    IAppointmentRepository appointments,
    IProviderRepository providers,
    IServiceRepository services,
    ICustomerRepository customers,
    INotificationRepository notifications,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<CreateAppointmentResult> CreateAsync(CreateAppointmentRequest request, Guid? idempotencyKey, CancellationToken cancellationToken = default)
    {
        var customerId = currentUser.RequireCustomerId();
        var startAt = request.ParseStartAt();

        if (idempotencyKey is { } key && await appointments.GetByIdempotencyKeyAsync(customerId, key, cancellationToken) is { } existing)
        {
            if (!existing.MatchesRequest(request.ProviderId, request.ServiceId, startAt, request.Notes))
            {
                throw new BusinessRuleException(ErrorCodes.IdempotencyKeyReused, "A chave de idempotência já foi usada com outro conteúdo.");
            }

            return new CreateAppointmentResult(await ToDtoAsync(existing, cancellationToken), true);
        }

        var utcDate = DateOnly.FromDateTime(startAt.UtcDateTime);
        var provider = await providers.GetWithScheduleAsync(request.ProviderId, utcDate.AddDays(-1), utcDate.AddDays(1), cancellationToken)
            ?? throw ProviderService.NotFound();
        var service = await services.GetByIdAsync(request.ServiceId, cancellationToken);
        if (service is null || service.ProviderId != provider.Id)
        {
            throw ServiceCatalogService.NotFound();
        }

        var customer = await customers.GetByIdAsync(customerId, cancellationToken) ?? throw new ForbiddenException(ErrorCodes.AuthForbidden, "Cliente não encontrado.");

        // Reservas do mesmo prestador são avaliadas em série: a ocupação lida já inclui a do vencedor anterior.
        // A exclusion constraint do banco permanece como garantia final contra sobreposição (R-01).
        return await unitOfWork.ExecuteSerializedAsync(provider.Id, async ct =>
        {
            var now = timeProvider.GetUtcNow();
            var occupations = await appointments.GetOccupationsAsync(
                provider.Id,
                AvailabilityService.DayStartUtc(utcDate.AddDays(-1)),
                AvailabilityService.DayStartUtc(utcDate.AddDays(2)),
                ct);

            // Validação R1–R11 com motivo específico para cada recusa.
            var slot = AvailabilityCalculator.EnsureBookable(provider, service, startAt, occupations, now);

            var appointment = Appointment.Create(
                provider.Id,
                customer.Id,
                service.Id,
                new ServiceSnapshot(service.Name, service.DurationMinutes, service.Price),
                slot,
                provider.BookingPolicy.BufferMinutes,
                provider.BookingPolicy.AutoConfirm,
                request.Notes,
                idempotencyKey,
                currentUser.ToActor(),
                now);

            appointments.Add(appointment);
            notifications.AddRange(NotificationFactory.AppointmentCreated(appointment, provider, customer, now));
            await unitOfWork.SaveChangesAsync(ct);

            return new CreateAppointmentResult(AppointmentDto.From(appointment, provider, customer), false);
        }, cancellationToken);
    }

    public async Task<AppointmentDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var appointment = await LoadVisibleAsync(id, cancellationToken);
        return await ToDtoAsync(appointment, cancellationToken);
    }

    public async Task<AppointmentDto> ConfirmAsync(Guid id, uint? expectedVersion, CancellationToken cancellationToken = default)
    {
        EnsureRole(Role.Provider, Role.Admin);
        var appointment = await LoadForChangeAsync(id, expectedVersion, cancellationToken);
        var now = timeProvider.GetUtcNow();

        appointment.Confirm(currentUser.ToActor(), now);
        var (provider, customer) = await LoadPartiesAsync(appointment, cancellationToken);
        notifications.AddRange(NotificationFactory.AppointmentConfirmed(appointment, provider, customer, now));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AppointmentDto.From(appointment, provider, customer);
    }

    public async Task<AppointmentDto> CancelAsync(Guid id, CancelAppointmentRequest request, uint? expectedVersion, CancellationToken cancellationToken = default)
    {
        if (currentUser.Role != Role.Admin && request.OnBehalfOf is not null)
        {
            throw new ForbiddenException(ErrorCodes.AuthForbidden, "'onBehalfOf' é permitido somente para Admin.");
        }

        var appointment = await LoadForChangeAsync(id, expectedVersion, cancellationToken);
        var (provider, customer) = await LoadPartiesAsync(appointment, cancellationToken);
        var now = timeProvider.GetUtcNow();

        appointment.Cancel(currentUser.ToActor(), request.OnBehalfOf, request.Reason, provider.BookingPolicy.MinCancellationNoticeMinutes, now);
        notifications.AddRange(NotificationFactory.AppointmentCancelled(appointment, provider, customer, currentUser.Role, now));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AppointmentDto.From(appointment, provider, customer);
    }

    public async Task<AppointmentDto> CompleteAsync(Guid id, uint? expectedVersion, CancellationToken cancellationToken = default)
    {
        EnsureRole(Role.Provider, Role.Admin);
        var appointment = await LoadForChangeAsync(id, expectedVersion, cancellationToken);

        appointment.Complete(currentUser.ToActor(), timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(appointment, cancellationToken);
    }

    public async Task<PagedResult<AppointmentSummaryDto>> GetProviderAgendaAsync(AgendaQuery query, CancellationToken cancellationToken = default)
    {
        var search = new AppointmentSearch(
            currentUser.RequireProviderId(),
            null,
            query.ServiceId,
            query.Status,
            query.From,
            query.To,
            query.Sort == AppointmentListQuery.SortDescending);
        var result = await appointments.SearchAsync(search, query, cancellationToken);
        return result.Map(AppointmentSummaryDto.From);
    }

    private void EnsureRole(params Role[] roles)
    {
        if (!roles.Contains(currentUser.Role))
        {
            throw new ForbiddenException(ErrorCodes.AuthForbidden, "Perfil sem permissão para esta ação.");
        }
    }

    /// <summary>Cliente dono, prestador do agendamento ou Admin; demais → 404 (não revela existência).</summary>
    private async Task<Appointment> LoadVisibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdAsync(id, cancellationToken);
        var visible = appointment is not null && currentUser.Role switch
        {
            Role.Admin => true,
            Role.Customer => appointment.CustomerId == currentUser.CustomerId,
            Role.Provider => appointment.ProviderId == currentUser.ProviderId,
            _ => false,
        };

        return visible ? appointment! : throw new NotFoundException(ErrorCodes.AppointmentNotFound, "Agendamento não encontrado.");
    }

    private async Task<Appointment> LoadForChangeAsync(Guid id, uint? expectedVersion, CancellationToken cancellationToken)
    {
        var appointment = await LoadVisibleAsync(id, cancellationToken);
        if (expectedVersion is { } version && version != appointment.Version)
        {
            throw new PreconditionFailedException(ErrorCodes.AppointmentPreconditionFailed, "O agendamento foi alterado desde a última leitura (If-Match divergente).");
        }

        return appointment;
    }

    private async Task<(Provider Provider, Customer Customer)> LoadPartiesAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        var provider = await providers.GetByIdAsync(appointment.ProviderId, cancellationToken) ?? throw ProviderService.NotFound();
        var customer = await customers.GetByIdAsync(appointment.CustomerId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "Cliente não encontrado.");
        return (provider, customer);
    }

    private async Task<AppointmentDto> ToDtoAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        var (provider, customer) = await LoadPartiesAsync(appointment, cancellationToken);
        return AppointmentDto.From(appointment, provider, customer);
    }
}
