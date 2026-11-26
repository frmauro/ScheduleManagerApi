using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common.Exceptions;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Availability;
using Scheduling.Domain.Common;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Notifications;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;
using static Scheduling.UnitTests.TestData;
using AppointmentEntity = Scheduling.Domain.Appointments.Appointment;

namespace Scheduling.UnitTests.Application;

public sealed class AppointmentServiceTests
{
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IProviderRepository _providers = Substitute.For<IProviderRepository>();
    private readonly IServiceRepository _services = Substitute.For<IServiceRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly Provider _provider = Provider().WithHours(DayOfWeek.Monday, Range(9, 0, 12, 0));
    private readonly Customer _customer = Customer.Create(Guid.NewGuid(), "Maria", "maria@ex.com", "+5511999998888");
    private readonly Service _service;

    public AppointmentServiceTests()
    {
        _service = Service(_provider);
        _currentUser.Role.Returns(Role.Customer);
        _currentUser.UserId.Returns(_customer.UserId);
        _currentUser.CustomerId.Returns(_customer.Id);
        _providers.GetWithScheduleAsync(_provider.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(_provider);
        _providers.GetByIdAsync(_provider.Id, Arg.Any<CancellationToken>()).Returns(_provider);
        _services.GetByIdAsync(_service.Id, Arg.Any<CancellationToken>()).Returns(_service);
        _customers.GetByIdAsync(_customer.Id, Arg.Any<CancellationToken>()).Returns(_customer);
        _appointments.GetOccupationsAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Occupation>());
        _unitOfWork.ExecuteSerializedAsync(Arg.Any<Guid>(), Arg.Any<Func<CancellationToken, Task<CreateAppointmentResult>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<CreateAppointmentResult>>>()(CancellationToken.None));
    }

    [Fact]
    public async Task Create_RunsInsideProviderSerializedScope()
    {
        await Sut().CreateAsync(Request(), null);
        await _unitOfWork.Received(1).ExecuteSerializedAsync(_provider.Id, Arg.Any<Func<CancellationToken, Task<CreateAppointmentResult>>>(), Arg.Any<CancellationToken>());
    }

    private AppointmentService Sut() =>
        new(_appointments, _providers, _services, _customers, _notifications, _currentUser, _unitOfWork, new FakeTimeProvider(Now));

    private CreateAppointmentRequest Request(string startAt = "2026-11-16T10:00:00-03:00", Guid? serviceId = null) =>
        new(_provider.Id, serviceId ?? _service.Id, startAt, null);

    [Fact]
    public async Task Create_PersistsAppointmentAndNotifications()
    {
        var result = await Sut().CreateAsync(Request(), null);

        Assert.False(result.IsReplay);
        Assert.Equal(AppointmentStatus.Pending, result.Appointment.Status);
        Assert.Equal(TimeSpan.FromHours(-3), result.Appointment.StartAt.Offset);
        _appointments.Received(1).Add(Arg.Any<AppointmentEntity>());
        _notifications.Received(1).AddRange(Arg.Is<IEnumerable<Notification>>(n => n.Count() == 2));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_InactiveProvider_IsRejected()
    {
        _provider.Deactivate();
        var ex = await Assert.ThrowsAsync<DomainException>(() => Sut().CreateAsync(Request(), null));
        Assert.Equal(ErrorCodes.ProviderInactive, ex.Code);
    }

    [Fact]
    public async Task Create_InactiveService_IsRejected()
    {
        _service.Deactivate();
        var ex = await Assert.ThrowsAsync<DomainException>(() => Sut().CreateAsync(Request(), null));
        Assert.Equal(ErrorCodes.ServiceInactive, ex.Code);
    }

    [Fact]
    public async Task Create_ServiceOfAnotherProvider_IsNotFound()
    {
        var foreign = Service(Provider());
        _services.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Sut().CreateAsync(Request(serviceId: foreign.Id), null));
        Assert.Equal(ErrorCodes.ServiceNotFound, ex.Code);
    }

