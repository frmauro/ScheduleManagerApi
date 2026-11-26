---

description: "Task list for 001-online-scheduling-api"
---

# Tasks: API de Agendamento Online (ScheduleManager)

**Input**: Design documents from `/specs/001-online-scheduling-api/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/openapi.yaml](./contracts/openapi.yaml), [quickstart.md](./quickstart.md)

**Tests**: **OBRIGATÓRIOS** — Constituição, Princípio V exige testes unitários **e** de integração na mesma entrega. Em cada story os testes vêm primeiro e DEVEM falhar antes da implementação.

**Organization**: tarefas agrupadas por user story. Ordem de execução das stories: **US2 → US1** (ambas P1; US1 consome prestador/serviço/expediente de US2) → US3 → US4 → US5.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: US1…US5 conforme spec.md
- Todos os caminhos são relativos à raiz do repositório; todo código fica em `src/` (Constituição)

## Convenções aplicáveis a TODAS as tarefas

- Todo método de I/O é `async`, sufixo `Async`, com `CancellationToken cancellationToken = default` propagado.
- Códigos de erro vêm de `src/Scheduling.Domain/Common/ErrorCodes.cs` (nunca strings soltas).
- Instantes: `DateTimeOffset` UTC na persistência; respostas convertidas ao fuso do prestador com offset (FR-031).
- Status/codes HTTP e `code` de erro conforme a tabela de [research.md R-07](./research.md#r-07--erros-fr-028-constituição-iv) e o contrato.
- Rotas sob o prefixo `api/v1` e formatos exatamente como em `contracts/openapi.yaml`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: solução, projetos e configuração base

- [X] T001 Create solution `src/ScheduleManager.slnx` with projects `src/Scheduling.Domain` (classlib), `src/Scheduling.Application` (classlib), `src/Scheduling.Infrastructure` (classlib), `src/Scheduling.Api` (webapi, controllers), `src/Scheduling.UnitTests` (xunit v3), `src/Scheduling.IntegrationTests` (xunit v3) and project references exactly: Application→Domain; Infrastructure→Application, Domain; Api→Application, Infrastructure; UnitTests→Domain, Application; IntegrationTests→Api
- [X] T002 Create `src/Directory.Build.props` with `TargetFramework=net10.0`, `LangVersion=14`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `GenerateDocumentationFile=true` (suppress CS1591 only in test projects)
- [X] T003 Create `src/Directory.Packages.props` (Central Package Management) with packages from research.md "Dependências NuGet": Npgsql.EntityFrameworkCore.PostgreSQL 10.x, Microsoft.EntityFrameworkCore.Design 10.x, FluentValidation + FluentValidation.DependencyInjectionExtensions 12.1+, Microsoft.AspNetCore.Authentication.JwtBearer 10.x, Microsoft.AspNetCore.OpenApi 10.x, Microsoft.Extensions.Identity.Core 10.x, Swashbuckle.AspNetCore.SwaggerUI, xunit.v3, Microsoft.NET.Test.Sdk, NSubstitute, Microsoft.Extensions.TimeProvider.Testing, Microsoft.AspNetCore.Mvc.Testing 10.x, Testcontainers.PostgreSql; add PackageReferences to each project per research table (Domain: none)
- [X] T004 [P] Create `src/.editorconfig` enforcing PascalCase types/members, `I` prefix for interfaces, `_camelCase` private fields, `camelCase` locals/parameters, file-scoped namespaces
- [X] T005 [P] Create `src/Scheduling.Api/appsettings.json` (Logging, `Jwt` section with `Issuer`, `Audience`, `AccessTokenMinutes: 60`, no signing key, no connection string, `Notifications` section with `ReminderIntervalSeconds: 60`, `DispatchIntervalSeconds: 15`, `MaxAttempts: 3`) and `src/Scheduling.Api/appsettings.Development.json` with `"ConnectionStrings": { "DefaultConnection": "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123" }` (plan.md › Configuração do Banco de Dados)
- [X] T006 [P] Create `src/global.json` pinning .NET SDK 10.0 (`rollForward: latestFeature`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: núcleo de domínio, persistência, erros RFC 7807, autenticação (registro/login) e infraestrutura de testes — bloqueia TODAS as stories

**⚠️ CRITICAL**: nenhuma story começa antes do fim desta fase

### Domain base

- [X] T007 [P] Create `Entity` (Guid `Id` via `Guid.CreateVersion7()`, `CreatedAt`, `UpdatedAt`) and `AggregateRoot` in `src/Scheduling.Domain/Common/Entity.cs`
- [X] T008 [P] Create `DomainException` (carries `Code`) and static class `ErrorCodes` with all codes: `validation.failed`, `auth.unauthenticated`, `auth.invalid_credentials`, `auth.forbidden`, `user.email_taken`, `user.not_found`, `provider.not_found`, `provider.inactive`, `service.not_found`, `service.inactive`, `service.name_taken`, `service.in_use`, `schedule.exception_in_past`, `schedule.special_hours_overlap`, `schedule.exception_not_found`, `availability.date_in_past`, `availability.beyond_horizon`, `availability.outside_working_hours`, `availability.closed`, `availability.misaligned_start`, `availability.too_soon`, `appointment.not_found`, `appointment.slot_unavailable`, `appointment.customer_overlap`, `appointment.concurrency_conflict`, `appointment.precondition_failed`, `appointment.invalid_transition`, `appointment.cancellation_window`, `appointment.already_started`, `appointment.not_started`, `idempotency.key_reused`, `admin.cannot_deactivate_self` in `src/Scheduling.Domain/Common/DomainException.cs` and `src/Scheduling.Domain/Common/ErrorCodes.cs`
- [X] T009 [P] Create `Role` enum (`Admin`, `Provider`, `Customer`) and `User` entity in `src/Scheduling.Domain/Users/Role.cs` and `src/Scheduling.Domain/Users/User.cs`: `name` "obrigatório, 2–120"; `email` "obrigatório, formato e-mail, armazenado normalizado (lower-case); único" (max 254); `password_hash` "nunca retornado" (max 512); `is_active` "padrão `true`; inativo não autentica"; methods `Activate()`, `Deactivate()`, `UpdateProfile(name, email)`
- [X] T010 [P] Create `Customer` entity in `src/Scheduling.Domain/Customers/Customer.cs`: `user_id` único (1:1), `name` varchar(120) "sincronizado com `users.name`", `email` varchar(254) "sincronizado com `users.email`", `phone` "obrigatório, E.164" (regex `^\+[1-9]\d{7,14}$`, max 20)
- [X] T011 [P] Create value objects/enums in `src/Scheduling.Domain/Providers/`: `ServiceCategory.cs` (`Beauty`, `Barbershop`, `Health`, `Dental`, `Therapy`, `Fitness`, `Wellness`, `Education`, `Consulting`, `Other`); `Address.cs` (`street` 200, `number` 20, `complement` 100 opcional, `district` 100, `city` 100, `state` char(2) `^[A-Z]{2}$`, `postalCode` "CEP 8 dígitos" — todos obrigatórios exceto complemento); `BookingPolicy.cs` with defaults and guards: `autoConfirm` padrão `false`; `slotStepMinutes` "5–60, múltiplo de 5; padrão 15"; `bufferMinutes` "0–120, múltiplo de 5; padrão 0"; `minBookingNoticeMinutes` "0–10080; padrão 60"; `bookingHorizonDays` "1–365; padrão 60"; `minCancellationNoticeMinutes` "0–10080; padrão 120"
- [X] T012 Create `Provider` aggregate root (profile part only) in `src/Scheduling.Domain/Providers/Provider.cs`: `UserId` único, `DisplayName` obrigatório max 120, `Description` max 2000 opcional, `Category`, `ContactEmail` obrigatório max 254, `ContactPhone` E.164, `Address`, `TimeZoneId` "IANA válido (`TimeZoneInfo` resolve); padrão `America/Sao_Paulo`", `BookingPolicy` (defaults), `IsActive`; methods `UpdateProfile(...)`, `UpdateBookingPolicy(...)`, `Activate()`, `Deactivate()`, `GetTimeZone()` (depends on T011)

### Application base

- [X] T013 [P] Create `PagedResult<T>` (`items`, `page`, `pageSize`, `totalCount`, `totalPages`), `PageRequest` (`page` ≥1 default 1, `pageSize` 1–100 default 20) and `PageRequestValidator` in `src/Scheduling.Application/Common/Paging/`
- [X] T014 [P] Create application exceptions `NotFoundException`, `ConflictException`, `ForbiddenException`, `BusinessRuleException`, `PreconditionFailedException`, `UnauthenticatedException` (all carry `ErrorCodes` code + pt-BR detail) in `src/Scheduling.Application/Common/Exceptions/`
- [X] T015 [P] Create abstractions `ICurrentUser` (UserId, Role, ProviderId?, CustomerId?), `IPasswordHasher`, `ITokenService`, `IUnitOfWork`, `IUserRepository`, `ICustomerRepository`, `IProviderRepository` in `src/Scheduling.Application/Abstractions/`
- [X] T016 Create `src/Scheduling.Application/DependencyInjection.cs` (`AddApplication()`: registers validators from assembly and application services; `TimeProvider` resolved from DI)

### Infrastructure base

- [X] T017 Create `SchedulingDbContext` (implements `IUnitOfWork`, snake_case table/column names via configurations, enums as `text`) in `src/Scheduling.Infrastructure/Persistence/SchedulingDbContext.cs`
- [X] T018 [P] Create EF configurations `UserConfiguration`, `CustomerConfiguration`, `ProviderConfiguration` (owned `Address` → `address_*` columns, owned `BookingPolicy` columns, `ux_users_email`, `ux_providers_user_id`, `ix_providers_category_city (category, address_city) WHERE is_active`, `ix_providers_display_name`) in `src/Scheduling.Infrastructure/Persistence/Configurations/`
- [X] T019 [P] Create `UserRepository`, `CustomerRepository`, `ProviderRepository` in `src/Scheduling.Infrastructure/Persistence/Repositories/`
- [X] T020 Create `PostgresErrorTranslator` (maps `PostgresException` 23505 by constraint name → `ConflictException` e.g. `ux_users_email` → `user.email_taken`) and use it in `SchedulingDbContext.SaveChangesAsync` override in `src/Scheduling.Infrastructure/Persistence/PostgresErrorTranslator.cs`
- [X] T021 [P] Create `JwtOptions` and `JwtTokenService` (HMAC-SHA256 via `JsonWebTokenHandler`, expiry `AccessTokenMinutes`=60, claims `sub`, `email`, `role`, `provider_id`, `customer_id`) in `src/Scheduling.Infrastructure/Identity/`
- [X] T022 [P] Create `PasswordHasherAdapter` wrapping `PasswordHasher<User>` in `src/Scheduling.Infrastructure/Identity/PasswordHasherAdapter.cs`
- [X] T023 Create `src/Scheduling.Infrastructure/DependencyInjection.cs` (`AddInfrastructure(configuration)`: `configuration.GetConnectionString("DefaultConnection")` — throw `InvalidOperationException` if missing; `UseNpgsql`; repositories; identity services; bind `JwtOptions`/`NotificationOptions` with validation on start)
- [X] T024 Generate migration `InitialIdentity` (users, customers, providers) in `src/Scheduling.Infrastructure/Persistence/Migrations/`
- [X] T025 Create `AdminSeeder` (idempotent; reads `Seed:AdminEmail`/`Seed:AdminPassword`; skips if absent) in `src/Scheduling.Infrastructure/Seed/AdminSeeder.cs`

### API base

- [X] T026 Create `GlobalExceptionHandler : IExceptionHandler` mapping exceptions → Problem Details (400/401/403/404/409/412/422/500), extensions `code` and `traceId`, `type` = `https://schedulemanager.dev/problems/{code}`, stack trace only in Development in `src/Scheduling.Api/ErrorHandling/GlobalExceptionHandler.cs`
- [X] T027 Create `FluentValidationFilter` (resolves `IValidator<T>` for action arguments; failures → `ValidationProblemDetails` 400 with `code: validation.failed` and `errors` by camelCase field) and configure `ApiBehaviorOptions.InvalidModelStateResponseFactory` with the same shape in `src/Scheduling.Api/ErrorHandling/FluentValidationFilter.cs`
- [X] T028 [P] Create authorization policies `CustomerOnly`, `ProviderOnly`, `AdminOnly`, `ProviderOrAdmin` and `HttpCurrentUser : ICurrentUser` (from claims) in `src/Scheduling.Api/Auth/`
- [X] T029 [P] Create OpenAPI document transformer adding `bearerAuth` scheme and server `/api/v1` in `src/Scheduling.Api/OpenApi/BearerSecuritySchemeTransformer.cs`
- [X] T030 Compose `src/Scheduling.Api/Program.cs`: `AddApplication`, `AddInfrastructure`, controllers + `FluentValidationFilter`, JSON enums as strings (camelCase props), `AddProblemDetails` + `GlobalExceptionHandler`, JWT bearer (401/403 as Problem Details with `auth.unauthenticated`/`auth.forbidden`), `TimeProvider.System`, `AddOpenApi("v1")` at `/openapi/v1.json`, Swagger UI at `/swagger` (Development/Staging only), run `AdminSeeder` on start; expose `public partial class Program` for tests

