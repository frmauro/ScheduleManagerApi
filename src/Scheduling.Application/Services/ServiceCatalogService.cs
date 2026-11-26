using Scheduling.Application.Abstractions;
using Scheduling.Application.Common;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Providers;
using Scheduling.Domain.Common;
using Scheduling.Domain.Services;

namespace Scheduling.Application.Services;

/// <summary>Gestão de serviços do prestador (FR-002).</summary>
public sealed class ServiceCatalogService(
    IServiceRepository services,
    IProviderRepository providers,
    IAppointmentRepository appointments,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<PagedResult<ServiceDto>> ListPublicAsync(Guid providerId, PageQuery page, CancellationToken cancellationToken = default)
    {
        var provider = await providers.GetByIdAsync(providerId, cancellationToken);
        if (provider is null || !provider.IsActive)
        {
            throw ProviderService.NotFound();
        }

        var result = await services.ListByProviderAsync(providerId, true, page, cancellationToken);
        return result.Map(ServiceDto.From);
    }

    public async Task<PagedResult<ServiceDto>> ListOwnAsync(ServiceListQuery query, CancellationToken cancellationToken = default)
    {
        var result = await services.ListByProviderAsync(currentUser.RequireProviderId(), query.IsActive, query, cancellationToken);
        return result.Map(ServiceDto.From);
    }

    public async Task<ServiceDto> CreateAsync(ServiceRequest request, CancellationToken cancellationToken = default)
    {
        var providerId = currentUser.RequireProviderId();
        await EnsureUniqueNameAsync(providerId, request.Name, null, cancellationToken);

        var service = Service.Create(providerId, request.Name, request.Description, request.DurationMinutes, request.Price);
        services.Add(service);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ServiceDto.From(service);
    }

    public async Task<ServiceDto> UpdateAsync(Guid serviceId, ServiceRequest request, CancellationToken cancellationToken = default)
    {
        var service = await LoadOwnAsync(serviceId, cancellationToken);
        await EnsureUniqueNameAsync(service.ProviderId, request.Name, service.Id, cancellationToken);

        service.Update(request.Name, request.Description, request.DurationMinutes, request.Price);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ServiceDto.From(service);
    }

    public async Task<ServiceDto> SetStatusAsync(Guid serviceId, bool isActive, CancellationToken cancellationToken = default)
    {
        var service = await LoadOwnAsync(serviceId, cancellationToken);
        if (isActive)
        {
            service.Activate();
        }
        else
        {
            service.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ServiceDto.From(service);
    }

    /// <summary>Exclusão física só para serviços nunca agendados; caso contrário use a desativação.</summary>
    public async Task DeleteAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        var service = await LoadOwnAsync(serviceId, cancellationToken);
        if (await appointments.ExistsForServiceAsync(service.Id, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.ServiceInUse, "O serviço possui agendamentos; desative-o em vez de excluí-lo.");
        }

        services.Remove(service);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    internal static NotFoundException NotFound() => new(ErrorCodes.ServiceNotFound, "Serviço não encontrado.");

    private async Task<Service> LoadOwnAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        var providerId = currentUser.RequireProviderId();
        var service = await services.GetByIdAsync(serviceId, cancellationToken);
        return service is not null && service.ProviderId == providerId ? service : throw NotFound();
    }

    private async Task EnsureUniqueNameAsync(Guid providerId, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (await services.NameExistsAsync(providerId, name.Trim(), excludeId, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.ServiceNameTaken, "Já existe um serviço com este nome.");
        }
    }
}
