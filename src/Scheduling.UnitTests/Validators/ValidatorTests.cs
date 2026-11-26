using FluentValidation;
using FluentValidation.TestHelper;
using Scheduling.Application.Admin;
using Scheduling.Application.Appointments;
using Scheduling.Application.Auth;
using Scheduling.Application.Availability;
using Scheduling.Application.Common.Paging;
using Scheduling.Application.Customers;
using Scheduling.Application.Providers;
using Scheduling.Application.Schedule;
using Scheduling.Application.Services;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Providers;

namespace Scheduling.UnitTests.Validators;

public sealed class AuthValidatorsTests
{
    static AuthValidatorsTests() => ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;

    private static readonly AddressDto ValidAddress = new("Rua A", "100", null, "Centro", "São Paulo", "SP", "01001000");

    [Fact]
    public void RegisterCustomer_Valid() =>
        new RegisterCustomerRequestValidator().TestValidate(new RegisterCustomerRequest("Maria", "maria@ex.com", "Senha123", "+5511999998888"))
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("senhasemdigito")]
    [InlineData("12345678")]
    [InlineData("a1")]
    public void RegisterCustomer_WeakPassword(string password) =>
        new RegisterCustomerRequestValidator().TestValidate(new RegisterCustomerRequest("Maria", "maria@ex.com", password, "+5511999998888"))
            .ShouldHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void RegisterCustomer_InvalidFields()
    {
        var result = new RegisterCustomerRequestValidator().TestValidate(new RegisterCustomerRequest("M", "invalid", "Senha123", "11999"));
        result.ShouldHaveValidationErrorFor(x => x.Name);
        result.ShouldHaveValidationErrorFor(x => x.Email);
        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }

    [Fact]
    public void RegisterCustomer_NullFields_DoNotThrow()
    {
        var result = new RegisterCustomerRequestValidator().TestValidate(new RegisterCustomerRequest(null!, null!, null!, null!));
        result.ShouldHaveValidationErrorFor(x => x.Password);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void RegisterProvider_Valid() =>
        new RegisterProviderRequestValidator().TestValidate(new RegisterProviderRequest(
                "Ana", "ana@ex.com", "Senha123", "Salão da Ana", null, ServiceCategory.Beauty, null, "+5511999998888", ValidAddress, null))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void RegisterProvider_InvalidAddressAndTimeZone()
    {
        var result = new RegisterProviderRequestValidator().TestValidate(new RegisterProviderRequest(
            "Ana", "ana@ex.com", "Senha123", "Salão", null, ServiceCategory.Beauty, "x", "+5511999998888",
            ValidAddress with { PostalCode = "123", State = "São Paulo" }, "Mars/Olympus"));

        result.ShouldHaveValidationErrorFor(x => x.Address.PostalCode);
        result.ShouldHaveValidationErrorFor(x => x.Address.State);
        result.ShouldHaveValidationErrorFor(x => x.TimeZoneId);
        result.ShouldHaveValidationErrorFor(x => x.ContactEmail);
    }

    [Fact]
    public void RegisterProvider_MissingAddress() =>
        new RegisterProviderRequestValidator().TestValidate(new RegisterProviderRequest(
                "Ana", "ana@ex.com", "Senha123", "Salão", null, ServiceCategory.Beauty, null, "+5511999998888", null!, null))
            .ShouldHaveValidationErrorFor(x => x.Address);

    [Fact]
    public void Login_RequiresFields()
    {
        var result = new LoginRequestValidator().TestValidate(new LoginRequest("", ""));
        result.ShouldHaveValidationErrorFor(x => x.Email);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData(0, 20, true)]
    [InlineData(1, 0, true)]
    [InlineData(1, 101, true)]
    [InlineData(1, 100, false)]
    public void PageQuery_Bounds(int page, int pageSize, bool hasError)
    {
        var result = new PageQueryDefaultValidator().TestValidate(new PageQuery { Page = page, PageSize = pageSize });
        Assert.Equal(hasError, !result.IsValid);
    }
}

public sealed class ProviderValidatorsTests
{
    static ProviderValidatorsTests() => ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;

    [Fact]
    public void BookingPolicy_OutOfRange()
    {
        var result = new BookingPolicyDtoValidator().TestValidate(new BookingPolicyDto(false, 7, 125, -1, 0, 20000));
        result.ShouldHaveValidationErrorFor(x => x.SlotStepMinutes);
        result.ShouldHaveValidationErrorFor(x => x.BufferMinutes);
        result.ShouldHaveValidationErrorFor(x => x.MinBookingNoticeMinutes);
        result.ShouldHaveValidationErrorFor(x => x.BookingHorizonDays);
        result.ShouldHaveValidationErrorFor(x => x.MinCancellationNoticeMinutes);
    }

