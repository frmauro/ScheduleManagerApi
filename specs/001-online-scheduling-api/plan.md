# Implementation Plan: API de Agendamento Online (ScheduleManager)

**Branch**: `001-online-scheduling-api` | **Date**: 2026-11-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-online-scheduling-api/spec.md`

## Summary

Web API REST (v1) de agendamento para um marketplace de prestadores individuais (FR-032/FR-033): prestadores configuram perfil, serviços, expediente semanal e exceções; clientes consultam horários livres e reservam; o ciclo de vida do agendamento segue uma máquina de estados auditada; notificações (simuladas) e lembretes de 24h são entregues por um outbox processado em background.

Abordagem técnica: Clean Architecture em 4 projetos (.NET 10). O cálculo de disponibilidade é um **serviço de domínio puro** (`AvailabilityCalculator`) reutilizado na consulta e na criação (SC-004). A ausência de overbooking é garantida pelo **PostgreSQL com exclusion constraints GiST** sobre `tstzrange` (R-01), complementada por concorrência otimista via `xmin` nas transições de status (R-02) e `Idempotency-Key` na criação (R-03). Erros em RFC 7807 com `code` estável; contrato completo em [contracts/openapi.yaml](./contracts/openapi.yaml).

## Technical Context

**Language/Version**: C# 14 / .NET 10.0 (`<Nullable>enable</Nullable>`, `TreatWarningsAsErrors`)

**Primary Dependencies**: ASP.NET Core 10 (Controllers), EF Core 10 + Npgsql provider, FluentValidation 12.1+, JwtBearer 10, Microsoft.AspNetCore.OpenApi 10 + Swashbuckle SwaggerUI, `PasswordHasher<T>` do shared framework ASP.NET Core — justificativas em [research.md](./research.md#dependências-nuget-justificativa-exigida-pela-constituição)

**Storage**: PostgreSQL 16+ (extensão `btree_gist`), migrations EF Core — conexão de desenvolvimento definida em [Configuração do Banco de Dados](#configuração-do-banco-de-dados)

**Testing**: xUnit v3 (Microsoft.Testing.Platform), NSubstitute, FluentValidation.TestHelper, `FakeTimeProvider`; integração com `WebApplicationFactory` + PostgreSQL real em banco descartável por execução (sem Docker — ver research R-14)

**Target Platform**: Linux container / Windows (Kestrel), ambiente stateless escalável horizontalmente

**Project Type**: web-service (API REST)

**Performance Goals**: disponibilidade p95 < 1 s com 500 agendamentos futuros por prestador (SC-002); criação de agendamento p95 < 500 ms; lembretes ≤ 15 min após o marco (SC-005)

**Constraints**: zero overbooking sob concorrência (SC-001); instantes com offset; sem stack trace em produção; páginas ≤ 100 itens; JWT 60 min

**Scale/Scope**: v1 para ~1 000 prestadores, ~50 000 clientes, ~200 000 agendamentos/ano; 34 operações HTTP; 5 user stories

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio | Verificação | Status |
|---|---|---|
| I. SDD | Spec aprovada com clarificações resolvidas; cada endpoint do contrato referencia FRs; tasks derivarão deste plano | ✅ |
| II. Clean Architecture | `Domain` sem dependências (nem EF, nem ASP.NET); `Application` → `Domain` (interfaces de repositório/UoW, validators, DTOs); `Infrastructure` implementa (EF, Npgsql, JWT, hasher, background services); `Api` compõe. FluentValidation é a única lib externa na Application (exigida pela Constituição IV) | ✅ |
| III. Type Safety | Nullable + warnings como erros; enums (`AppointmentStatus`, `Role`, `ServiceCategory`…), VOs (`BookingPolicy`, `Address`, `TimeSlot`, `ServiceSnapshot`), códigos de erro em classe de constantes `ErrorCodes`; opções fortemente tipadas (`JwtOptions`, `NotificationOptions`) | ✅ |
| IV. Validação/Erros | Validators `AbstractValidator<T>` por request; `IExceptionHandler` global → Problem Details; stack trace só em Development | ✅ |
| V. Testes | Unit + Integration por story, com banco PostgreSQL real descartável para provar constraints; `dotnet build` + `dotnet test` como gate (ver [quickstart.md](./quickstart.md)) | ✅ |
| Tech Stack | .NET 10, EF Core 10, FluentValidation 12.1+, JwtBearer 10, OpenApi 10 ✅. Novos pacotes listados e justificados em research.md | ✅ (justificado) |
| Convenções | Async + `CancellationToken` em todo I/O; PascalCase/`_camelCase`; tudo em `src/` | ✅ |

**Re-check pós-design (Phase 1)**: ✅ sem violações. Pontos de atenção registrados:
- O SQL das exclusion constraints vive em migrations da Infrastructure — o Domain apenas expressa a invariante e a Application traduz a exceção de conflito (sem vazar `PostgresException`).
- `TimeProvider` (BCL) é usado no Domain/Application como abstração de relógio — tipo do runtime, não framework externo.

## Project Structure

### Documentation (this feature)

```text
specs/001-online-scheduling-api/
├── plan.md              # Este arquivo
├── research.md          # Phase 0 — decisões R-01…R-14
├── data-model.md        # Phase 1 — tabelas, constraints, máquina de estados
├── quickstart.md        # Phase 1 — setup e roteiro de validação
├── contracts/
│   └── openapi.yaml     # Phase 1 — contrato REST v1 (OpenAPI 3.1)
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── ScheduleManager.slnx
├── Directory.Build.props                 # net10.0, Nullable, TreatWarningsAsErrors, ImplicitUsings
├── Directory.Packages.props              # Central Package Management
├── Scheduling.Domain/
│   ├── Common/                           # Entity, AggregateRoot, DomainException, ErrorCodes
│   ├── Users/                            # User, Role
│   ├── Providers/                        # Provider, BookingPolicy, Address, ServiceCategory,
│   │                                     # WorkingHours, ScheduleException
│   ├── Customers/                        # Customer
│   ├── Services/                         # Service
│   ├── Appointments/                     # Appointment, AppointmentStatus, ServiceSnapshot,
│   │                                     # AppointmentStatusChange (máquina de estados)
│   ├── Availability/                     # AvailabilityCalculator, TimeSlot, DaySchedule
│   └── Notifications/                    # Notification, NotificationType/Status
├── Scheduling.Application/
│   ├── Abstractions/                     # IRepositories, IUnitOfWork, ICurrentUser,
│   │                                     # IPasswordHasher, ITokenService, INotificationChannel
│   ├── Common/                           # PagedResult, PageRequest, exceções de aplicação
│   ├── Auth/  Customers/  Providers/  Services/  Schedule/
│   ├── Availability/                     # AvailabilityService
│   ├── Appointments/                     # AppointmentService (create/confirm/cancel/complete)
│   ├── Notifications/                    # NotificationFactory (templates pt-BR)
│   └── DependencyInjection.cs
├── Scheduling.Infrastructure/
│   ├── Persistence/                      # SchedulingDbContext, Configurations/, Migrations/,
│   │                                     # Repositories/, PostgresErrorTranslator
│   ├── Identity/                         # JwtTokenService, PasswordHasherAdapter
│   ├── Notifications/                    # SimulatedNotificationChannel, ReminderScheduler,
│   │                                     # NotificationDispatcher (BackgroundServices)
│   ├── Seed/                             # AdminSeeder
│   └── DependencyInjection.cs
├── Scheduling.Api/
│   ├── Controllers/                      # Auth, Customers, Providers, Services, Schedule,
│   │                                     # Availability, Appointments, Notifications, Admin
│   ├── ErrorHandling/                    # GlobalExceptionHandler, ProblemDetails mapping
│   ├── Auth/                             # Policies, CurrentUser (claims)
│   ├── OpenApi/                          # transformers (bearer, exemplos)
│   └── Program.cs
├── Scheduling.UnitTests/                 # Domain/, Application/, Validators/
└── Scheduling.IntegrationTests/          # Fixtures/ (PostgresContainer, ApiFactory, AuthHelper),
                                          # Endpoints/, Concurrency/, Notifications/
