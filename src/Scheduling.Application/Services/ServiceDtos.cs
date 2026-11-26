using FluentValidation;
using Scheduling.Application.Common.Paging;
using Scheduling.Domain.Common;
using Scheduling.Domain.Services;

namespace Scheduling.Application.Services;

public sealed record ServiceRequest(string Name, string? Description, int DurationMinutes, decimal Price);

public sealed record ServiceDto(Guid Id, Guid ProviderId, string Name, string? Description, int DurationMinutes, decimal Price, bool IsActive)
{
    public static ServiceDto From(Service s) => new(s.Id, s.ProviderId, s.Name, s.Description, s.DurationMinutes, s.Price, s.IsActive);
}

public sealed record ActiveStatusRequest(bool IsActive);

public sealed class ServiceListQuery : PageQuery
{
    public bool? IsActive { get; set; }
}

public sealed class ServiceRequestValidator : AbstractValidator<ServiceRequest>
{
    public ServiceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(ValidationLimits.ServiceNameMinLength, ValidationLimits.ServiceNameMaxLength);
        RuleFor(x => x.Description).MaximumLength(ValidationLimits.ServiceDescriptionMaxLength);
        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(ValidationLimits.ServiceDurationMin, ValidationLimits.ServiceDurationMax)
            .Must(d => d % ValidationLimits.ServiceDurationMultiple == 0).WithMessage("'durationMinutes' deve ser múltiplo de 5.");
        RuleFor(x => x.Price)
            .InclusiveBetween(ValidationLimits.ServicePriceMin, ValidationLimits.ServicePriceMax)
            .Must(p => decimal.Round(p, 2) == p).WithMessage("'price' deve ter no máximo 2 casas decimais.");
    }
}

public sealed class ServiceListQueryValidator : PageQueryValidator<ServiceListQuery>;
