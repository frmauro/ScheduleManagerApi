# Research: API de Agendamento Online

**Feature**: `001-online-scheduling-api` | **Date**: 2026-11-09 | **Spec**: [spec.md](./spec.md)

Cada decisão segue o formato *Decision / Rationale / Alternatives considered*. Todas as incógnitas do Technical Context do [plan.md](./plan.md) estão resolvidas aqui.

---

## R-01 — Prevenção de overbooking sob concorrência (FR-017, FR-018, SC-001)

**Decision**: garantir a não-sobreposição **no banco de dados** com *exclusion constraints* do PostgreSQL (extensão `btree_gist`), avaliadas atomicamente no `INSERT`/`UPDATE`:

- `ex_appointments_provider_overlap`: `EXCLUDE USING gist (provider_id WITH =, tstzrange(start_at, blocked_until, '[)') WITH &&) WHERE (status IN (Pending, Confirmed))`, onde `blocked_until = end_at + buffer` (snapshot do preparo no momento da reserva).
- `ex_appointments_customer_overlap`: idem com `customer_id` e `tstzrange(start_at, end_at, '[)')`.

A violação (SQLSTATE `23P01`) é traduzida pela Infrastructure para `SlotUnavailableException` → **409 Conflict** (`code: appointment.slot_unavailable` ou `appointment.customer_overlap`, conforme o nome da constraint). A Application faz antes uma pré-validação (regras R1–R11) para devolver mensagens específicas; a constraint é a garantia final.

**Refinamento (implementação, 2026-11-09)**: testes de concorrência mostraram que o PostgreSQL pode abortar **as duas** inserções concorrentes que conflitam em uma exclusion constraint (estratégia interna contra deadlock) — nunca há overbooking, mas "exatamente um vence" (SC-001) não fica garantido. Por isso a criação roda em `IUnitOfWork.ExecuteSerializedAsync(providerId, …)`: transação com `pg_advisory_xact_lock` por prestador, em que a ocupação é relida após o lock. Reservas do **mesmo prestador** são avaliadas em série (o perdedor recebe 409 `appointment.slot_unavailable` com mensagem específica); prestadores diferentes não se bloqueiam. A exclusion constraint permanece como garantia final.

**Rationale**: é a única abordagem que fecha a janela entre "verificar" e "gravar" sem lock explícito, funciona com N instâncias da API, não bloqueia reservas em horários não conflitantes (requisito de edge case) e cobre sobreposição parcial (10:00–10:45 × 10:30–11:00), não só igualdade de início. Intervalo semiaberto `[)` permite adjacência (R7).

**Alternatives considered**:
- *Índice único (provider_id, start_at)*: só detecta inícios iguais; falha em sobreposição parcial e com durações diferentes.
- *Lock pessimista (`SELECT … FOR UPDATE` na linha do prestador / advisory lock)*: correto, mas serializa todas as reservas do prestador e depende de disciplina em todos os caminhos de escrita.
- *Isolamento SERIALIZABLE*: correto, mas gera falhas de serialização com retries e custo maior; menos explícito.
- *Concorrência otimista (row version) no agregado Provider*: força conflito entre reservas não conflitantes.

## R-02 — Concorrência em mudanças de status (FR-022)

**Decision**: concorrência otimista com a coluna de sistema `xmin` do PostgreSQL mapeada como *concurrency token* (`uint Version`). `DbUpdateConcurrencyException` → **409** (`code: appointment.concurrency_conflict`). A versão é exposta como `ETag` (`W/"<xmin>"`); se o cliente enviar `If-Match` divergente → **412 Precondition Failed**.

**Rationale**: sem coluna extra, nativo do provider Npgsql; a última escrita nunca sobrescreve silenciosamente a primeira.

**Alternatives considered**: coluna `RowVersion` manual (redundante no PostgreSQL); lock pessimista (desnecessário para operações curtas de status).

## R-03 — Idempotência na criação (FR-023)

**Decision**: header opcional `Idempotency-Key` (UUID) em `POST /appointments`, persistido em `appointments.idempotency_key` com índice único `(customer_id, idempotency_key)`. Repetição com mesma chave e mesmo payload → **200** com o agendamento original; mesma chave e payload diferente → **422** (`code: idempotency.key_reused`). Sem chave, o duplo envio é barrado pela constraint de sobreposição do cliente (R-01) → 409.