### Auth (registro/login — bloqueante para rotas protegidas)

- [X] T031 [P] Create DTOs + validators `RegisterCustomerRequest` (name 2–120, email ≤254, password 8–128 "ao menos uma letra e um dígito", phone E.164), `RegisterProviderRequest` (+ displayName 2–120, description ≤2000, category, contactEmail opcional, contactPhone, address, timeZoneId IANA válido default `America/Sao_Paulo`), `LoginRequest`, `AuthResponse`, `CurrentUserDto` in `src/Scheduling.Application/Auth/`
- [X] T032 Create `AuthService` (`RegisterCustomerAsync` creates User+Customer; `RegisterProviderAsync` creates User+Provider with default BookingPolicy; `LoginAsync` returns generic `auth.invalid_credentials` for unknown email, wrong password or inactive user; `GetCurrentAsync`) in `src/Scheduling.Application/Auth/AuthService.cs`
- [X] T033 Create `AuthController` (`POST auth/register/customer` 201, `POST auth/register/provider` 201, `POST auth/login` 200, `GET auth/me` 200) per contract in `src/Scheduling.Api/Controllers/AuthController.cs`

### Test infrastructure & foundational tests

- [X] T034 [P] Create integration fixtures in `src/Scheduling.IntegrationTests/Fixtures/`: `PostgresContainerFixture` (Testcontainers `postgres:16`, collection fixture), `ApiFactory : WebApplicationFactory<Program>` (overrides `ConnectionStrings:DefaultConnection` with container connection string, test JWT key, replaces `TimeProvider` with `FakeTimeProvider`, disables hosted background services by default, applies migrations), `DatabaseReset` (truncate all tables between tests), `AuthHelper` (register/login customer, provider, seeded admin → authenticated `HttpClient`), `ProblemAssert` (asserts content-type `application/problem+json`, `status`, `code`, `traceId`)
- [X] T035 [P] Unit tests for `User`, `Customer`, `Address`, `BookingPolicy` (each range bound ±1), `Provider.UpdateProfile` invalid time zone in `src/Scheduling.UnitTests/Domain/FoundationDomainTests.cs`
- [X] T036 [P] Unit tests for `RegisterCustomerRequest`, `RegisterProviderRequest`, `LoginRequest`, `PageRequest` validators (FluentValidation.TestHelper) in `src/Scheduling.UnitTests/Validators/AuthValidatorsTests.cs`
- [X] T037 Integration tests in `src/Scheduling.IntegrationTests/Endpoints/AuthEndpointsTests.cs`: register customer 201 + token; register provider 201 with default policy; duplicate e-mail differing only by case → 409 `user.email_taken`; invalid payload → 400 `validation.failed` with `errors`; wrong password and unknown e-mail → identical 401 `auth.invalid_credentials`; expired token (advance `FakeTimeProvider` 61 min) → 401; `GET auth/me` returns role/providerId/customerId (US4 scenarios 1, 2, 3, 5)

