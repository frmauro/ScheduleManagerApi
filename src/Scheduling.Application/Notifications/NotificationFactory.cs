using System.Globalization;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Customers;
using Scheduling.Domain.Notifications;
using Scheduling.Domain.Providers;
using Scheduling.Domain.Users;

namespace Scheduling.Application.Notifications;

/// <summary>Monta as notificações (pt-BR) de cada evento de agendamento e define os destinatários (FR-025).</summary>
public static class NotificationFactory
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static IReadOnlyList<Notification> AppointmentCreated(Appointment a, Provider provider, Customer customer, DateTimeOffset now)
    {
        var when = Describe(a, provider);
        var list = new List<Notification>
        {
            Notification.Create(a.Id, customer.UserId, NotificationType.AppointmentCreated,
                $"Agendamento recebido: {a.Service.Name}",
                $"Olá, {customer.Name}! Seu agendamento de {a.Service.Name} com {provider.DisplayName} para {when} foi registrado com status {StatusLabel(a.Status)}.",
                now),
            Notification.Create(a.Id, provider.UserId, NotificationType.AppointmentCreated,
                $"Novo agendamento: {a.Service.Name}",
                $"{customer.Name} agendou {a.Service.Name} para {when}. Status: {StatusLabel(a.Status)}.",
                now),
        };

        if (a.Status == AppointmentStatus.Confirmed)
        {
            list.Add(Confirmed(a, provider, customer, now));
        }

        return list;
    }

    public static IReadOnlyList<Notification> AppointmentConfirmed(Appointment a, Provider provider, Customer customer, DateTimeOffset now) =>
        [Confirmed(a, provider, customer, now)];

    public static IReadOnlyList<Notification> AppointmentCancelled(Appointment a, Provider provider, Customer customer, Role actorRole, DateTimeOffset now)
    {
        var recipients = new HashSet<Guid>();
        if (a.Status == AppointmentStatus.CancelledByCustomer)
        {
            recipients.Add(provider.UserId);
        }
        else
        {
            recipients.Add(customer.UserId);
        }

        if (actorRole == Role.Admin)
        {
            recipients.Add(customer.UserId);
        }

        var who = a.Status == AppointmentStatus.CancelledByCustomer ? "pelo cliente" : "pelo prestador";
        var subject = $"Agendamento cancelado: {a.Service.Name}";
        var body = $"O agendamento de {a.Service.Name} com {provider.DisplayName} para {Describe(a, provider)} foi cancelado {who}. Motivo: {a.CancellationReason}.";

        return recipients.Select(r => Notification.Create(a.Id, r, NotificationType.AppointmentCancelled, subject, body, now)).ToList();
    }

    public static Notification AppointmentReminder(Appointment a, Provider provider, Customer customer, DateTimeOffset now) =>
        Notification.Create(a.Id, customer.UserId, NotificationType.AppointmentReminder,
            $"Lembrete: {a.Service.Name} amanhã",
            $"Olá, {customer.Name}! Lembrete do seu agendamento de {a.Service.Name} com {provider.DisplayName} em {Describe(a, provider)}. Endereço: {provider.Address.Street}, {provider.Address.Number} — {provider.Address.City}/{provider.Address.State}.",
            now);

    private static Notification Confirmed(Appointment a, Provider provider, Customer customer, DateTimeOffset now) =>
        Notification.Create(a.Id, customer.UserId, NotificationType.AppointmentConfirmed,
            $"Agendamento confirmado: {a.Service.Name}",
            $"Olá, {customer.Name}! {provider.DisplayName} confirmou seu agendamento de {a.Service.Name} para {Describe(a, provider)}.",
            now);

    private static string Describe(Appointment a, Provider provider)
    {
        var local = TimeZoneInfo.ConvertTime(a.StartAt, provider.GetTimeZone());
        return $"{local.ToString("dd/MM/yyyy 'às' HH:mm", PtBr)} (UTC{local.ToString("zzz", PtBr)})";
    }

    private static string StatusLabel(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => "pendente de confirmação",
        AppointmentStatus.Confirmed => "confirmado",
        AppointmentStatus.CancelledByCustomer => "cancelado pelo cliente",
        AppointmentStatus.CancelledByProvider => "cancelado pelo prestador",
        AppointmentStatus.Completed => "concluído",
        _ => status.ToString(),
    };
}
