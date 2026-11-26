namespace Scheduling.Domain.Common;

/// <summary>Limites de tamanho/intervalo compartilhados entre domínio, validators e mapeamento EF (data-model.md).</summary>
public static class ValidationLimits
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 120;
    public const int EmailMaxLength = 254;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int PasswordHashMaxLength = 512;
    public const int PhoneMaxLength = 20;

    public const int ProviderDescriptionMaxLength = 2000;
    public const int TimeZoneIdMaxLength = 64;
    public const string DefaultTimeZoneId = "America/Sao_Paulo";

    public const int StreetMaxLength = 200;
    public const int NumberMaxLength = 20;
    public const int ComplementMaxLength = 100;
    public const int DistrictMaxLength = 100;
    public const int CityMaxLength = 100;
    public const int StateLength = 2;
    public const int PostalCodeLength = 8;

    public const int ServiceNameMinLength = 2;
    public const int ServiceNameMaxLength = 100;
    public const int ServiceDescriptionMaxLength = 1000;
    public const int ServiceDurationMin = 5;
    public const int ServiceDurationMax = 720;
    public const int ServiceDurationMultiple = 5;
    public const decimal ServicePriceMin = 0m;
    public const decimal ServicePriceMax = 99_999.99m;

    public const int SlotStepMin = 5;
    public const int SlotStepMax = 60;
    public const int BufferMin = 0;
    public const int BufferMax = 120;
    public const int MinutesMultiple = 5;
    public const int NoticeMinutesMin = 0;
    public const int NoticeMinutesMax = 10_080;
    public const int HorizonDaysMin = 1;
    public const int HorizonDaysMax = 365;

    public const int MaxIntervalsPerDay = 10;
    public const int ExceptionMaxPeriodDays = 366;
    public const int ExceptionReasonMaxLength = 200;

    public const int NotesMaxLength = 500;
    public const int CancellationReasonMinLength = 3;
    public const int CancellationReasonMaxLength = 500;

    public const int QueryTextMaxLength = 100;
    public const int MaxPeriodDays = 366;
}