**Checkpoint**: `dotnet build src/ScheduleManager.slnx` e `dotnet test` verdes — stories podem começar

---

## Phase 3: User Story 2 - Prestador configura perfil, serviços e expediente (Priority: P1)

**Goal**: prestador mantém perfil, política de reservas, serviços, expediente semanal e exceções; catálogo público lista prestadores/serviços ativos

**Independent Test**: prestador cria serviço e expediente com almoço; endpoints de schedule/serviços/catálogo refletem exatamente a configuração (a verificação via disponibilidade ocorre em US1)

### Tests for User Story 2 ⚠️ (escrever primeiro; devem falhar)

- [X] T038 [P] [US2] Unit tests for weekly schedule invariants in `src/Scheduling.UnitTests/Domain/Providers/WorkingHoursTests.cs`: overlap on same day rejected; adjacent (12:00–12:00) allowed; `start_time < end_time`; "sem faixa atravessando meia-noite"; "duração mínima da faixa ≥ `slot_step_minutes`"; "máx. 10 faixas/dia"; replace substitutes the whole set
- [X] T039 [P] [US2] Unit tests for `ScheduleException` in `src/Scheduling.UnitTests/Domain/Providers/ScheduleExceptionTests.cs`: `end_date >= start_date`; "período ≤ 366 dias"; Closure with both times null = full day, both set = partial, only one set invalid; SpecialHours requires times; overlapping SpecialHours on same date rejected (`schedule.special_hours_overlap`); start date in past rejected (`schedule.exception_in_past`)
- [X] T040 [P] [US2] Unit tests for `Service` in `src/Scheduling.UnitTests/Domain/Services/ServiceTests.cs`: duration "5–720, múltiplo de 5"; price "0 – 99 999,99"; name 1–100; activate/deactivate
- [X] T041 [P] [US2] Unit tests for validators `UpdateProviderRequest`, `BookingPolicyDto`, `ServiceRequest`, `WeeklyScheduleDto` (duplicate `dayOfWeek`, `HH:mm` format), `ScheduleExceptionRequest`, catalog query in `src/Scheduling.UnitTests/Validators/ProviderValidatorsTests.cs`
- [X] T042 [P] [US2] Integration tests in `src/Scheduling.IntegrationTests/Endpoints/ProvidersEndpointsTests.cs`: `GET/PUT providers/me`; `PUT providers/me/booking-policy` (out-of-range → 400); `GET providers` paging + filters `category`, `city`, `q`, `sort`; `GET providers/{id}` 404 for unknown; customer calling `PUT providers/me` → 403 (US2 scenario 7)
- [X] T043 [P] [US2] Integration tests in `src/Scheduling.IntegrationTests/Endpoints/ServicesEndpointsTests.cs`: create 201 + `Location` (US2 scenario 1); duplicate name case-insensitive → 409 `service.name_taken`; update; `PATCH .../status` deactivate → absent from public `GET providers/{id}/services`; `GET providers/me/services?isActive=false`; delete never-booked → 204; another provider's serviceId → 404
- [X] T044 [P] [US2] Integration tests in `src/Scheduling.IntegrationTests/Endpoints/ScheduleEndpointsTests.cs`: `PUT working-hours` with Tue 08–12 + 13–18 → 200 `affectedAppointments: []`; overlapping intervals (08–12 & 11–14) → 400 (US2 scenario 3); exceptions create (Closure full/partial, SpecialHours) 201 + list with `from`/`to` + delete 204; past date → 422 `schedule.exception_in_past`

### Implementation for User Story 2

