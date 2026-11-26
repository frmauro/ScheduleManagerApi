# Data Model: API de Agendamento Online

**Feature**: `001-online-scheduling-api` | **Date**: 2026-11-09 | **Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Convenções: tabelas e colunas em `snake_case`; chaves primárias `uuid` (UUID v7 gerado na aplicação via `Guid.CreateVersion7()`); instantes em `timestamptz` (UTC); horas de parede em `time`; datas em `date`; dinheiro em `numeric(10,2)`; enums como `text`. Todas as tabelas têm `created_at` e `updated_at`.

## Diagrama de relacionamentos

```text
User 1──0..1 Provider 1──* Service
  │              │ 1──* WorkingHours
  │              │ 1──* ScheduleException
  │              │ 1──* Appointment *──1 Customer 0..1──1 User
  │                         │ *──1 Service
  │                         │ 1──* AppointmentStatusChange
  │                         │ 1──* Notification *──1 User (destinatário)
```

## Agregados (DDD leve)

| Agregado (raiz) | Entidades internas | Observação |
|---|---|---|
| `User` | — | Identidade e credencial |
| `Provider` | `BookingPolicy` (value object), `Address` (VO), `WorkingHours`, `ScheduleException` | Expediente = parte do agregado Provider ("Schedule" lógico) |
| `Customer` | — | Perfil do cliente |
| `Service` | — | Raiz própria (referencia `ProviderId`) para não carregar o catálogo inteiro |
| `Appointment` | `ServiceSnapshot` (VO), `AppointmentStatusChange` | Máquina de estados |
| `Notification` | — | Outbox de comunicação |

---

## User — `users`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | uuid PK | |
| `name` | varchar(120) | obrigatório, 2–120 |
| `email` | varchar(254) | obrigatório, formato e-mail, armazenado normalizado (lower-case); **único** |
| `password_hash` | varchar(512) | nunca retornado |
| `role` | text | `Admin` \| `Provider` \| `Customer` |
| `is_active` | boolean | padrão `true`; inativo não autentica |
| `created_at` / `updated_at` | timestamptz | |

Índices: `ux_users_email (email)`.
Regras: senha ≥ 8 caracteres com ao menos uma letra e um dígito (validado antes do hash); `Admin` só via seed/provisionamento (FR-011).

## Provider — `providers`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | uuid PK | |
| `user_id` | uuid FK → users | **único** (1:1) |
| `display_name` | varchar(120) | obrigatório |
| `description` | varchar(2000) | opcional |
| `category` | text | enum `ServiceCategory`: `Beauty`, `Barbershop`, `Health`, `Dental`, `Therapy`, `Fitness`, `Wellness`, `Education`, `Consulting`, `Other` |
| `contact_email` | varchar(254) | obrigatório |
| `contact_phone` | varchar(20) | obrigatório, E.164 (`+5511999998888`) |
| `address_*` (VO `Address`) | `street` varchar(200), `number` varchar(20), `complement` varchar(100)?, `district` varchar(100), `city` varchar(100), `state` char(2), `postal_code` char(8) | todos obrigatórios exceto complemento; CEP 8 dígitos |
| `time_zone_id` | varchar(64) | IANA válido (`TimeZoneInfo` resolve); padrão `America/Sao_Paulo` |
| `auto_confirm` | boolean | padrão `false` (FR-019) |
| `slot_step_minutes` | smallint | 5–60, múltiplo de 5; padrão 15 |
| `buffer_minutes` | smallint | 0–120, múltiplo de 5; padrão 0 |
| `min_booking_notice_minutes` | int | 0–10080; padrão 60 |
| `booking_horizon_days` | smallint | 1–365; padrão 60 |
| `min_cancellation_notice_minutes` | int | 0–10080; padrão 120 |
| `is_active` | boolean | espelha `users.is_active` para buscas |
| `created_at` / `updated_at` | timestamptz | |

Índices: `ux_providers_user_id`, `ix_providers_category_city (category, address_city) WHERE is_active`, `ix_providers_display_name` (busca `ILIKE`).
Os campos `auto_confirm … min_cancellation_notice_minutes` compõem o VO `BookingPolicy`.

