using FluentValidation;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Application.Common.Paging;
using Scheduling.Domain.Common;
using Scheduling.Domain.Users;

namespace Scheduling.Application.Admin;

public sealed record UserSummaryDto(Guid Id, string Name, string Email, Role Role, bool IsActive, DateTimeOffset CreatedAt)
{
    public static UserSummaryDto From(User u) => new(u.Id, u.Name, u.Email, u.Role, u.IsActive, u.CreatedAt);
}

public sealed class AdminUserQuery : PageQuery
{
    public Role? Role { get; set; }

    public bool? IsActive { get; set; }

    public string? Q { get; set; }
}

public sealed class AdminUserQueryValidator : PageQueryValidator<AdminUserQuery>
{
    public AdminUserQueryValidator()
    {
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.Q).MaximumLength(ValidationLimits.QueryTextMaxLength);
    }
}

/// <summary>Administração de contas (FR-010). Desativar um prestador o remove do catálogo e bloqueia reservas.</summary>
public sealed class AdminUserService(
    IUserRepository users,
    IProviderRepository providers,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork)
{
    public async Task<PagedResult<UserSummaryDto>> SearchAsync(AdminUserQuery query, CancellationToken cancellationToken = default)
    {
        var result = await users.SearchAsync(new UserSearch(query.Role, query.IsActive, query.Q), query, cancellationToken);
        return result.Map(UserSummaryDto.From);
    }

    public async Task SetStatusAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        if (userId == currentUser.UserId && !isActive)
        {
            throw new BusinessRuleException(ErrorCodes.AdminCannotDeactivateSelf, "Um administrador não pode desativar a própria conta.");
        }

        var user = await users.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException(ErrorCodes.UserNotFound, "Usuário não encontrado.");

        if (isActive)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
        }

        if (user.Role == Role.Provider && await providers.GetByUserIdAsync(user.Id, cancellationToken) is { } provider)
        {
            if (isActive)
            {
                provider.Activate();
            }
            else
            {
                provider.Deactivate();
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