- [X] T045 [P] [US2] Create `Service` aggregate in `src/Scheduling.Domain/Services/Service.cs`: `ProviderId`, `Name` "obrigatório; único por prestador (case-insensitive)" max 100, `Description` max 1000 opcional, `DurationMinutes` "5–720, múltiplo de 5", `Price` "0 – 99 999,99 (BRL)" decimal(10,2), `IsActive` padrão `true`
- [X] T046 [P] [US2] Create `WorkingHours` (`DayOfWeek` 0–6, `StartTime`/`EndTime` `TimeOnly`), `ScheduleExceptionType` (`Closure`, `SpecialHours`) and `ScheduleException` (`StartDate`/`EndDate` `DateOnly`, `StartTime?`/`EndTime?`, `Reason` max 200) in `src/Scheduling.Domain/Providers/`
- [X] T047 [US2] Add to `Provider` aggregate: `ReplaceWorkingHours(IEnumerable<WorkingHours>)`, `AddException(...)`, `RemoveException(id)`, `GetDaySchedule(DateOnly)` (SpecialHours of date if any else weekly intervals; returns intervals + closures) enforcing T038/T039 invariants in `src/Scheduling.Domain/Providers/Provider.cs`
- [X] T048 [P] [US2] Create EF configurations `ServiceConfiguration` (`ux_services_provider_name (provider_id, lower(name))`), `WorkingHoursConfiguration` (`provider_working_hours`, cascade, `ix_working_hours_provider_day`), `ScheduleExceptionConfiguration` (`provider_schedule_exceptions`, cascade, `ix_exceptions_provider_dates`) in `src/Scheduling.Infrastructure/Persistence/Configurations/`
- [X] T049 [US2] Generate migration `ProviderScheduleAndServices` in `src/Scheduling.Infrastructure/Persistence/Migrations/`; add `ux_services_provider_name` → `service.name_taken` to `PostgresErrorTranslator`
- [X] T050 [P] [US2] Create `IServiceRepository` + `ServiceRepository`; extend `IProviderRepository` with `SearchActiveAsync(category, city, q, sort, PageRequest)` and `GetWithScheduleAsync(providerId, fromDate, toDate)` in `src/Scheduling.Application/Abstractions/` and `src/Scheduling.Infrastructure/Persistence/Repositories/`
- [X] T051 [P] [US2] Create provider DTOs + validators (`UpdateProviderRequest`, `BookingPolicyDto`, `ProviderProfileDto`, `ProviderPublicProfileDto`, `ProviderSummaryDto`, `ProviderSearchQuery`) in `src/Scheduling.Application/Providers/`
- [X] T052 [P] [US2] Create service DTOs + validators (`ServiceRequest`, `ServiceDto`, `ActiveStatusRequest`) in `src/Scheduling.Application/Services/`
- [X] T053 [P] [US2] Create schedule DTOs + validators (`WeeklyScheduleDto`, `DayScheduleDto`, `TimeIntervalDto` `HH:mm`, `ScheduleExceptionRequest`, `ScheduleExceptionDto`, `ScheduleChangeResultDto`, `ScheduleExceptionCreatedDto`) in `src/Scheduling.Application/Schedule/`
- [X] T054 [US2] Create `ProviderService` (get own profile, update profile, update booking policy, public profile — 404 if inactive, search catalog) in `src/Scheduling.Application/Providers/ProviderService.cs`
- [X] T055 [US2] Create `ServiceCatalogService` (list own incl. inactive, list public active, create, update, set status, delete; ownership via `ICurrentUser.ProviderId` → 404 otherwise) in `src/Scheduling.Application/Services/ServiceCatalogService.cs`
- [X] T056 [US2] Create `ScheduleService` (get weekly schedule, replace working hours, list/create/delete exceptions; returns `affectedAppointments` — empty until T082 wires the query) in `src/Scheduling.Application/Schedule/ScheduleService.cs`
- [X] T057 [US2] Create `ProvidersController` (`GET providers`, `GET providers/{providerId}`, `GET/PUT providers/me`, `PUT providers/me/booking-policy`) in `src/Scheduling.Api/Controllers/ProvidersController.cs`
- [X] T058 [US2] Create `ServicesController` (`GET providers/{providerId}/services`, `GET/POST providers/me/services`, `PUT/DELETE providers/me/services/{serviceId}`, `PATCH providers/me/services/{serviceId}/status`) in `src/Scheduling.Api/Controllers/ServicesController.cs`
- [X] T059 [US2] Create `ScheduleController` (`GET providers/me/schedule`, `PUT providers/me/schedule/working-hours`, `GET/POST providers/me/schedule/exceptions`, `DELETE providers/me/schedule/exceptions/{exceptionId}`) in `src/Scheduling.Api/Controllers/ScheduleController.cs`

**Checkpoint**: US2 testes verdes; prestador totalmente configurável

---

## Phase 4: User Story 1 - Cliente consulta horários livres e agenda (Priority: P1) 🎯 MVP

**Goal**: disponibilidade calculada pela matriz R1–R11 e criação de agendamento sem overbooking, inclusive sob concorrência

**Independent Test**: com prestador/serviço/expediente configurados (fixtures via API de US2), cliente consulta a data, reserva um horário retornado, e o horário (e sobrepostos) somem da nova consulta; 50 reservas paralelas → 1 sucesso

### Tests for User Story 1 ⚠️

- [X] T060 [P] [US1] Unit tests `src/Scheduling.UnitTests/Domain/Availability/AvailabilityCalculatorTests.cs` (fake `now`): Mon 09–12, 30 min, step 15 → 09:00…11:30 (US1 scenario 1); lunch gap 08–12/13–18 with 60 min → last morning 11:00 (US2 scenario 2); service longer than every interval → empty; end exactly at interval end valid, +1 min invalid; adjacency allowed; buffer blocks following start (R6/R7 with `blocked_until`); cancelled/completed don't block; today excludes past and `< now + minBookingNotice` (R8); date in past / beyond horizon → `availability.date_in_past` / `availability.beyond_horizon` (R2); full-day Closure → empty; partial Closure cuts interval; SpecialHours replaces weekly (US2 scenario 5); DST gap time skipped and ambiguous time not duplicated (R9, use a zone with DST e.g. `America/New_York`); output ordered without duplicates (R10); `EnsureBookable` returns `availability.misaligned_start` (10:07 with step 15), `availability.outside_working_hours`, `availability.closed`, `availability.too_soon`, `appointment.slot_unavailable`
- [X] T061 [P] [US1] Unit tests `src/Scheduling.UnitTests/Domain/Appointments/AppointmentCreateTests.cs`: snapshot copied (name/duration/price); `end_at = start_at + duration`; `blocked_until = end_at + buffer`; initial status `Confirmed` when `autoConfirm` else `Pending`; initial `AppointmentStatusChange` with `from_status` null
- [X] T062 [P] [US1] Unit tests `src/Scheduling.UnitTests/Application/AppointmentServiceCreateTests.cs` (NSubstitute): inactive provider → `provider.inactive`; inactive service → `service.inactive`; service of other provider → 404 `service.not_found`; idempotency key existing + same payload → returns original (isReplay=true); same key + different payload → `idempotency.key_reused`; non-customer → forbidden
- [X] T063 [P] [US1] Unit tests validators `CreateAppointmentRequest` (`startAt` must include offset, `notes` ≤500) and `AvailabilityQuery` (`serviceId` required, `date` `yyyy-MM-dd`) in `src/Scheduling.UnitTests/Validators/BookingValidatorsTests.cs`
- [X] T064 [P] [US1] Integration tests `src/Scheduling.IntegrationTests/Endpoints/AvailabilityEndpointsTests.cs`: anonymous access 200; slots carry `-03:00` offset and `timeZoneId`; holiday/vacation date → `slots: []` (US1 scenario 4); past date → 422; malformed date → 400 with `errors.date`; unknown provider/service or service from other provider → 404
- [X] T065 [P] [US1] Integration tests `src/Scheduling.IntegrationTests/Endpoints/CreateAppointmentTests.cs`: 201 with `Location`, `ETag`, status `Pending` (and `Confirmed` with autoConfirm) (US1 scenario 2); slots 09:45/10:00/10:15 vanish, 10:30 remains; second customer same slot → 409 `appointment.slot_unavailable` (US1 scenario 3); same customer overlapping booking at another provider → 409 `appointment.customer_overlap`; misaligned 10:07 → 422; too soon / beyond horizon → 422 (US1 scenario 5); provider role → 403; anonymous → 401; `Idempotency-Key` replay → 200 same id; reused key different payload → 422 `idempotency.key_reused`
- [X] T066 [P] [US1] Integration tests `src/Scheduling.IntegrationTests/Concurrency/BookingConcurrencyTests.cs` (50 distinct customers, `Task.WhenAll`, `SemaphoreSlim` start barrier): same slot → exactly 1×201 + 49×409 and exactly 1 active row in DB (SC-001); overlapping different durations (10:00–10:45 vs 10:30–11:00) → exactly 1 accepted; 10 non-conflicting slots in parallel → all 201; same customer double submit without key → 1×201 + 1×409
- [X] T067 [P] [US1] Integration tests `src/Scheduling.IntegrationTests/Endpoints/GetAppointmentTests.cs`: owner customer, appointment's provider and admin → 200 with `history` and `ETag`; other customer / other provider → 404 `appointment.not_found`; provider sees customer email/phone

