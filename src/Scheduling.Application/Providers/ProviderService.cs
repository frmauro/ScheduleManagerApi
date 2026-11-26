using Scheduling.Application.Abstractions;
using Scheduling.Application.Common;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Application.Common.Paging;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;

namespace Scheduling.Application.Providers;

/// <summary>Perfil, política de reservas e catálogo público de prestadores (FR-001, FR-005, FR-006).</summary>
public sealed class ProviderService(IProviderRepository providers, ICurrentUser currentUser, IUnitOfWork unitOfWork)
{
    public async Task<PagedResult<ProviderSummaryDto>> SearchAsync(ProviderSearchQuery query, CancellationToken cancellationToken = default)
    {
        var search = new ProviderSearch(query.Category, query.City, query.Q, query.Sort == ProviderSearchQuery.SortDescending);
        var result = await providers.SearchActiveAsync(search, query, cancellationToken);
        return result.Map(ProviderSummaryDto.From);
    }

    public async Task<ProviderPublicProfileDto> GetPublicAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var provider = await providers.GetByIdAsync(providerId, cancellationToken);
        if (provider is null || !provider.IsActive)
        {
            throw NotFound();
        }

        return ProviderPublicProfileDto.From(provider);
    }

    public async Task<ProviderProfileDto> GetOwnAsync(CancellationToken cancellationToken = default) =>
        ProviderProfileDto.From(await LoadOwnAsync(cancellationToken));

    public async Task<ProviderProfileDto> UpdateOwnAsync(UpdateProviderRequest request, CancellationToken cancellationToken = default)
    {
        var provider = await LoadOwnAsync(cancellationToken);
        provider.UpdateProfile(
            request.DisplayName,
            request.Description,
            request.Category,
            request.ContactEmail,
            request.ContactPhone,
            request.Address.ToDomain(),
            request.TimeZoneId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ProviderProfileDto.From(provider);
    }

    public async Task<BookingPolicyDto> UpdateBookingPolicyAsync(BookingPolicyDto request, CancellationToken cancellationToken = default)
    {
        var provider = await LoadOwnAsync(cancellationToken);
        provider.UpdateBookingPolicy(request.ToDomain());

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BookingPolicyDto.From(provider.BookingPolicy);
    }

    internal static NotFoundException NotFound() => new(ErrorCodes.ProviderNotFound, "Prestador não encontrado.");

    private async Task<Provider> LoadOwnAsync(CancellationToken cancellationToken) =>
        await providers.GetByIdAsync(currentUser.RequireProviderId(), cancellationToken) ?? throw NotFound();
}