    [Theory]
    [InlineData(30, 150.0, true)]
    [InlineData(7, 150.0, false)]
    [InlineData(0, 150.0, false)]
    [InlineData(725, 150.0, false)]
    [InlineData(30, -1.0, false)]
    [InlineData(30, 100000.0, false)]
    public void ServiceRequest_DurationAndPrice(int duration, double price, bool valid)
    {
        var result = new ServiceRequestValidator().TestValidate(new ServiceRequest("Corte", null, duration, (decimal)price));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void ServiceRequest_PriceWithThreeDecimals_IsInvalid() =>
        new ServiceRequestValidator().TestValidate(new ServiceRequest("Corte", null, 30, 10.555m)).ShouldHaveValidationErrorFor(x => x.Price);

    [Fact]
    public void WeeklySchedule_Valid() =>
        new WeeklyScheduleDtoValidator().TestValidate(new WeeklyScheduleDto(null,
                [new DayScheduleDto(DayOfWeek.Tuesday, [new TimeIntervalDto("08:00", "12:00"), new TimeIntervalDto("13:00", "18:00")])]))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void WeeklySchedule_OverlappingIntervals_IsInvalid() =>
        Assert.False(new WeeklyScheduleDtoValidator().TestValidate(new WeeklyScheduleDto(null,
            [new DayScheduleDto(DayOfWeek.Monday, [new TimeIntervalDto("08:00", "12:00"), new TimeIntervalDto("11:00", "14:00")])])).IsValid);

    [Fact]
    public void WeeklySchedule_DuplicateDay_IsInvalid() =>
        new WeeklyScheduleDtoValidator().TestValidate(new WeeklyScheduleDto(null,
            [
                new DayScheduleDto(DayOfWeek.Monday, [new TimeIntervalDto("08:00", "09:00")]),
                new DayScheduleDto(DayOfWeek.Monday, [new TimeIntervalDto("10:00", "11:00")]),
            ])).ShouldHaveValidationErrorFor(x => x.Days);

    [Theory]
    [InlineData("8:00", "12:00")]
    [InlineData("08:00", "24:00")]
    [InlineData("12:00", "08:00")]
    [InlineData("12:00", "12:00")]
    public void WeeklySchedule_InvalidInterval(string start, string end) =>
        Assert.False(new WeeklyScheduleDtoValidator().TestValidate(new WeeklyScheduleDto(null,
            [new DayScheduleDto(DayOfWeek.Monday, [new TimeIntervalDto(start, end)])])).IsValid);

    [Fact]
    public void WeeklySchedule_NullDays_IsInvalidWithoutThrowing() =>
        new WeeklyScheduleDtoValidator().TestValidate(new WeeklyScheduleDto(null, null!)).ShouldHaveValidationErrorFor(x => x.Days);

    [Fact]
    public void ScheduleException_Rules()
    {
        var validator = new ScheduleExceptionRequestValidator();
        var day = new DateOnly(2026, 12, 25);

        validator.TestValidate(new ScheduleExceptionRequest(ScheduleExceptionType.Closure, day, day, null, null, "Natal")).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new ScheduleExceptionRequest(ScheduleExceptionType.Closure, day, day.AddDays(-1), null, null, null)).ShouldHaveValidationErrorFor(x => x.EndDate);
        validator.TestValidate(new ScheduleExceptionRequest(ScheduleExceptionType.Closure, day, day, "10:00", null, null)).ShouldHaveValidationErrorFor(x => x.EndTime);
        validator.TestValidate(new ScheduleExceptionRequest(ScheduleExceptionType.SpecialHours, day, day, null, null, null)).ShouldHaveValidationErrorFor(x => x.StartTime);
        validator.TestValidate(new ScheduleExceptionRequest(ScheduleExceptionType.SpecialHours, day, day, "13:00", "09:00", null)).ShouldHaveValidationErrorFor(x => x.EndTime);
        validator.TestValidate(new ScheduleExceptionRequest(ScheduleExceptionType.Closure, day, day.AddDays(366), null, null, null)).ShouldHaveValidationErrorFor(x => x.EndDate);
    }

    [Fact]
    public void UpdateProvider_InvalidPhoneAndTimeZone()
    {
        var result = new UpdateProviderRequestValidator().TestValidate(new UpdateProviderRequest(
            "Clínica", null, ServiceCategory.Health, "a@b.com", "123", new AddressDto("Rua", "1", null, "Centro", "SP", "SP", "01001000"), ""));
        result.ShouldHaveValidationErrorFor(x => x.ContactPhone);
        result.ShouldHaveValidationErrorFor(x => x.TimeZoneId);
    }