### Implementation for User Story 1

- [X] T068 [P] [US1] Create `AppointmentStatus` (`Pending`, `Confirmed`, `CancelledByCustomer`, `CancelledByProvider`, `Completed`; helper `IsBlocking`), `ServiceSnapshot` VO and `AppointmentStatusChange` entity (`from_status?`, `to_status`, `changed_by_user_id`, `changed_by_role`, `reason?` max 500, `changed_at`) in `src/Scheduling.Domain/Appointments/`
- [X] T069 [US1] Create `Appointment` aggregate with factory `Create(provider, customerId, service, startAt, notes, idempotencyKey, actor, now)` in `src/Scheduling.Domain/Appointments/Appointment.cs`: `customer_notes` max 500; invariants `end_at > start_at`, `blocked_until >= end_at`; `Version` (uint) property for concurrency
- [X] T070 [P] [US1] Create `TimeSlot` VO, `Occupation` record (`StartAt`, `BlockedUntil`), `DaySchedule` and `AvailabilityCalculator` (`GetAvailableSlots(provider, service, date, occupations, now)` and `EnsureBookable(...)` implementing R1–R11 exactly as spec.md matrix, candidates aligned to `interval_start + k·step`, half-open intervals, local→UTC via `TimeZoneInfo`, invalid DST times skipped, ambiguous → first offset) in `src/Scheduling.Domain/Availability/`
- [X] T071 [P] [US1] Create EF configurations `AppointmentConfiguration` (`appointments`, snapshot columns, `xmin` mapped as concurrency token `Version`, `idempotency_key uuid?`) and `AppointmentStatusChangeConfiguration` (`appointment_status_changes`, cascade) in `src/Scheduling.Infrastructure/Persistence/Configurations/`
- [X] T072 [US1] Generate migration `Appointments` and add via `migrationBuilder.Sql` exactly as data-model.md: `CREATE EXTENSION IF NOT EXISTS btree_gist`, `ck_appointments_time`, `ex_appointments_provider_overlap`, `ex_appointments_customer_overlap`, `ux_appointments_idempotency`, `ix_appointments_customer_start`, `ix_appointments_status_start` (with matching `Down`) in `src/Scheduling.Infrastructure/Persistence/Migrations/`
- [X] T073 [US1] Extend `PostgresErrorTranslator`: SQLSTATE `23P01` with constraint `ex_appointments_provider_overlap` → `ConflictException(appointment.slot_unavailable)`, `ex_appointments_customer_overlap` → `appointment.customer_overlap`; `23505` on `ux_appointments_idempotency` → reload and treat as replay in `src/Scheduling.Infrastructure/Persistence/PostgresErrorTranslator.cs`
- [X] T074 [P] [US1] Create `IAppointmentRepository` + `AppointmentRepository` (`GetOccupationsAsync(providerId, utcFrom, utcTo)` blocking statuses only, `GetByIdempotencyKeyAsync(customerId, key)`, `GetDetailAsync(id)` with history/provider/customer, `ExistsForServiceAsync(serviceId)`, `GetActiveFutureAsync(providerId, utcFrom)`) in `src/Scheduling.Application/Abstractions/IAppointmentRepository.cs` and `src/Scheduling.Infrastructure/Persistence/Repositories/AppointmentRepository.cs`
- [X] T075 [P] [US1] Create DTOs + mapping `AvailabilityQuery`, `AvailabilityResponse`, `TimeSlotDto`, `CreateAppointmentRequest` (+ validator), `AppointmentDto` (provider, customer with contact only for owner/provider/admin, service snapshot, `version`, `history`), `AppointmentSummaryDto`, `StatusChangeDto` in `src/Scheduling.Application/Availability/` and `src/Scheduling.Application/Appointments/`
- [X] T076 [US1] Create `AvailabilityService.GetAsync(providerId, query)` (load provider with schedule for date, service, occupations for the local day ±buffer in UTC; call calculator; convert to provider offset) in `src/Scheduling.Application/Availability/AvailabilityService.cs`
- [X] T077 [US1] Create `AppointmentService.CreateAsync(request, idempotencyKey)` (flow from plan.md "Fluxo de criação": idempotency → load → `EnsureBookable` → `Appointment.Create` → save; returns `(dto, isReplay)`) and `GetByIdAsync(id)` (ownership → 404) in `src/Scheduling.Application/Appointments/AppointmentService.cs`
- [X] T078 [US1] Create `AvailabilityController` (`GET providers/{providerId}/availability`, `[AllowAnonymous]`) in `src/Scheduling.Api/Controllers/AvailabilityController.cs`
- [X] T079 [US1] Create `AppointmentsController` with `POST appointments` (`CustomerOnly`, optional `Idempotency-Key` header, 201 + `Location` + `ETag: W/"{version}"` or 200 on replay) and `GET appointments/{appointmentId}` (`ETag`) in `src/Scheduling.Api/Controllers/AppointmentsController.cs`
- [X] T080 [US1] Close US2 gaps that need appointments: `ServiceCatalogService.DeleteAsync` → 409 `service.in_use` when `ExistsForServiceAsync`; `ScheduleService` fills `affectedAppointments` with active future appointments no longer bookable under the new schedule/exception (FR-007, never cancels) in `src/Scheduling.Application/Services/ServiceCatalogService.cs` and `src/Scheduling.Application/Schedule/ScheduleService.cs`
- [X] T081 [US1] Add integration cases for T080 (delete booked service → 409 `service.in_use`; vacation exception over a booked date → 201 with that appointment in `affectedAppointments` and appointment still `Pending`; deactivated service keeps existing appointment valid — US2 scenario 6) in `src/Scheduling.IntegrationTests/Endpoints/ServicesEndpointsTests.cs` and `src/Scheduling.IntegrationTests/Endpoints/ScheduleEndpointsTests.cs`

