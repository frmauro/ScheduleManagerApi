using Scheduling.Application.Abstractions;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;

namespace Scheduling.Application.Common;

public static class CurrentUserExtensions
{
    public static Guid RequireProviderId(this ICurrentUser user) =>
        user.ProviderId ?? throw new ForbiddenException(ErrorCodes.AuthForbidden, "Operação exclusiva de prestadores.");

    public static Guid RequireCustomerId(this ICurrentUser user) =>
        user.CustomerId ?? throw new ForbiddenException(ErrorCodes.AuthForbidden, "Operação exclusiva de clientes.");

    public static Actor ToActor(this ICurrentUser user) => new(user.UserId, user.Role);
}