    [Fact]
    public async Task Create_UnknownProvider_IsNotFound()
    {
        var request = Request() with { ProviderId = Guid.NewGuid() };
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Sut().CreateAsync(request, null));
        Assert.Equal(ErrorCodes.ProviderNotFound, ex.Code);
    }

    [Fact]
    public async Task Create_IdempotentReplay_ReturnsOriginalWithoutSaving()
    {
        var key = Guid.NewGuid();
        var first = await Sut().CreateAsync(Request(), key);
        var stored = AppointmentEntity.Create(_provider.Id, _customer.Id, _service.Id, new ServiceSnapshot("Consulta", 30, 150m),
            new TimeSlot(first.Appointment.StartAt, first.Appointment.EndAt), 0, false, null, key, CustomerActor(), Now);
        _appointments.GetByIdempotencyKeyAsync(_customer.Id, key, Arg.Any<CancellationToken>()).Returns(stored);
        _unitOfWork.ClearReceivedCalls();

        var replay = await Sut().CreateAsync(Request(), key);

        Assert.True(replay.IsReplay);
        Assert.Equal(stored.Id, replay.Appointment.Id);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_IdempotencyKeyWithDifferentPayload_IsRejected()
    {
        var key = Guid.NewGuid();
        var stored = AppointmentEntity.Create(_provider.Id, _customer.Id, _service.Id, new ServiceSnapshot("Consulta", 30, 150m),
            new TimeSlot(SaoPauloTime(NextMonday, 11), SaoPauloTime(NextMonday, 11, 30)), 0, false, null, key, CustomerActor(), Now);
        _appointments.GetByIdempotencyKeyAsync(_customer.Id, key, Arg.Any<CancellationToken>()).Returns(stored);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().CreateAsync(Request(), key));
        Assert.Equal(ErrorCodes.IdempotencyKeyReused, ex.Code);
    }

    [Fact]
    public async Task Create_ByNonCustomer_IsForbidden()
    {
        _currentUser.CustomerId.Returns((Guid?)null);
        _currentUser.Role.Returns(Role.Provider);
        await Assert.ThrowsAsync<ForbiddenException>(() => Sut().CreateAsync(Request(), null));
    }

    [Fact]
    public async Task Create_OccupiedSlot_IsConflict()
    {
        _appointments.GetOccupationsAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([Busy(NextMonday, 10, 0, 30)]);

        var ex = await Assert.ThrowsAsync<DomainException>(() => Sut().CreateAsync(Request(), null));
        Assert.Equal(ErrorCodes.AppointmentSlotUnavailable, ex.Code);
        _appointments.DidNotReceive().Add(Arg.Any<AppointmentEntity>());
    }

    [Fact]
    public async Task Confirm_ByCustomer_IsForbidden() =>
        await Assert.ThrowsAsync<ForbiddenException>(() => Sut().ConfirmAsync(Guid.NewGuid(), null));

    [Fact]
    public async Task Get_AppointmentOfAnotherCustomer_IsNotFound()
    {
        var other = Appointment(SaoPauloTime(NextMonday, 10));
        _appointments.GetByIdAsync(other.Id, Arg.Any<CancellationToken>()).Returns(other);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Sut().GetByIdAsync(other.Id));
        Assert.Equal(ErrorCodes.AppointmentNotFound, ex.Code);
    }

    [Fact]
    public async Task Cancel_WithStaleVersion_IsPreconditionFailed()
    {
        var a = AppointmentEntity.Create(_provider.Id, _customer.Id, _service.Id, new ServiceSnapshot("Consulta", 30, 150m),
            new TimeSlot(SaoPauloTime(NextMonday, 10), SaoPauloTime(NextMonday, 10, 30)), 0, false, null, null, CustomerActor(), Now);
        _appointments.GetByIdAsync(a.Id, Arg.Any<CancellationToken>()).Returns(a);

        await Assert.ThrowsAsync<PreconditionFailedException>(() => Sut().CancelAsync(a.Id, new CancelAppointmentRequest("Motivo", null), 999));
    }

    [Fact]
    public async Task Cancel_OnBehalfOfByNonAdmin_IsForbidden() =>
        await Assert.ThrowsAsync<ForbiddenException>(() => Sut().CancelAsync(Guid.NewGuid(), new CancelAppointmentRequest("Motivo", CancellationParty.Provider), null));
}