```

**Structure Decision**: projeto único de backend com 4 camadas da Constituição (`Scheduling.Domain`, `.Application`, `.Infrastructure`, `.Api`) e 2 projetos de teste, todos sob `src/` (regra de diretório de saída da Constituição). Referências: `Application → Domain`; `Infrastructure → Application, Domain`; `Api → Application, Infrastructure`; `UnitTests → Domain, Application`; `IntegrationTests → Api`.

## Configuração do Banco de Dados

Conexão de **desenvolvimento local** em `src/Scheduling.Api/appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123"
  }
}
```

- **Nome da chave**: `DefaultConnection` — lida em `Scheduling.Infrastructure/DependencyInjection.cs` via `configuration.GetConnectionString("DefaultConnection")` e passada a `UseNpgsql(...)`. Ausência da chave → falha explícita na inicialização (`InvalidOperationException`), sem fallback silencioso.
- **Migrations**: `dotnet ef database update` aplica o schema nesse banco (inclui `CREATE EXTENSION IF NOT EXISTS btree_gist`; o usuário `postgres` tem permissão para criá-la).
- **Outros ambientes**: a senha `123` é apenas para desenvolvimento local. Em Staging/Produção a mesma chave é fornecida por variável de ambiente `ConnectionStrings__DefaultConnection` (ou cofre de segredos) e **não** é versionada; `appsettings.json` base não contém connection string.
- **Testes de integração**: **não** usam esta conexão. O `ApiFactory` sobrescreve `ConnectionStrings:DefaultConnection` com um banco descartável `schedulemanager_it_<guid>` criado no mesmo servidor (configurável por `SCHEDULEMANAGER_TEST_DB`) e removido ao final, evitando que testes apaguem ou poluam o banco `postgres` local.

## Design Highlights

### Fluxo de criação de agendamento (R-01, R-03, R-05)

1. Controller → `CreateAppointmentRequestValidator` (formato, offset presente, notas ≤ 500).
2. `AppointmentService.CreateAsync`: se `Idempotency-Key` já existe para o cliente → retorna original (200) ou 422.
3. Carrega Provider (+ política, fuso, expediente, exceções da data), Service, ocupações do dia (`Pending`/`Confirmed`).
4. `AvailabilityCalculator.EnsureBookable(start, …)` → 422 com motivo específico (fora do expediente, desalinhado, antecedência, horizonte, inativo) ou 409 se já ocupado.
5. `Appointment.Create(...)` (snapshot, `blocked_until`, status inicial por `autoConfirm`), `AppointmentStatusChange` inicial e `Notification`s `Created` (+ `Confirmed` se auto) na **mesma transação**.
6. Passos 3 (ocupações) a 6 rodam em `ExecuteSerializedAsync(providerId)`: transação com `pg_advisory_xact_lock` por prestador — o perdedor de uma disputa relê a ocupação já confirmada e recebe 409 `slot_unavailable`.
7. `SaveChangesAsync` → violação `23P01` (garantia final) traduzida para 409 (`slot_unavailable` ou `customer_overlap` pelo nome da constraint). Nenhum retry automático.

### Mapeamento User Stories → componentes

| Story | Componentes principais |
|---|---|
| US1 (P1) Disponibilidade + reserva | `AvailabilityCalculator`, `AvailabilityService`, `AppointmentService.Create`, constraints GiST |
| US2 (P1) Prestador/serviços/expediente | `Provider` (agregado + Schedule), `Service`, `ScheduleService` (affected appointments) |
| US3 (P2) Ciclo de vida | `Appointment` (máquina de estados), `AppointmentService.Confirm/Cancel/Complete`, `xmin`/ETag |
| US4 (P2) Auth/perfis | `AuthService`, `JwtTokenService`, policies, `AdminSeeder`, histórico do cliente |
| US5 (P3) Notificações | `NotificationFactory`, outbox, `ReminderScheduler`, `NotificationDispatcher`, `SimulatedNotificationChannel` |

### Ordem sugerida de implementação (módulo por módulo)

1. **Fundação**: solução, camadas, Problem Details, paginação, DbContext, fixtures de teste (banco descartável + ApiFactory).
2. **Auth & Usuários** (US4 parcial — necessário para todas as rotas protegidas).
3. **Prestadores, Serviços e Expediente** (US2).
4. **Motor de Agendamento** (US1) — calculador + criação + concorrência. *MVP aqui.*
5. **Ciclo de vida** (US3).
6. **Histórico/Admin** (restante de US4).
7. **Notificações** (US5).

## Complexity Tracking

> Nenhuma violação da Constituição. Itens abaixo registram escolhas não-triviais para revisão.

| Escolha | Por que é necessária | Alternativa mais simples rejeitada porque |
|---|---|---|
| Exclusion constraints GiST (SQL específico de PostgreSQL) | Única garantia atômica contra sobreposição parcial multi-instância (SC-001) | Índice único só detecta inícios iguais; locks serializam reservas não conflitantes |
| Banco PostgreSQL real (descartável) nos testes de integração | Provar constraints/concorrência em banco real (Constituição V) | SQLite/InMemory não suportam `EXCLUDE`/`tstzrange` |
| Advisory lock por prestador na criação (`ExecuteSerializedAsync`) | Com exclusion constraint apenas, inserções concorrentes conflitantes podem ser **ambas** abortadas pelo PostgreSQL — viola “exatamente um vence” (SC-001) | Retry automático: não determinístico; serializar só o mesmo prestador tem custo baixo |
| Background services (lembrete + dispatcher) | Lembretes 24h e isolamento de falhas de envio (FR-026/027) | Envio síncrono acopla falha externa à reserva e não cobre lembretes |
