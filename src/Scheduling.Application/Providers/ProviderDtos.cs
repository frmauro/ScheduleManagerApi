using FluentValidation;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Common.Validation;
using Scheduling.Domain.Common;
using Scheduling.Domain.Providers;

namespace Scheduling.Application.Providers;

public sealed record AddressDto(string Street, string Number, string? Complement, string District, string City, string State, string PostalCode)
{
    public static AddressDto From(Address a) => new(a.Street, a.Number, a.Complement, a.District, a.City, a.State, a.PostalCode);

    public Address ToDomain() => Address.Create(Street, Number, Complement, District, City, State, PostalCode);
}

public sealed record BookingPolicyDto(
    bool AutoConfirm,
    int SlotStepMinutes,
    int BufferMinutes,
    int MinBookingNoticeMinutes,
    int BookingHorizonDays,
    int MinCancellationNoticeMinutes)
{
    public static BookingPolicyDto From(BookingPolicy p) =>
        new(p.AutoConfirm, p.SlotStepMinutes, p.BufferMinutes, p.MinBookingNoticeMinutes, p.BookingHorizonDays, p.MinCancellationNoticeMinutes);

    public BookingPolicy ToDomain() =>
        BookingPolicy.Create(AutoConfirm, SlotStepMinutes, BufferMinutes, MinBookingNoticeMinutes, BookingHorizonDays, MinCancellationNoticeMinutes);
}

public sealed record UpdateProviderRequest(
    string DisplayName,
    string? Description,
    ServiceCategory Category,
    string ContactEmail,
    string ContactPhone,
    AddressDto Address,
    string TimeZoneId);

public sealed record ProviderSummaryDto(Guid Id, string DisplayName, ServiceCategory Category, string City, string State)
{
    public static ProviderSummaryDto From(Provider p) => new(p.Id, p.DisplayName, p.Category, p.Address.City, p.Address.State);
}

public record ProviderPublicProfileDto(
    Guid Id,
    string DisplayName,
    string? Description,
    ServiceCategory Category,
    string ContactEmail,
    string ContactPhone,
    AddressDto Address,
    string TimeZoneId)
{
    public static ProviderPublicProfileDto From(Provider p) =>
        new(p.Id, p.DisplayName, p.Description, p.Category, p.ContactEmail, p.ContactPhone, AddressDto.From(p.Address), p.TimeZoneId);
}

public sealed record ProviderProfileDto(
    Guid Id,
    string DisplayName,
    string? Description,
    ServiceCategory Category,
    string ContactEmail,
    string ContactPhone,
    AddressDto Address,
    string TimeZoneId,
    BookingPolicyDto BookingPolicy,
    bool IsActive)
    : ProviderPublicProfileDto(Id, DisplayName, Description, Category, ContactEmail, ContactPhone, Address, TimeZoneId)
{
    public static new ProviderProfileDto From(Provider p) =>
        new(p.Id, p.DisplayName, p.Description, p.Category, p.ContactEmail, p.ContactPhone, AddressDto.From(p.Address), p.TimeZoneId, BookingPolicyDto.From(p.BookingPolicy), p.IsActive);
}

public sealed class ProviderSearchQuery : PageQuery
{
    public const string SortAscending = "displayName";
    public const string SortDescending = "-displayName";

    public ServiceCategory? Category { get; set; }

    public string? City { get; set; }

    public string? Q { get; set; }

    public string Sort { get; set; } = SortAscending;
}

public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
{
    public AddressDtoValidator()
    {
        RuleFor(x => x.Street).NotEmpty().MaximumLength(ValidationLimits.StreetMaxLength);
        RuleFor(x => x.Number).NotEmpty().MaximumLength(ValidationLimits.NumberMaxLength);
        RuleFor(x => x.Complement).MaximumLength(ValidationLimits.ComplementMaxLength);
        RuleFor(x => x.District).NotEmpty().MaximumLength(ValidationLimits.DistrictMaxLength);
        RuleFor(x => x.City).NotEmpty().MaximumLength(ValidationLimits.CityMaxLength);
        RuleFor(x => x.State).NotEmpty().Matches("^[A-Za-z]{2}$").WithMessage("'state' deve ser a sigla da UF com 2 letras.");
        RuleFor(x => x.PostalCode).NotEmpty().Matches(@"^\d{8}$").WithMessage("'postalCode' deve conter 8 dígitos.");
    }
}

public sealed class BookingPolicyDtoValidator : AbstractValidator<BookingPolicyDto>
{
    public BookingPolicyDtoValidator()
    {
        RuleFor(x => x.SlotStepMinutes).InclusiveBetween(ValidationLimits.SlotStepMin, ValidationLimits.SlotStepMax)
            .Must(v => v % ValidationLimits.MinutesMultiple == 0).WithMessage("'slotStepMinutes' deve ser múltiplo de 5.");
        RuleFor(x => x.BufferMinutes).InclusiveBetween(ValidationLimits.BufferMin, ValidationLimits.BufferMax)
            .Must(v => v % ValidationLimits.MinutesMultiple == 0).WithMessage("'bufferMinutes' deve ser múltiplo de 5.");
        RuleFor(x => x.MinBookingNoticeMinutes).InclusiveBetween(ValidationLimits.NoticeMinutesMin, ValidationLimits.NoticeMinutesMax);
        RuleFor(x => x.BookingHorizonDays).InclusiveBetween(ValidationLimits.HorizonDaysMin, ValidationLimits.HorizonDaysMax);
        RuleFor(x => x.MinCancellationNoticeMinutes).InclusiveBetween(ValidationLimits.NoticeMinutesMin, ValidationLimits.NoticeMinutesMax);
    }
}

public sealed class UpdateProviderRequestValidator : AbstractValidator<UpdateProviderRequest>
{
    public UpdateProviderRequestValidator()
    {
        RuleFor(x => x.DisplayName).PersonName();
        RuleFor(x => x.Description).MaximumLength(ValidationLimits.ProviderDescriptionMaxLength);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.ContactEmail).ValidEmail();
        RuleFor(x => x.ContactPhone).E164Phone();
        RuleFor(x => x.Address).NotNull().SetValidator(new AddressDtoValidator());
        RuleFor(x => x.TimeZoneId).NotEmpty().IanaTimeZone();
    }
}

public sealed class ProviderSearchQueryValidator : PageQueryValidator<ProviderSearchQuery>
{
    public ProviderSearchQueryValidator()
    {
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.City).MaximumLength(ValidationLimits.CityMaxLength);
        RuleFor(x => x.Q).MaximumLength(ValidationLimits.QueryTextMaxLength);
        RuleFor(x => x.Sort).Must(s => s is ProviderSearchQuery.SortAscending or ProviderSearchQuery.SortDescending)
            .WithMessage("'sort' deve ser 'displayName' ou '-displayName'.");
    }
}