## WorkingHours — `provider_working_hours`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | uuid PK | |
| `provider_id` | uuid FK → providers (cascade) | |
| `day_of_week` | smallint | 0 (Domingo) – 6 (Sábado) |
| `start_time` | time | `start_time < end_time` |
| `end_time` | time | sem faixa atravessando meia-noite |

Índices: `ix_working_hours_provider_day (provider_id, day_of_week)`.
Invariantes (validadas no agregado Provider, FR-003): faixas do mesmo dia não se sobrepõem (adjacentes permitidas); duração mínima da faixa ≥ `slot_step_minutes`; máx. 10 faixas/dia. A atualização substitui o conjunto semanal inteiro (operação `PUT`).

## ScheduleException — `provider_schedule_exceptions`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | uuid PK | |
| `provider_id` | uuid FK → providers (cascade) | |
| `type` | text | `Closure` \| `SpecialHours` |
| `start_date` | date | |
| `end_date` | date | `end_date >= start_date`; período ≤ 366 dias |
| `start_time` | time? | `Closure`: ambos nulos = dia inteiro; ambos preenchidos = fechamento parcial. `SpecialHours`: obrigatórios |
| `end_time` | time? | `start_time < end_time` |
| `reason` | varchar(200) | opcional (ex.: "Natal", "Férias") |

Índices: `ix_exceptions_provider_dates (provider_id, start_date, end_date)`.
Regras: para uma mesma data, várias `SpecialHours` formam o conjunto de faixas do dia (não podem se sobrepor entre si); `Closure` sempre recorta (R4). Datas no passado não podem ser criadas.

## Customer — `customers`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | uuid PK | |
| `user_id` | uuid FK → users | **único** |
| `name` | varchar(120) | sincronizado com `users.name` |
| `email` | varchar(254) | sincronizado com `users.email` |
| `phone` | varchar(20) | obrigatório, E.164 |
| `created_at` / `updated_at` | timestamptz | |

## Service — `services`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | uuid PK | |
| `provider_id` | uuid FK → providers | |
| `name` | varchar(100) | obrigatório; **único por prestador** (case-insensitive) |
| `description` | varchar(1000) | opcional |
| `duration_minutes` | smallint | 5–720, múltiplo de 5 |
| `price` | numeric(10,2) | 0 – 99 999,99 (BRL) |
| `is_active` | boolean | padrão `true` |
| `created_at` / `updated_at` | timestamptz | |

Índices: `ux_services_provider_name (provider_id, lower(name))`.
Regras: desativar não afeta agendamentos existentes (snapshot); exclusão física só se nunca houve agendamento, senão 409 (`service.in_use`).

## Appointment — `appointments`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | uuid PK | |
| `provider_id` | uuid FK → providers | |
| `customer_id` | uuid FK → customers | |
| `service_id` | uuid FK → services | |
| `service_name` | varchar(100) | snapshot (FR-020) |
| `service_duration_minutes` | smallint | snapshot |
| `service_price` | numeric(10,2) | snapshot |
| `start_at` | timestamptz | alinhado à grade (R11) |
| `end_at` | timestamptz | `= start_at + duration`; `end_at > start_at` |
| `blocked_until` | timestamptz | `= end_at + buffer` (snapshot do preparo) |
| `status` | text | `Pending` \| `Confirmed` \| `CancelledByCustomer` \| `CancelledByProvider` \| `Completed` |
| `customer_notes` | varchar(500) | opcional |
| `cancellation_reason` | varchar(500) | obrigatório quando cancelado |
| `idempotency_key` | uuid? | R-03 |
| `xmin` | xid (sistema) | concurrency token (R-02) |
| `created_at` / `updated_at` | timestamptz | |

Índices e constraints:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE appointments ADD CONSTRAINT ck_appointments_time CHECK (end_at > start_at AND blocked_until >= end_at);

ALTER TABLE appointments ADD CONSTRAINT ex_appointments_provider_overlap
  EXCLUDE USING gist (provider_id WITH =, tstzrange(start_at, blocked_until, '[)') WITH &&)
  WHERE (status IN ('Pending', 'Confirmed'));

ALTER TABLE appointments ADD CONSTRAINT ex_appointments_customer_overlap
  EXCLUDE USING gist (customer_id WITH =, tstzrange(start_at, end_at, '[)') WITH &&)
  WHERE (status IN ('Pending', 'Confirmed'));