**Rationale**: dupla proteção barata, sem tabela/infra adicional.

**Alternatives considered**: tabela genérica de idempotência com cache de resposta (mais genérico, mais complexo que o necessário para um único endpoint).

## R-04 — Fusos horários e relógio (FR-031, R8, R9)

**Decision**:
- Instantes persistidos como `timestamptz` (UTC) / `DateTimeOffset` com offset 0.
- Expediente e exceções em hora local de parede: `TimeOnly` / `DateOnly` + `Provider.TimeZoneId` (IANA, ex. `America/Sao_Paulo`), resolvido por `TimeZoneInfo.FindSystemTimeZoneById` (suporte IANA nativo no .NET 6+ em Windows via ICU e em Linux).
- Conversão local→UTC no `AvailabilityCalculator` (Domain): hora inválida (salto DST) → candidato descartado; hora ambígua → primeiro offset (o maior, i.e. horário de verão ainda vigente).
- Relógio injetado via `TimeProvider` (BCL .NET 8+); testes usam `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, apenas no projeto de testes).

**Rationale**: zero dependências extras; determinismo em testes.

**Alternatives considered**: NodaTime (melhor modelagem, mas nova dependência sem necessidade comprovada — Constituição prioriza o nativo).

## R-05 — Localização do algoritmo de disponibilidade

**Decision**: `AvailabilityCalculator` como **serviço de domínio puro** em `Scheduling.Domain` — entrada: regras do prestador, faixas do dia, exceções, ocupações, duração, `now`; saída: lista de `TimeSlot`. A Application carrega os dados (1 consulta do expediente + 1 consulta de agendamentos bloqueantes no intervalo UTC do dia local) e chama o calculador. Na criação, o mesmo calculador valida o horário pedido (`IsBookable`), garantindo SC-004 (consulta ≡ criação).

**Rationale**: regra crítica 100% testável por unit test sem banco; uma única fonte de verdade para R1–R11.

## R-06 — Autenticação, senhas e autorização (FR-008–FR-013)

**Decision**:
- JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0); emissão com `JsonWebTokenHandler` (`Microsoft.IdentityModel.JsonWebTokens`, já transitivo). HMAC-SHA256, chave em configuração/secret. Access token de **60 min**, sem refresh token nesta etapa (fora do escopo, assim como recuperação de senha).
- Claims: `sub` (UserId), `email`, `role`, `provider_id` ou `customer_id`.
- Hash de senha: `PasswordHasher<TUser>` de `Microsoft.Extensions.Identity.Core` (PBKDF2, formato versionado), **sem** adotar o ASP.NET Core Identity completo.
- Policies: `CustomerOnly`, `ProviderOnly`, `AdminOnly`; verificação de propriedade (ownership) na Application (ex.: `appointment.CustomerId == currentUser.CustomerId`) → 404 para recurso de terceiros em leitura (não vaza existência) e 403 em ações.
- Login com falha → 401 genérico `auth.invalid_credentials`; conta inativa → mesmo 401.

**Alternatives considered**: ASP.NET Core Identity completo (tabelas e fluxos que não usaremos); BCrypt.Net (dependência de terceiros sem ganho relevante).

## R-07 — Erros (FR-028, Constituição IV)

**Decision**: `AddProblemDetails()` + `IExceptionHandler` global. Hierarquia de exceções na Application/Domain (`DomainRuleViolationException`, `NotFoundException`, `ConflictException`, `ForbiddenException`) mapeada a status. Todo Problem Details inclui extensões `code` (estável, legível por máquina) e `traceId`. Validação FluentValidation → `ValidationProblemDetails` (400) com `errors` por campo. Stack trace apenas em `Development`.

| Situação | Status | `code` (exemplos) |
|---|---|---|
| Payload inválido | 400 | `validation.failed` |
| Não autenticado / token expirado | 401 | `auth.unauthenticated`, `auth.invalid_credentials` |
| Perfil sem permissão | 403 | `auth.forbidden` |
| Recurso inexistente ou de terceiro | 404 | `provider.not_found`, `appointment.not_found` |
| Horário ocupado / sobreposição / versão | 409 | `appointment.slot_unavailable`, `appointment.customer_overlap`, `appointment.concurrency_conflict`, `user.email_taken` |
| `If-Match` divergente | 412 | `appointment.precondition_failed` |
| Regra de negócio violada | 422 | `availability.outside_working_hours`, `availability.misaligned_start`, `availability.too_soon`, `availability.beyond_horizon`, `appointment.invalid_transition`, `appointment.cancellation_window`, `service.inactive` |

## R-08 — Versionamento, OpenAPI e documentação interativa (FR-030)

**Decision**: prefixo de rota fixo `/api/v1` via `MapGroup` (sem pacote de versionamento — uma única versão). Documento gerado por `Microsoft.AspNetCore.OpenApi` 10 em `/openapi/v1.json`; UI interativa com **Swashbuckle.AspNetCore.SwaggerUI** (apenas o middleware de UI, apontando para o documento nativo), habilitada em Development/Staging. O contrato de referência é [contracts/openapi.yaml](./contracts/openapi.yaml); testes de integração verificam conformidade dos endpoints principais.

**Rationale**: o pacote nativo não traz UI; o pedido exige Swagger. Asp.Versioning seria dependência sem benefício para versão única.

**Alternatives considered**: Scalar (UI moderna, mas o usuário pediu Swagger); Swashbuckle completo (gerador duplicaria o nativo).

## R-09 — Estilo de endpoints: Controllers vs Minimal APIs

**Decision**: **Controllers** (`[ApiController]`), um por recurso, finos, delegando a Application Services.

**Rationale**: convenções maduras de Problem Details/model binding, atributos de autorização e documentação por action, organização familiar para o tamanho da API (~35 operações).

**Alternatives considered**: Minimal APIs com endpoint groups (viável; menos estrutura padronizada para uma API desse porte).

## R-10 — Organização da Application (sem mediator)

**Decision**: Application Services por agregado/caso de uso (`AppointmentService`, `AvailabilityService`, `ScheduleService`, …), interfaces de repositório e `IUnitOfWork` declaradas na Application e implementadas na Infrastructure; DTOs de request/response + validators FluentValidation na Application.

**Alternatives considered**: MediatR (licenciamento comercial recente + dependência adicional sem necessidade).

## R-11 — Notificações e lembretes (FR-025–FR-027, SC-005)

**Decision**: padrão *outbox* simples na tabela `notifications`:
1. Eventos de agendamento (criado/confirmado/cancelado) inserem `Notification(status=Pending)` **na mesma transação** da mudança de estado.
2. `ReminderScheduler` (`BackgroundService` + `PeriodicTimer`, a cada 1 min) insere lembretes para agendamentos `Confirmed` com `start_at - 24h <= now < start_at`, `created_at <= start_at - 24h` e sem lembrete; índice único parcial `(appointment_id) WHERE type = 'Reminder'` garante unicidade mesmo com várias instâncias (`ON CONFLICT DO NOTHING`).
3. `NotificationDispatcher` (`BackgroundService`, a cada 15 s) busca pendentes com `FOR UPDATE SKIP LOCKED`, envia via `INotificationChannel` (implementação `SimulatedNotificationChannel`, que apenas registra em log e marca `Sent`), com até 3 tentativas e backoff exponencial (1, 5, 25 min) → `Failed`.
4. Antes de enviar um lembrete, o dispatcher verifica se o agendamento ainda está `Confirmed`; caso contrário marca `Cancelled`.

**Rationale**: atende o SLA de 15 min com folga, falha de notificação nunca afeta o agendamento, sem Hangfire/Quartz.

**Alternatives considered**: Hangfire/Quartz (dependências pesadas para a etapa simulada); disparo síncrono na requisição (acopla falha de envio à reserva).

## R-12 — Paginação, filtros e ordenação (FR-029)

**Decision**: query `page` (≥1, padrão 1), `pageSize` (1–100, padrão 20), `sort` (`campo` ou `-campo`, whitelist por endpoint). Envelope `PagedResult<T>` = `{ items, page, pageSize, totalCount, totalPages }`. Filtros por endpoint documentados no contrato (ex.: `status` multivalor, `from`/`to`).

## R-13 — Persistência e provider

**Decision**: PostgreSQL 16+ com **`Npgsql.EntityFrameworkCore.PostgreSQL` 10.x** (justificativa: banco de referência exigido na especificação; o EF Core não traz provider PostgreSQL). Convenção `snake_case` nas tabelas configurada manualmente via `ToTable/HasColumnName` (ou `EFCore.NamingConventions` — **não** adotado, evitando dependência extra). Exclusion constraints e extensão `btree_gist` criadas via `migrationBuilder.Sql` nas migrations. Enums persistidos como `text` (legível, estável).

## R-14 — Estratégia de testes (Constituição V)

**Decision**:
- **Unit** (`Scheduling.UnitTests`, xUnit): entidades/invariantes, máquina de estados, `AvailabilityCalculator` (matriz R1–R11, DST, bordas), Application Services com dublês (NSubstitute) e todos os Validators (`FluentValidation.TestHelper`).
- **Integration** (`Scheduling.IntegrationTests`, xUnit + `WebApplicationFactory` + PostgreSQL real): pipeline HTTP real, JWT, Problem Details, persistência, exclusion constraints e testes de concorrência (50 requisições paralelas → 1 × 201 e 49 × 409), lembretes com relógio controlável (`TestClock`).
- **Desvio na implementação (2026-11-09)**: o ambiente de desenvolvimento não tem Docker, então **Testcontainers não foi adotado**. O fixture cria um banco **descartável e exclusivo** (`schedulemanager_it_<guid>`) no servidor PostgreSQL local, aplica as migrations e o remove ao final (`DROP DATABASE … WITH (FORCE)`). Servidor configurável por `SCHEDULEMANAGER_TEST_DB` (padrão `Host=localhost;Port=5432;Username=postgres;Password=123`). O banco `postgres` de desenvolvimento nunca é usado pelos testes. O objetivo (banco real com `EXCLUDE`/`tstzrange`) é mantido.
- Asserções com `Assert` do xUnit (sem FluentAssertions por licença comercial na v8).

**Rationale**: as garantias de concorrência dependem de recursos específicos do PostgreSQL — banco em memória/SQLite não as reproduziria. Testcontainers dá um banco real descartável por execução.

**Alternatives considered**: SQLite/InMemory (não suportam exclusion constraints — invalidariam SC-001); banco PostgreSQL compartilhado (testes não isolados).

## Dependências NuGet (justificativa exigida pela Constituição)

| Pacote | Projeto | Justificativa |
|---|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Infrastructure | Provider do banco exigido (R-13) |
| `Microsoft.EntityFrameworkCore.Design` | Infrastructure/Api | Migrations (já previsto na Constituição) |
| `FluentValidation.DependencyInjectionExtensions` | Application | Validação obrigatória (Constituição IV) |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Api | Autenticação (Constituição) |
| `Microsoft.AspNetCore.OpenApi` | Api | Documento OpenAPI (Constituição) |
| ~~`Microsoft.Extensions.Identity.Core`~~ | — | **Não instalado**: `PasswordHasher<T>` já vem no shared framework `Microsoft.AspNetCore.App` (o restore acusa a referência explícita como redundante — NU1510) |
| `Swashbuckle.AspNetCore.SwaggerUI` | Api | UI interativa Swagger exigida (R-08) |
| `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `NSubstitute`, `Microsoft.Extensions.TimeProvider.Testing` | Testes | Testes unitários (R-14). `FluentValidation.TestHelper` vem no próprio pacote FluentValidation |
| `Microsoft.AspNetCore.Mvc.Testing` | Testes | Testes de integração com o pipeline HTTP real (R-14) |
| ~~`Testcontainers.PostgreSql`~~ | — | **Não instalado** (sem Docker no ambiente) — ver desvio em R-14 |
| `dotnet-ef` 10.0.14 (ferramenta local, `src/dotnet-tools.json`) | — | Geração/aplicação de migrations |

Versões efetivamente fixadas em `src/Directory.Packages.props` (Central Package Management).