    [Fact]
    public void ProviderSearch_InvalidSort() =>
        new ProviderSearchQueryValidator().TestValidate(new ProviderSearchQuery { Sort = "name" }).ShouldHaveValidationErrorFor(x => x.Sort);
}

public sealed class BookingValidatorsTests
{
    static BookingValidatorsTests() => ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;

    [Theory]
    [InlineData("2026-11-16T10:00:00-03:00", true)]
    [InlineData("2026-11-16T13:00:00Z", true)]
    [InlineData("2026-11-16T10:00-03:00", true)]
    [InlineData("2026-11-16T10:00:00", false)]
    [InlineData("16/11/2026 10:00", false)]
    [InlineData("", false)]
    public void CreateAppointment_StartAtMustHaveOffset(string startAt, bool valid)
    {
        var result = new CreateAppointmentRequestValidator().TestValidate(new CreateAppointmentRequest(Guid.NewGuid(), Guid.NewGuid(), startAt, null));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void CreateAppointment_NotesMax500_AndIdsRequired()
    {
        var result = new CreateAppointmentRequestValidator().TestValidate(new CreateAppointmentRequest(Guid.Empty, Guid.Empty, "2026-11-16T10:00:00-03:00", new string('n', 501)));
        result.ShouldHaveValidationErrorFor(x => x.Notes);
        result.ShouldHaveValidationErrorFor(x => x.ProviderId);
        result.ShouldHaveValidationErrorFor(x => x.ServiceId);
    }

    [Fact]
    public void AvailabilityQuery_RequiresServiceAndDate()
    {
        var result = new AvailabilityQueryValidator().TestValidate(new AvailabilityQuery());
        result.ShouldHaveValidationErrorFor(x => x.ServiceId);
        result.ShouldHaveValidationErrorFor(x => x.Date);
    }

    [Theory]
    [InlineData("ab", false)]
    [InlineData("abc", true)]
    public void CancelAppointment_ReasonLength(string reason, bool valid) =>
        Assert.Equal(valid, new CancelAppointmentRequestValidator().TestValidate(new CancelAppointmentRequest(reason, null)).IsValid);

    [Fact]
    public void CancelAppointment_ReasonMax500() =>
        new CancelAppointmentRequestValidator().TestValidate(new CancelAppointmentRequest(new string('r', 501), null)).ShouldHaveValidationErrorFor(x => x.Reason);

    [Fact]
    public void CancelAppointment_InvalidOnBehalfOf() =>
        new CancelAppointmentRequestValidator().TestValidate(new CancelAppointmentRequest("Motivo", (CancellationParty)9)).ShouldHaveValidationErrorFor(x => x.OnBehalfOf);

    [Fact]
    public void AgendaQuery_PeriodRules()
    {
        var validator = new AgendaQueryValidator();
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        validator.TestValidate(new AgendaQuery { From = from, To = from.AddDays(-1) }).ShouldHaveValidationErrorFor(x => x.To);
        validator.TestValidate(new AgendaQuery { From = from, To = from.AddDays(367) }).ShouldHaveValidationErrorFor(x => x.To);
        validator.TestValidate(new AgendaQuery { Sort = "status" }).ShouldHaveValidationErrorFor(x => x.Sort);
        validator.TestValidate(new AgendaQuery { From = from, To = from.AddDays(30), Sort = "-startAt" }).ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class CustomerAdminValidatorsTests
{
    static CustomerAdminValidatorsTests() => ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;

    [Fact]
    public void UpdateCustomer_Invalid()
    {
        var result = new UpdateCustomerRequestValidator().TestValidate(new UpdateCustomerRequest("A", "x", "1"));
        result.ShouldHaveValidationErrorFor(x => x.Name);
        result.ShouldHaveValidationErrorFor(x => x.Email);
        result.ShouldHaveValidationErrorFor(x => x.Phone);
    }

    [Fact]
    public void CustomerHistory_InvalidPage() =>
        new CustomerHistoryQueryValidator().TestValidate(new CustomerHistoryQuery { PageSize = 500 }).ShouldHaveValidationErrorFor(x => x.PageSize);

    [Fact]
    public void AdminUserQuery_TextTooLong() =>
        new AdminUserQueryValidator().TestValidate(new AdminUserQuery { Q = new string('q', 101) }).ShouldHaveValidationErrorFor(x => x.Q);
}
