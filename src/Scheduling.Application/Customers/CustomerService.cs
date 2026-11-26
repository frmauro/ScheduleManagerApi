using FluentValidation;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Common.Validation;
using Scheduling.Domain.Common;
using Scheduling.Domain.Customers;

namespace Scheduling.Application.Customers;

public sealed record CustomerProfileDto(Guid Id, string Name, string Email, string Phone, DateTimeOffset CreatedAt)
{
    public static CustomerProfileDto From(Customer c) => new(c.Id, c.Name, c.Email, c.Phone, c.CreatedAt);
}

public sealed record UpdateCustomerRequest(string Name, string Email, string Phone);

public sealed class CustomerHistoryQuery : AppointmentListQuery
{
    public Guid? ProviderId { get; set; }
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).PersonName();
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Phone).E164Phone();
    }
}

public sealed class CustomerHistoryQueryValidator : AppointmentListQueryValidator<CustomerHistoryQuery>;

/// <summary>Perfil e histórico do cliente (FR-012).</summary>
public sealed class CustomerService(
    ICustomerRepository customers,
    IUserRepository users,
    IAppointmentRepository appointments,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<CustomerProfileDto> GetOwnAsync(CancellationToken cancellationToken = default) =>
        CustomerProfileDto.From(await LoadOwnAsync(cancellationToken));

    /// <summary>Atualiza o perfil mantendo nome/e-mail sincronizados entre Customer e User.</summary>
    public async Task<CustomerProfileDto> UpdateOwnAsync(UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await LoadOwnAsync(cancellationToken);
        var user = await users.GetByIdAsync(customer.UserId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "Usuário não encontrado.");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await users.EmailExistsAsync(normalizedEmail, user.Id, cancellationToken))
        {
            throw new ConflictException(ErrorCodes.UserEmailTaken, "Já existe uma conta com este e-mail.");
        }

        user.UpdateProfile(request.Name, request.Email);
        customer.Update(request.Name, request.Email, request.Phone);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CustomerProfileDto.From(customer);
    }

    public async Task<PagedResult<AppointmentSummaryDto>> GetHistoryAsync(CustomerHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var search = new AppointmentSearch(
            query.ProviderId,
            currentUser.RequireCustomerId(),
            null,
            query.Status,
            query.From,
            query.To,
            query.Sort != AppointmentListQuery.SortAscending);
        var result = await appointments.SearchAsync(search, query, cancellationToken);
        return result.Map(AppointmentSummaryDto.From);
    }

    private async Task<Customer> LoadOwnAsync(CancellationToken cancellationToken) =>
        await customers.GetByIdAsync(currentUser.RequireCustomerId(), cancellationToken)
        ?? throw new NotFoundException(ErrorCodes.UserNotFound, "Cliente não encontrado.");
}