CREATE UNIQUE INDEX ux_appointments_idempotency ON appointments (customer_id, idempotency_key)
  WHERE idempotency_key IS NOT NULL;
CREATE INDEX ix_appointments_customer_start ON appointments (customer_id, start_at DESC);
CREATE INDEX ix_appointments_status_start ON appointments (status, start_at); -- lembretes
```

> **Nota sobre o preparo (buffer)**: a constraint do prestador compara `[start_at, blocked_until)` de ambos os lados, logo o preparo do agendamento anterior bloqueia o início do seguinte — coerente com R6/R7. O calculador de disponibilidade usa `[t, t + d + b)` para o candidato, equivalente ao que será gravado.

### Máquina de estados

```text
            confirm (Provider/Admin)
 Pending ─────────────────────────▶ Confirmed ──complete (Provider/Admin, now ≥ start_at)──▶ Completed
    │                                   │
    ├─ cancel (Customer*, Admin) ──▶ CancelledByCustomer ◀── cancel (Customer*, Admin) ──┤
    └─ cancel (Provider, Admin)  ──▶ CancelledByProvider ◀── cancel (Provider, Admin)  ──┘

 * Customer: somente se now ≤ start_at − min_cancellation_notice_minutes (422 appointment.cancellation_window)
 Estados finais: CancelledByCustomer, CancelledByProvider, Completed → qualquer transição = 422 appointment.invalid_transition
 Cancelar após start_at: proibido para todos (422); Admin cancela informando onBehalfOf = Customer | Provider.
```

Cada transição: incrementa versão (xmin), grava `AppointmentStatusChange` e enfileira `Notification` (na mesma transação).

## AppointmentStatusChange — `appointment_status_changes`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | uuid PK | |
| `appointment_id` | uuid FK → appointments (cascade) | |
| `from_status` | text? | nulo na criação |
| `to_status` | text | |
| `changed_by_user_id` | uuid FK → users | |
| `changed_by_role` | text | |
| `reason` | varchar(500)? | |
| `changed_at` | timestamptz | |

## Notification — `notifications`

| Campo | Tipo | Regras |
|---|---|---|
| `id` | uuid PK | |
| `appointment_id` | uuid FK → appointments | |
| `recipient_user_id` | uuid FK → users | |
| `channel` | text | `Email` (simulado) — extensível (`Sms`, `WhatsApp`) |
| `type` | text | `AppointmentCreated` \| `AppointmentConfirmed` \| `AppointmentCancelled` \| `AppointmentReminder` |
| `subject` | varchar(200) | |
| `body` | text | conteúdo renderizado (pt-BR) |
| `status` | text | `Pending` \| `Sent` \| `Failed` \| `Cancelled` |
| `attempts` | smallint | 0–3 |
| `scheduled_for` | timestamptz | quando pode ser enviada |
| `next_attempt_at` | timestamptz? | backoff |
| `sent_at` | timestamptz? | |
| `last_error` | varchar(1000)? | |
| `created_at` | timestamptz | |

Índices:

```sql
CREATE UNIQUE INDEX ux_notifications_reminder ON notifications (appointment_id, recipient_user_id)
  WHERE type = 'AppointmentReminder';
CREATE INDEX ix_notifications_dispatch ON notifications (status, next_attempt_at) WHERE status = 'Pending';
CREATE INDEX ix_notifications_recipient ON notifications (recipient_user_id, created_at DESC);
```

Destinatários por evento: `Created` → cliente + prestador; `Confirmed` → cliente; `Cancelled` → a outra parte (e o cliente quando Admin cancela); `Reminder` → cliente.

---

## Mapeamento Spec → Modelo

| Spec | Onde |
|---|---|
| FR-001, FR-005 | `providers` (+ VO `BookingPolicy`, `Address`) |
| FR-002 | `services` |
| FR-003, FR-004 | `provider_working_hours`, `provider_schedule_exceptions` |
| FR-017, FR-018 | `ex_appointments_provider_overlap`, `ex_appointments_customer_overlap` |
| FR-020 | `appointments.service_*` |
| FR-021 | máquina de estados + `appointment_status_changes` |
| FR-022 | `xmin` |
| FR-023 | `ux_appointments_idempotency` |
| FR-025–FR-027 | `notifications` |
