# Quickstart & Validação: API de Agendamento Online

**Feature**: `001-online-scheduling-api` | Contrato: [contracts/openapi.yaml](./contracts/openapi.yaml) | Modelo: [data-model.md](./data-model.md)

Guia para provar, de ponta a ponta, que a funcionalidade atende à [spec](./spec.md). Não contém código de implementação.

## Pré-requisitos

- .NET SDK 10.0
- PostgreSQL 16+ acessível em `localhost:5432` (usuário `postgres`, senha `123`) — instalado localmente ou via Docker
- Opcional: cliente HTTP (curl, Bruno, REST Client) — os exemplos usam curl

## Setup

```bash
# 1. Banco local — PostgreSQL em localhost:5432, banco "postgres", usuário "postgres", senha "123".
#    Se ainda não houver um PostgreSQL instalado, suba um container equivalente:
docker run -d --name schedulemanager-db -e POSTGRES_PASSWORD=123 -p 5432:5432 postgres:16

# 2. Configuração
#    A connection string já vem em src/Scheduling.Api/appsettings.Development.json
#    (ver plan.md › Configuração do Banco de Dados):
#    "DefaultConnection": "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123"
#    O appsettings.Development.json também traz Jwt:SigningKey e um Admin inicial SOMENTE para desenvolvimento
#    (admin@schedulemanager.local / Admin12345). Para sobrescrever, use user-secrets do projeto Api:
dotnet user-secrets --project src/Scheduling.Api set "Jwt:SigningKey" "<chave aleatória com 32+ bytes>"
dotnet user-secrets --project src/Scheduling.Api set "Seed:AdminPassword" "<senha forte>"

# 3. Build, migrations e execução (comandos a partir de src/, onde está o global.json e a ferramenta dotnet-ef local)
cd src
dotnet tool restore
dotnet build ScheduleManager.slnx
dotnet ef database update --project Scheduling.Infrastructure --startup-project Scheduling.Api
dotnet run --project Scheduling.Api
```

Swagger UI: `https://localhost:7154/swagger` (perfil `https`) · Documento: `/openapi/v1.json`.
Opcional: `Database:ApplyMigrationsOnStartup=true` aplica as migrations automaticamente ao iniciar.

## Verificação obrigatória (Constituição V)

```bash
cd src
dotnet build ScheduleManager.slnx -warnaserror
dotnet test --project Scheduling.UnitTests
dotnet test --project Scheduling.IntegrationTests   # requer PostgreSQL acessível; cria e remove um banco temporário
```

`dotnet test` usa o Microsoft.Testing.Platform (opt-in em `src/global.json`). Os testes de integração **não** usam o
banco `postgres` de desenvolvimento: criam `schedulemanager_it_<guid>` e o removem ao final. Para outro servidor,
defina `SCHEDULEMANAGER_TEST_DB` (ex.: `Host=db;Port=5432;Database=postgres;Username=postgres;Password=...`).
Somente o teste de desempenho: `dotnet test --project Scheduling.IntegrationTests --filter-trait "Category=Performance"`.

**Esperado**: build sem erros/avisos; 100% dos testes aprovados; nenhum teste com `Skip`.

## Cenários de validação manual (caminho feliz)

Variáveis: `API=https://localhost:<porta>/api/v1`. Datas abaixo assumem uma **segunda-feira futura** dentro do horizonte (substitua `2026-11-16`).

| # | Passo | Chamada | Resultado esperado |
|---|---|---|---|
| 1 | Cadastrar prestador | `POST $API/auth/register/provider` (ver `RegisterProviderRequest`) | 201 + `accessToken` (`PROVIDER_TOKEN`) |
| 2 | Criar serviço 30 min | `POST $API/providers/me/services` `{ "name":"Consulta","durationMinutes":30,"price":150 }` | 201 + `id` (`SERVICE_ID`) |
| 3 | Definir expediente | `PUT $API/providers/me/schedule/working-hours` seg 09:00–12:00 e 13:00–18:00 | 200, `affectedAppointments: []` |
| 4 | Cadastrar cliente | `POST $API/auth/register/customer` | 201 + `CUSTOMER_TOKEN` |
| 5 | Consultar disponibilidade | `GET $API/providers/{PROVIDER_ID}/availability?serviceId=…&date=2026-11-16` | 200; slots 09:00…11:30 e 13:00…17:30 a cada 15 min, nenhum entre 11:31 e 12:59, todos com `-03:00` |
| 6 | Reservar 10:00 | `POST $API/appointments` com `Idempotency-Key` | 201, `status: Pending`, `ETag` presente |
| 7 | Reenviar mesma requisição/chave | idem | 200 com o **mesmo** `id` |
| 8 | Consultar de novo | passo 5 | 09:45, 10:00, 10:15 ausentes; 10:30 presente |
| 9 | Outro cliente reserva 10:15 | `POST $API/appointments` | 409 `appointment.slot_unavailable` (Problem Details) |
| 10 | Prestador confirma | `POST $API/appointments/{id}/confirm` | 200 `Confirmed`; notificação `AppointmentConfirmed` em `GET $API/notifications/me` do cliente |
| 11 | Cliente cancela (> 2h antes) | `POST $API/appointments/{id}/cancel` `{ "reason":"Imprevisto" }` | 200 `CancelledByCustomer`; 10:00 volta à disponibilidade |
| 12 | Exceção de férias | `POST $API/providers/me/schedule/exceptions` cobrindo a data | 201; disponibilidade da data → `slots: []` |
| 13 | Erro de validação | `GET …/availability?serviceId=…&date=16-11-2026` | 400 `validation.failed` com `errors.date` |
| 14 | Acesso indevido | cliente chama `POST $API/providers/me/services` | 403 `auth.forbidden` |

## Cenários automatizados obrigatórios (rastreio para tasks)

| Área | Suite | Prova |
|---|---|---|
| Matriz R1–R11 (inclui DST, adjacência, borda de faixa, preparo, antecedência, horizonte, grade) | Unit — `AvailabilityCalculatorTests` | Story 1/2, SC-004 |
| Máquina de estados e janelas de cancelamento/conclusão | Unit — `AppointmentTests` | Story 3 |
| Validators (todos os requests) | Unit — `*ValidatorTests` | FR-028 |
| 50 reservas paralelas mesmo horário → 1×201 + 49×409 | Integration — `BookingConcurrencyTests` | SC-001, FR-017 |
| Reservas paralelas **sobrepostas** (durações distintas) e **não conflitantes** | Integration — `BookingConcurrencyTests` | Edge cases |
| Conclusão × cancelamento concorrentes → 1×200 + 1×409 | Integration — `StatusConcurrencyTests` | FR-022 |
| Idempotência (mesma chave; payload diferente → 422) | Integration — `CreateAppointmentTests` | FR-023 |
| Autorização por perfil e propriedade em todos os endpoints | Integration — `AuthorizationTests` | SC-007 |
| Formato Problem Details em 400/401/403/404/409/412/422 | Integration — `ProblemDetailsTests` | SC-006 |
| Lembrete único com `FakeTimeProvider` (25h → avança 1h), sem lembrete para cancelados/criados < 24h | Integration — `ReminderTests` | Story 5, SC-005 |
| Disponibilidade p95 < 1 s com 500 agendamentos futuros | Integration (perf, categoria `Performance`) | SC-002 |