**Checkpoint**: 🎯 MVP — US2 + US1 funcionais e testadas; validar quickstart passos 1–9 e 12–13

---

## Phase 5: User Story 3 - Gestão do ciclo de vida do agendamento (Priority: P2)

**Goal**: confirmar, cancelar e concluir conforme a máquina de estados, com auditoria, janela de cancelamento e controle de concorrência

**Independent Test**: percorrer Pending→Confirmed→Completed e cancelamentos; transições inválidas → 422; cancelamento libera horário; alterações concorrentes → 1×200 + 1×409

### Tests for User Story 3 ⚠️

- [X] T082 [P] [US3] Unit tests `src/Scheduling.UnitTests/Domain/Appointments/AppointmentStateMachineTests.cs` covering every cell of the data-model.md state table: Provider/Admin confirm Pending; Customer cancel only if `now ≤ start_at − min_cancellation_notice_minutes` else `appointment.cancellation_window`; Provider cancel → `CancelledByProvider`; Admin cancel requires `onBehalfOf`; cancel after start → `appointment.already_started`; complete before start → `appointment.not_started`; any transition from final states → `appointment.invalid_transition`; each transition appends history and requires reason on cancel
- [X] T083 [P] [US3] Unit tests for `CancelAppointmentRequest` validator (`reason` 3–500; `onBehalfOf` only `Customer`/`Provider`) in `src/Scheduling.UnitTests/Validators/CancelAppointmentValidatorTests.cs`
- [X] T084 [P] [US3] Integration tests `src/Scheduling.IntegrationTests/Endpoints/AppointmentLifecycleTests.cs`: US3 scenarios 1–6; cancelled slot reappears in availability; `ETag` changes after each transition; stale `If-Match` → 412 `appointment.precondition_failed`; customer cannot confirm/complete → 403; complete after advancing `FakeTimeProvider` past start → 200
- [X] T085 [P] [US3] Integration tests `src/Scheduling.IntegrationTests/Concurrency/StatusConcurrencyTests.cs`: provider `complete` and customer `cancel` in parallel (no `If-Match`) → exactly one 200 and one 409 `appointment.concurrency_conflict`; parallel cancel + new booking of same slot never yields two active overlapping rows
- [X] T086 [P] [US3] Integration tests `src/Scheduling.IntegrationTests/Endpoints/ProviderAgendaTests.cs`: `GET providers/me/appointments` paging, filters `status` (multi), `from`/`to`, `serviceId`, `sort`; only own appointments

### Implementation for User Story 3

- [X] T087 [US3] Add `Confirm(actor, now)`, `Cancel(actor, onBehalfOf, reason, policy, now)`, `Complete(actor, now)` to `Appointment` (`cancellation_reason` max 500 "obrigatório quando cancelado"; history entry per transition) in `src/Scheduling.Domain/Appointments/Appointment.cs`
- [X] T088 [P] [US3] Create `CancelAppointmentRequest` + validator and `AgendaQuery` (+ validator: `to > from`, período ≤ 366 dias) in `src/Scheduling.Application/Appointments/`
- [X] T089 [US3] Add `ConfirmAsync`, `CancelAsync`, `CompleteAsync(id, expectedVersion?)` to `AppointmentService` (ownership; `expectedVersion` mismatch → `PreconditionFailedException`) and map `DbUpdateConcurrencyException` → `ConflictException(appointment.concurrency_conflict)` in `src/Scheduling.Application/Appointments/AppointmentService.cs` and `src/Scheduling.Infrastructure/Persistence/SchedulingDbContext.cs`
- [X] T090 [US3] Add `POST appointments/{id}/confirm|cancel|complete` (parse `If-Match` `W/"n"`, return `ETag`) to `src/Scheduling.Api/Controllers/AppointmentsController.cs`
- [X] T091 [US3] Add `AppointmentService.GetProviderAgendaAsync(AgendaQuery)` + repository paging query and `GET providers/me/appointments` in `src/Scheduling.Application/Appointments/AppointmentService.cs`, `src/Scheduling.Infrastructure/Persistence/Repositories/AppointmentRepository.cs`, `src/Scheduling.Api/Controllers/ProvidersController.cs`

**Checkpoint**: US1–US3 verdes

---

## Phase 6: User Story 4 - Cadastro, autenticação e perfis de acesso (Priority: P2)

**Goal**: completar o que não é bloqueante (registro/login já em Phase 2): perfil e histórico do cliente, administração de contas, matriz de autorização

**Independent Test**: cliente registra, atualiza perfil, vê só o próprio histórico filtrado; admin desativa prestador e ele some do catálogo; acessos cruzados negados

### Tests for User Story 4 ⚠️

- [X] T092 [P] [US4] Unit tests for `UpdateCustomerRequest`, `CustomerHistoryQuery`, `AdminUserQuery` validators in `src/Scheduling.UnitTests/Validators/CustomerAdminValidatorsTests.cs`
- [X] T093 [P] [US4] Integration tests `src/Scheduling.IntegrationTests/Endpoints/CustomersEndpointsTests.cs`: `GET/PUT customers/me` (e-mail change to taken e-mail → 409; name/email synced to user and login works with new e-mail); `GET customers/me/appointments` only own, paging, filters `status`/`from`/`to`/`providerId`, default sort `-startAt` (US4 scenario 4)
- [X] T094 [P] [US4] Integration tests `src/Scheduling.IntegrationTests/Endpoints/AdminEndpointsTests.cs`: `GET admin/users` filters `role`/`isActive`/`q`; `PATCH admin/users/{id}/status` deactivating provider → absent from `GET providers`, `GET providers/{id}` 404, booking → 422 `provider.inactive`, its login → 401 (US4 scenario 6); self-deactivation → 422 `admin.cannot_deactivate_self`; non-admin → 403
- [X] T095 [P] [US4] Integration tests `src/Scheduling.IntegrationTests/Authorization/AuthorizationMatrixTests.cs`: data-driven over every operation in contracts/openapi.yaml × {anonymous, customer, provider, admin} asserting 401/403/2xx per `x-roles`, plus cross-ownership (customer A vs B, provider A vs B) → 404 (SC-007)

### Implementation for User Story 4

