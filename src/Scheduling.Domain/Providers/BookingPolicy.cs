using Scheduling.Domain.Common;

namespace Scheduling.Domain.Providers;

/// <summary>Regras de reserva do prestador (FR-005). Valores padrão conforme Assumptions da spec.</summary>
public sealed record BookingPolicy
{
    public const int DefaultSlotStepMinutes = 15;
    public const int DefaultBufferMinutes = 0;
    public const int DefaultMinBookingNoticeMinutes = 60;
    public const int DefaultBookingHorizonDays = 60;
    public const int DefaultMinCancellationNoticeMinutes = 120;

    private BookingPolicy(bool autoConfirm, int slotStepMinutes, int bufferMinutes, int minBookingNoticeMinutes, int bookingHorizonDays, int minCancellationNoticeMinutes)
    {
        AutoConfirm = autoConfirm;
        SlotStepMinutes = slotStepMinutes;
        BufferMinutes = bufferMinutes;
        MinBookingNoticeMinutes = minBookingNoticeMinutes;
        BookingHorizonDays = bookingHorizonDays;
        MinCancellationNoticeMinutes = minCancellationNoticeMinutes;
    }

    public static BookingPolicy Default { get; } = new(
        false,
        DefaultSlotStepMinutes,
        DefaultBufferMinutes,
        DefaultMinBookingNoticeMinutes,
        DefaultBookingHorizonDays,
        DefaultMinCancellationNoticeMinutes);

    public bool AutoConfirm { get; }

    public int SlotStepMinutes { get; }

    public int BufferMinutes { get; }

    public int MinBookingNoticeMinutes { get; }

    public int BookingHorizonDays { get; }

    public int MinCancellationNoticeMinutes { get; }

    public static BookingPolicy Create(bool autoConfirm, int slotStepMinutes, int bufferMinutes, int minBookingNoticeMinutes, int bookingHorizonDays, int minCancellationNoticeMinutes)
    {
        return new BookingPolicy(
            autoConfirm,
            Guard.Range(slotStepMinutes, "slotStepMinutes", ValidationLimits.SlotStepMin, ValidationLimits.SlotStepMax, ValidationLimits.MinutesMultiple),
            Guard.Range(bufferMinutes, "bufferMinutes", ValidationLimits.BufferMin, ValidationLimits.BufferMax, ValidationLimits.MinutesMultiple),
            Guard.Range(minBookingNoticeMinutes, "minBookingNoticeMinutes", ValidationLimits.NoticeMinutesMin, ValidationLimits.NoticeMinutesMax),
            Guard.Range(bookingHorizonDays, "bookingHorizonDays", ValidationLimits.HorizonDaysMin, ValidationLimits.HorizonDaysMax),
            Guard.Range(minCancellationNoticeMinutes, "minCancellationNoticeMinutes", ValidationLimits.NoticeMinutesMin, ValidationLimits.NoticeMinutesMax));
    }
}