- [X] T096 [P] [US4] Create `CustomerProfileDto`, `UpdateCustomerRequest` (+ validator), `CustomerHistoryQuery` (+ validator) in `src/Scheduling.Application/Customers/`
- [X] T097 [US4] Create `CustomerService` (get/update profile keeping `users.name/email` and `customers.name/email` in sync; history paging via `IAppointmentRepository`) in `src/Scheduling.Application/Customers/CustomerService.cs`
- [X] T098 [US4] Create `CustomersController` (`GET/PUT customers/me`, `GET customers/me/appointments`) in `src/Scheduling.Api/Controllers/CustomersController.cs`
- [X] T099 [P] [US4] Create `UserSummaryDto`, `AdminUserQuery` (+ validator) and `AdminUserService` (list; set status — deactivating a Provider user also sets `providers.is_active = false`; cannot deactivate self) in `src/Scheduling.Application/Admin/`
- [X] T100 [US4] Create `AdminController` (`GET admin/users`, `PATCH admin/users/{userId}/status`) in `src/Scheduling.Api/Controllers/AdminController.cs`

**Checkpoint**: US1–US4 verdes

---

## Phase 7: User Story 5 - Notificações de confirmação e lembrete (Priority: P3)

**Goal**: outbox de notificações transacional, lembrete único 24h antes e dispatcher simulado com retries

**Independent Test**: confirmar agendamento para daqui a 25h, avançar relógio 1h e rodar o processador → exatamente 1 lembrete; cancelado → nenhum

### Tests for User Story 5 ⚠️

- [X] T101 [P] [US5] Unit tests `src/Scheduling.UnitTests/Application/NotificationFactoryTests.cs`: recipients per event ("`Created` → cliente + prestador; `Confirmed` → cliente; `Cancelled` → a outra parte (e o cliente quando Admin cancela); `Reminder` → cliente"); pt-BR subject/body include service, local date/time with offset
- [X] T102 [P] [US5] Unit tests `src/Scheduling.UnitTests/Domain/Notifications/NotificationTests.cs`: `MarkSent`, `MarkFailedAttempt` backoff 1/5/25 min and `Failed` after 3 attempts ("0–3"), `Cancel`
- [X] T103 [P] [US5] Integration tests `src/Scheduling.IntegrationTests/Notifications/NotificationOutboxTests.cs`: creating/confirming/cancelling writes notifications in same transaction (US5 scenario 1); `GET notifications/me` returns only own; `GET admin/notifications` filters; simulated channel failure → `Failed` after retries while appointment unchanged (US5 scenario 5)
- [X] T104 [P] [US5] Integration tests `src/Scheduling.IntegrationTests/Notifications/ReminderTests.cs` using `FakeTimeProvider` and `IReminderProcessor.RunOnceAsync`: confirmed at +25h, advance 1h → exactly 1 reminder even after 3 runs (US5 scenario 2); cancelled → none (scenario 3); created with < 24h notice → none (scenario 4); pending-reminder cancelled by dispatcher if appointment no longer `Confirmed`

### Implementation for User Story 5

- [X] T105 [P] [US5] Create `NotificationType` (`AppointmentCreated`, `AppointmentConfirmed`, `AppointmentCancelled`, `AppointmentReminder`), `NotificationStatus` (`Pending`, `Sent`, `Failed`, `Cancelled`), `NotificationChannel` (`Email`) and `Notification` entity (`subject` max 200, `body` text, `attempts` 0–3, `scheduled_for`, `next_attempt_at?`, `sent_at?`, `last_error` max 1000) in `src/Scheduling.Domain/Notifications/`
- [X] T106 [P] [US5] Create `NotificationConfiguration` and migration `Notifications` with `ux_notifications_reminder ... WHERE type = 'AppointmentReminder'`, `ix_notifications_dispatch ... WHERE status = 'Pending'`, `ix_notifications_recipient` exactly as data-model.md in `src/Scheduling.Infrastructure/Persistence/Configurations/NotificationConfiguration.cs` and `src/Scheduling.Infrastructure/Persistence/Migrations/`
- [X] T107 [US5] Create `NotificationFactory` (pt-BR templates) and enqueue notifications inside `AppointmentService.CreateAsync/ConfirmAsync/CancelAsync` before the single `SaveChangesAsync` in `src/Scheduling.Application/Notifications/NotificationFactory.cs` and `src/Scheduling.Application/Appointments/AppointmentService.cs`
- [X] T108 [P] [US5] Create `INotificationChannel` and `NotificationOptions` in `src/Scheduling.Application/Abstractions/`, and `SimulatedNotificationChannel` (structured log only, no PII beyond recipient id; `SimulateFailure` option for tests) in `src/Scheduling.Infrastructure/Notifications/SimulatedNotificationChannel.cs`
- [X] T109 [US5] Create `IReminderProcessor`/`ReminderProcessor` (insert reminders for `Confirmed` with `start_at − 24h ≤ now < start_at` and `created_at ≤ start_at − 24h` using `INSERT … ON CONFLICT DO NOTHING`) and `ReminderScheduler : BackgroundService` (`PeriodicTimer`, `ReminderIntervalSeconds`) in `src/Scheduling.Infrastructure/Notifications/`
- [X] T110 [US5] Create `INotificationDispatcher`/`NotificationDispatcher` (batch `FOR UPDATE SKIP LOCKED`, send via channel, backoff, `MaxAttempts`, cancel reminders of non-`Confirmed` appointments) and `NotificationDispatcherService : BackgroundService` in `src/Scheduling.Infrastructure/Notifications/`; register hosted services in `src/Scheduling.Infrastructure/DependencyInjection.cs`
- [X] T111 [US5] Create `NotificationDto`, `NotificationQuery` and `NotificationsController` (`GET notifications/me`) plus `GET admin/notifications` in `src/Scheduling.Application/Notifications/`, `src/Scheduling.Api/Controllers/NotificationsController.cs` and `src/Scheduling.Api/Controllers/AdminController.cs`

**Checkpoint**: todas as stories verdes

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T112 [P] Integration test `src/Scheduling.IntegrationTests/Contract/OpenApiConformanceTests.cs`: fetch `/openapi/v1.json` and assert the set of (path, method) equals the 34 operations in `specs/001-online-scheduling-api/contracts/openapi.yaml` (embedded expected list) and that `bearerAuth` is declared
- [X] T113 [P] Integration test `src/Scheduling.IntegrationTests/Endpoints/ProblemDetailsTests.cs`: one representative request per status 400/401/403/404/409/412/422 asserts RFC 7807 shape, `code`, `traceId`; forced unhandled exception in `Production` environment → 500 without stack trace/exception message (SC-006)
- [X] T114 [P] Performance test `src/Scheduling.IntegrationTests/Performance/AvailabilityPerformanceTests.cs` (`[Trait("Category","Performance")]`): provider with 500 future appointments, 200 availability requests → p95 < 1 s (SC-002)
- [X] T115 [P] Add XML summaries, `ProducesResponseType`/`ProducesProblem` attributes and request examples to all controllers in `src/Scheduling.Api/Controllers/` so Swagger UI matches the contract
- [X] T116 [P] Add request logging that never logs passwords, tokens or `Authorization` header in `src/Scheduling.Api/Program.cs`
- [X] T117 Run `dotnet build src/ScheduleManager.slnx -warnaserror`, `dotnet test src/Scheduling.UnitTests`, `dotnet test src/Scheduling.IntegrationTests` and the manual scenarios of `specs/001-online-scheduling-api/quickstart.md`; record evidence (Constituição V)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2)** → stories
- **US2 (Phase 3)** depende só da Phase 2
- **US1 (Phase 4)** depende de US2 (Provider schedule, Service) — MVP = US2 + US1
- **US3 (Phase 5)** depende de US1 (Appointment)
- **US4 (Phase 6)** depende da Phase 2; o histórico (T093/T097) e o teste de matriz (T095) dependem de US1/US3 → executar após US3
- **US5 (Phase 7)** depende de US1 e US3 (eventos de criação/confirmação/cancelamento)
- **Polish (Phase 8)** após todas as stories

### User Story Dependencies

```text
Phase 2 ──▶ US2 ──▶ US1 (MVP) ──▶ US3 ──┬──▶ US5
   │                                    └──▶ US4 (histórico/admin/matriz)
   └── (US4 parte de registro/login já incluída na Phase 2)
```

### Within Each User Story

- Testes primeiro e falhando → entidades/VOs → configurações EF + migration → repositórios → DTOs/validators → services → controllers
- Migrations geradas em sequência (T024 → T049 → T072 → T106); nunca em paralelo

### Parallel Opportunities

- Phase 1: T004, T005, T006
- Phase 2: T007–T011 (domínio); T013–T015; T018, T019, T021, T022; T028, T029; T031; T034–T036
- US2: testes T038–T044 juntos; T045, T046, T048, T050–T053
- US1: testes T060–T067 juntos; T068, T070, T071, T074, T075
- US3: T082–T086 juntos; T088
- US4: T092–T095 juntos; T096, T099
- US5: T101–T104 juntos; T105, T106, T108
- Polish: T112–T116

---

## Parallel Example: User Story 1

```bash
# Testes (devem falhar primeiro):
Task: "T060 AvailabilityCalculatorTests in src/Scheduling.UnitTests/Domain/Availability/AvailabilityCalculatorTests.cs"
Task: "T065 CreateAppointmentTests in src/Scheduling.IntegrationTests/Endpoints/CreateAppointmentTests.cs"
Task: "T066 BookingConcurrencyTests in src/Scheduling.IntegrationTests/Concurrency/BookingConcurrencyTests.cs"

# Modelo e infraestrutura em paralelo:
Task: "T068 AppointmentStatus/ServiceSnapshot/AppointmentStatusChange in src/Scheduling.Domain/Appointments/"
Task: "T070 AvailabilityCalculator in src/Scheduling.Domain/Availability/"
Task: "T071 AppointmentConfiguration in src/Scheduling.Infrastructure/Persistence/Configurations/"
Task: "T075 Booking DTOs in src/Scheduling.Application/Availability/ and src/Scheduling.Application/Appointments/"
```

---

## Implementation Strategy

### MVP First (US2 + US1)

1. Phase 1 + Phase 2 (inclui registro/login)
2. Phase 3 (US2) → validar configuração do prestador
3. Phase 4 (US1) → **PARAR e VALIDAR**: quickstart passos 1–9, 12–13 e `BookingConcurrencyTests` (SC-001)
4. Demonstrar o MVP

### Incremental Delivery

1. MVP (US2 + US1)
2. + US3 → ciclo de vida completo
3. + US4 → histórico do cliente, administração e matriz de autorização
4. + US5 → notificações e lembretes
5. Polish → conformidade com contrato, Problem Details, performance

### Parallel Team Strategy

- Após Phase 2: Dev A — US2; Dev B — testes de US1 (T060–T067) e `AvailabilityCalculator` (T070, domínio puro, sem dependência de US2 além das entidades T045–T047)
- Após US1: Dev A — US3; Dev B — US4 (perfil/admin); US5 após US3

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente
- Cada tarefa é concluída somente com seus testes verdes (Constituição V); proibido `Skip` ou enfraquecer testes
- Commit ao fim de cada tarefa ou grupo lógico
- Novos pacotes além dos listados em research.md exigem justificativa na spec/plano antes da instalação

---

## Notas de implementação (2026-11-09)

Todas as tarefas foram concluídas. Desvios em relação ao texto original das tarefas:

- **T003 / T034 — sem Testcontainers**: o ambiente não tem Docker. O fixture (`src/Scheduling.IntegrationTests/Fixtures/ApiFixture.cs`) cria um banco descartável `schedulemanager_it_<guid>` no PostgreSQL local (configurável por `SCHEDULEMANAGER_TEST_DB`) e o remove ao final; o banco `postgres` de desenvolvimento nunca é usado. O relógio de teste é um `TestClock` próprio (o `FakeTimeProvider` não volta no tempo, necessário para resetar entre testes); `FakeTimeProvider` é usado nos testes unitários.
- **T003 — `Microsoft.Extensions.Identity.Core` não instalado**: o `PasswordHasher<T>` já vem no shared framework ASP.NET Core (o restore acusou a referência como redundante).
- **T024 / T049 / T072 / T106 — migration única** `InitialSchema` (o modelo foi implementado de uma vez), com o SQL das exclusion constraints, `ck_appointments_time`, `btree_gist` e `ux_services_provider_name` conforme data-model.md.
- **T025** — o seeder ficou em `src/Scheduling.Infrastructure/Seed/DatabaseInitializer.cs` (também aplica migrations se `Database:ApplyMigrationsOnStartup=true`).
- **T077 — advisory lock por prestador**: testes de concorrência mostraram que o PostgreSQL pode abortar as duas inserções concorrentes que conflitam numa exclusion constraint. A criação passou a rodar em `IUnitOfWork.ExecuteSerializedAsync` (`pg_advisory_xact_lock` por prestador); a constraint continua como garantia final (research.md R-01).
- **T085** — o cenário "conclui × cancela" foi provado com intercalação determinística de dois `DbContext` (mesma versão lida, segundo `SaveChanges` → 409 `appointment.concurrency_conflict`), já que a corrida via HTTP não é reproduzível de forma determinística.
- **T069** — `Appointment.Create` copia o `ServiceSnapshot` (bug encontrado pelo teste de desempenho: instância compartilhada de tipo owned só era persistida uma vez).
- **Confirm/Complete** recebem também a policy `ProviderOrAdmin` no controller (403 por perfil antes de chegar à aplicação).
- Nomes de alguns arquivos de teste diferem dos sugeridos (testes agrupados por tema, ex.: `ValidatorTests.cs`, `ProviderConfigurationTests.cs`, `ConcurrencyTests.cs`).

**Verificação (T117)**: `dotnet build ScheduleManager.slnx -warnaserror` sem avisos; 198 testes unitários e 119 de integração aprovados; smoke test da API real (Swagger 200, OpenAPI com 34 operações, disponibilidade, reserva 201 e reserva repetida 409) contra um banco temporário, removido em seguida.
