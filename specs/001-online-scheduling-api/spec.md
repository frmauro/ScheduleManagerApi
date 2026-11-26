# Feature Specification: API de Agendamento Online (ScheduleManager)

**Feature Branch**: `001-online-scheduling-api`

**Created**: 2026-11-09

**Status**: Draft

**Input**: User description: "Web API de Agendamento Online (SaaS multi-tenant ou sistema robusto de reservas) que gerencia agendas de prestadores de serviços (profissionais, clínicas, salões, consultórios) e permite que clientes finais marquem horários de forma autônoma, evitando conflitos de horários e gerando notificações. Módulos: Prestadores (cadastro, expediente, exceções, serviços), Clientes (cadastro, perfil, histórico, autenticação com perfis Admin/Prestador/Cliente), Motor de Agendamento (consulta de disponibilidade, criação, gestão de status, concorrência) e Notificações (confirmação e lembrete 24h antes, simulados)."

> **Escopo desta Spec**: descreve **o quê** o sistema deve fazer e **por quê** — atores, jornadas, regras de negócio, modelo conceitual de domínio e cenários de teste. Rotas, payloads JSON, contrato OpenAPI, modelo físico de dados e estratégia técnica de concorrência são artefatos da fase de planejamento (`/speckit-plan` → `plan.md`, `data-model.md`, `contracts/`) e serão derivados desta Spec, módulo por módulo.

## Clarifications

### Session 2026-11-09

- Q: Modelo de tenancy? → A: Plataforma única (marketplace), sem isolamento por organização (FR-032).
- Q: O que é um "Prestador"? → A: Profissional individual com uma única agenda (FR-033).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cliente consulta horários livres e agenda um serviço (Priority: P1)

Um cliente autenticado escolhe um prestador e um serviço, informa uma data e recebe a lista de horários de início disponíveis. Ele escolhe um horário e confirma a reserva. O sistema garante que aquele horário não esteja ocupado por outro agendamento e registra a reserva com o status inicial definido pelo prestador.

**Why this priority**: é o valor central do produto — sem consulta de disponibilidade e reserva sem conflito não existe sistema de agendamento.

**Independent Test**: com um prestador, um serviço e um expediente pré-cadastrados, um cliente consulta a disponibilidade de uma data, reserva um dos horários retornados e verifica que esse horário deixa de aparecer em uma nova consulta.

**Acceptance Scenarios**:

1. **Given** um prestador com expediente de segunda-feira das 09:00 às 12:00 e um serviço de 30 minutos, sem agendamentos, **When** o cliente consulta a disponibilidade de uma segunda-feira futura, **Then** recebe os horários 09:00, 09:15, 09:30 … 11:30 (passo padrão de 15 minutos; último início = 11:30, pois termina às 12:00).
2. **Given** o horário 10:00 disponível, **When** o cliente reserva o serviço às 10:00, **Then** o agendamento é criado com status `Pendente` (ou `Confirmado`, se o prestador habilita confirmação automática) e uma nova consulta não retorna nenhum início que se sobreponha a 10:00–10:30 (09:45, 10:00, 10:15 somem).
3. **Given** o horário 10:00 já reservado por outro cliente, **When** o cliente tenta reservar 10:00 (ex.: com uma lista de horários desatualizada), **Then** a reserva é recusada com um erro de conflito explícito e nenhum agendamento é criado.
4. **Given** um prestador com uma exceção de "férias" cobrindo a data consultada, **When** o cliente consulta a disponibilidade, **Then** recebe uma lista vazia, sem erro.
5. **Given** uma data no passado ou além do horizonte máximo de reserva, **When** o cliente consulta ou tenta reservar, **Then** o sistema rejeita com erro de validação descritivo.

---

### User Story 2 - Prestador configura perfil, serviços e expediente (Priority: P1)

Um prestador se cadastra, completa seu perfil (contato, endereço, categoria), cadastra os serviços que oferece (nome, descrição, duração, preço), define o expediente semanal (dias, faixas de horário, intervalos como almoço) e registra exceções (feriados, férias, horários especiais em datas pontuais).

**Why this priority**: é pré-requisito da Story 1 — sem expediente e serviços não há disponibilidade a calcular. Ambas formam o MVP.

**Independent Test**: um prestador cria um serviço e um expediente semanal com intervalo de almoço; a consulta de disponibilidade reflete exatamente essas configurações (inclusive a ausência de horários no almoço).

**Acceptance Scenarios**:

1. **Given** um prestador autenticado, **When** cadastra o serviço "Corte" com 45 minutos e preço 60,00, **Then** o serviço fica disponível para agendamento no seu perfil público.
2. **Given** um expediente de terça 08:00–12:00 e 13:00–18:00, **When** um cliente consulta terça para um serviço de 60 minutos, **Then** nenhum horário retornado se sobrepõe ao intervalo 12:00–13:00 (último início da manhã = 11:00).
3. **Given** faixas de expediente sobrepostas no mesmo dia (ex.: 08:00–12:00 e 11:00–14:00), **When** o prestador tenta salvá-las, **Then** o sistema rejeita com erro de validação.
4. **Given** uma exceção de feriado em 25/12, **When** um cliente consulta 25/12, **Then** a lista de horários é vazia.
5. **Given** uma exceção de "horário especial" em uma data (ex.: 09:00–13:00 num sábado normalmente fechado), **When** um cliente consulta essa data, **Then** os horários respeitam a faixa especial em vez do expediente semanal.
6. **Given** um serviço com agendamentos futuros ativos, **When** o prestador o desativa, **Then** o serviço deixa de aceitar novas reservas, mas os agendamentos existentes permanecem válidos.
7. **Given** um prestador que tenta alterar o perfil ou os serviços de outro prestador, **When** a operação é enviada, **Then** o acesso é negado.

---

### User Story 3 - Gestão do ciclo de vida do agendamento (Priority: P2)

O prestador confirma agendamentos pendentes, cancela quando necessário e marca como concluídos após o atendimento. O cliente pode cancelar seus próprios agendamentos dentro da política de antecedência. Cancelamentos liberam o horário imediatamente.

**Why this priority**: sem gestão de status, a agenda fica imprecisa (horários presos, atendimentos sem desfecho); é essencial à operação, mas vem depois de conseguir reservar.

**Independent Test**: criar um agendamento, percorrer as transições válidas (Pendente → Confirmado → Concluído; e Pendente/Confirmado → Cancelado) e verificar que transições inválidas são rejeitadas e que o cancelamento devolve o horário à disponibilidade.

**Acceptance Scenarios**:

1. **Given** um agendamento `Pendente`, **When** o prestador o confirma, **Then** o status passa para `Confirmado` e o cliente é notificado.
2. **Given** um agendamento `Confirmado` com início daqui a 48h, **When** o cliente o cancela, **Then** o status passa para `CanceladoPeloCliente` e o horário volta a aparecer na consulta de disponibilidade.
3. **Given** um agendamento com início em menos tempo que a antecedência mínima de cancelamento do prestador, **When** o cliente tenta cancelar, **Then** a operação é recusada com mensagem explicando a política.
4. **Given** um agendamento `Confirmado` cujo horário de início ainda não chegou, **When** o prestador tenta marcá-lo como `Concluído`, **Then** a operação é recusada.
5. **Given** um agendamento `Cancelado*` ou `Concluído`, **When** qualquer ator tenta alterar seu status, **Then** a operação é recusada (estados finais).
6. **Given** um cliente, **When** tenta alterar o status de um agendamento de outro cliente, **Then** o acesso é negado.

---

### User Story 4 - Cadastro, autenticação e perfis de acesso (Priority: P2)

Clientes e prestadores criam conta com e-mail e senha, fazem login e recebem uma credencial de acesso com validade limitada que identifica seu perfil (Admin, Prestador ou Cliente). Cada perfil só acessa as operações permitidas. O cliente mantém seu perfil (nome, e-mail, telefone) e consulta seu histórico de agendamentos.

**Why this priority**: necessário para identificar quem agenda e proteger dados, mas pode ser simulado (usuários pré-cadastrados) durante a validação do motor de agendamento.

**Independent Test**: registrar um cliente, autenticar, acessar o próprio histórico com sucesso e ter acesso negado a operações exclusivas de prestador/admin.

**Acceptance Scenarios**:

1. **Given** um e-mail ainda não cadastrado, **When** um visitante se registra como cliente com dados válidos, **Then** a conta é criada com o perfil Cliente.
2. **Given** um e-mail já cadastrado, **When** alguém tenta registrar outra conta com ele (ignorando maiúsculas/minúsculas), **Then** o cadastro é recusado.
3. **Given** credenciais inválidas, **When** o usuário tenta autenticar, **Then** recebe erro genérico que não revela se o e-mail existe.
4. **Given** um cliente autenticado, **When** consulta seu histórico, **Then** vê apenas seus agendamentos, paginados e filtráveis por status e período.
5. **Given** uma credencial expirada, **When** o usuário acessa uma operação protegida, **Then** recebe erro de não autenticado.
6. **Given** um Admin autenticado, **When** desativa um prestador, **Then** o prestador some das buscas públicas e não aceita novas reservas.

---

### User Story 5 - Notificações de confirmação e lembrete (Priority: P3)

O sistema gera notificações para o cliente (e para o prestador quando pertinente) na criação, confirmação e cancelamento de agendamentos, e um lembrete 24h antes do início de agendamentos confirmados. Nesta etapa, o envio é simulado: as notificações são registradas e consultáveis, sem integração com provedores reais de e-mail/SMS.

**Why this priority**: melhora a experiência e reduz faltas, mas o sistema funciona sem ela.

**Independent Test**: criar e confirmar um agendamento para daqui a 25h, avançar o relógio de teste 1h e verificar que exatamente um lembrete foi registrado; cancelar outro agendamento e verificar que nenhum lembrete é gerado para ele.

**Acceptance Scenarios**:

1. **Given** um agendamento criado, **When** a reserva é concluída, **Then** uma notificação de "agendamento recebido/confirmado" é registrada para o cliente e uma de "novo agendamento" para o prestador.
2. **Given** um agendamento `Confirmado` com início em 24h, **When** o processo de lembretes executa, **Then** exatamente um lembrete é registrado, mesmo que o processo execute várias vezes.
3. **Given** um agendamento cancelado antes da janela de lembrete, **When** o processo de lembretes executa, **Then** nenhum lembrete é gerado.
4. **Given** um agendamento criado com menos de 24h de antecedência, **When** o processo de lembretes executa, **Then** nenhum lembrete adicional é gerado (a confirmação já cumpre o papel).
5. **Given** uma falha simulada de entrega, **When** a notificação é processada, **Then** ela fica registrada como falha, com tentativas limitadas, sem afetar o agendamento.

---

### Edge Cases

**Disponibilidade e horários inválidos**

- Serviço mais longo que qualquer faixa do dia (ex.: 4h em faixas de 3h) → lista vazia, sem erro.
- Serviço que terminaria exatamente no fim da faixa (11:30 + 30 min = 12:00) → horário válido; que ultrapassaria em 1 minuto → inválido.
- Agendamentos adjacentes (um termina 10:30, outro começa 10:30) → permitidos (intervalos semiabertos `[início, fim)`).
- Consulta de "hoje" → excluir horários já passados e os que violam a antecedência mínima de reserva.
- Reserva com horário de início fora da grade de passo (ex.: 10:07 com passo de 15 min) → rejeitada.
- Reserva para horário dentro do intervalo de almoço, em exceção de fechamento, em dia sem expediente ou além do horizonte → rejeitada com motivo específico.
- Serviço ou prestador inativo, ou serviço que não pertence ao prestador informado → rejeitado.
- Data inválida, formato inválido ou duração de serviço ≤ 0 / não múltipla de 5 minutos → erro de validação.
- Mudança de horário de verão / fuso: horários calculados no fuso do prestador; horários inexistentes (salto) são omitidos e horários ambíguos (retrocesso) não geram duplicidade.
- Cliente em fuso diferente do prestador → horários exibidos sempre com indicação explícita de fuso/deslocamento.
- Prestador altera expediente ou cria exceção sobre data com agendamentos já existentes → agendamentos existentes NÃO são cancelados automaticamente; o sistema avisa o prestador da lista de agendamentos afetados.
- Duração do serviço alterada após reservas → agendamentos existentes mantêm a duração/preço do momento da reserva (snapshot).

**Concorrência**

- Dois clientes reservam o mesmo prestador, mesmo horário, no mesmo instante → exatamente um sucesso; o outro recebe erro de conflito.
- Duas reservas com horários diferentes mas sobrepostos (10:00–10:45 e 10:30–11:00) simultâneas → exatamente uma é aceita.
- N (ex.: 50) requisições simultâneas para o mesmo horário → exatamente 1 agendamento persistido.
- Reservas simultâneas em horários não conflitantes do mesmo prestador → todas aceitas (sem bloqueio desnecessário).
- Cancelamento e nova reserva concorrentes no mesmo horário → resultado consistente (nunca dois ativos sobrepostos).
- Duas alterações de status concorrentes no mesmo agendamento (ex.: prestador conclui enquanto cliente cancela) → apenas a primeira é aplicada; a segunda recebe erro de conflito de versão.
- Mesmo cliente submete a mesma reserva duas vezes (duplo clique / reenvio) → no máximo um agendamento criado.

## Requirements *(mandatory)*

### Functional Requirements

**Prestadores e estabelecimentos**

- **FR-001**: O sistema MUST permitir que um prestador cadastre e mantenha seu perfil: nome de exibição, descrição, e-mail e telefone de contato, endereço, categoria de serviço e fuso horário.
- **FR-002**: O sistema MUST permitir que o prestador cadastre, edite, ative e desative serviços com nome, descrição, duração em minutos (> 0, múltiplo de 5, máx. 720) e preço (≥ 0, moeda única).
- **FR-003**: O sistema MUST permitir que o prestador defina um expediente semanal com uma ou mais faixas de horário por dia da semana; lacunas entre faixas representam intervalos (ex.: almoço). Faixas do mesmo dia não podem se sobrepor e o início deve ser anterior ao fim.
- **FR-004**: O sistema MUST permitir exceções por data ou período: **fechamento** (feriado, férias, folga — dia inteiro ou faixa parcial) e **horário especial** (substitui o expediente semanal naquela data).
- **FR-005**: O sistema MUST permitir que o prestador configure suas regras de reserva: confirmação automática (sim/não), passo da grade de horários, intervalo de preparo entre atendimentos, antecedência mínima de reserva, horizonte máximo de reserva e antecedência mínima de cancelamento pelo cliente (padrões em Assumptions).
- **FR-006**: O sistema MUST oferecer listagem pública de prestadores ativos e seus serviços ativos, com paginação e filtros por categoria e texto (nome/cidade).
- **FR-007**: Ao alterar expediente ou criar exceção que afete agendamentos ativos futuros, o sistema MUST informar ao prestador os agendamentos afetados, sem cancelá-los automaticamente.

**Clientes e autenticação**

- **FR-008**: O sistema MUST permitir o auto-cadastro de clientes e de prestadores com nome, e-mail (único, sem distinção de maiúsculas) e senha; clientes informam também telefone.
- **FR-009**: O sistema MUST autenticar usuários por e-mail e senha e emitir uma credencial de acesso com validade limitada que carrega o perfil do usuário (Admin, Prestador, Cliente).
- **FR-010**: O sistema MUST aplicar autorização por perfil e por propriedade: clientes acessam apenas seus dados e agendamentos; prestadores apenas seu perfil, serviços, expediente e agendamentos recebidos; Admin acessa todos e pode ativar/desativar contas.
- **FR-011**: Contas Admin MUST NOT ser criadas por auto-cadastro.
- **FR-012**: O sistema MUST permitir que o cliente consulte e atualize seu perfil e veja o histórico de agendamentos com paginação e filtros (status, período, prestador).
- **FR-013**: Senhas MUST seguir política mínima (≥ 8 caracteres, com letras e números) e nunca ser armazenadas ou retornadas em forma legível; mensagens de falha de login MUST ser genéricas.

**Motor de agendamento**

- **FR-014**: O sistema MUST retornar, para (prestador, serviço, data), a lista de horários de início disponíveis calculada conforme a *Matriz de Regras de Disponibilidade* (abaixo).
- **FR-015**: O sistema MUST permitir que um cliente crie um agendamento informando prestador, serviço e horário de início; o fim é derivado da duração do serviço.
- **FR-016**: Na criação, o sistema MUST revalidar todas as regras de disponibilidade (a lista consultada não é uma reserva) e recusar com erro de conflito se o horário não estiver mais livre.
- **FR-017**: O sistema MUST garantir, mesmo sob requisições simultâneas, que nunca existam dois agendamentos em status bloqueante (`Pendente`, `Confirmado`) sobrepostos para o mesmo prestador (considerando o intervalo de preparo).
- **FR-018**: O sistema MUST impedir que um mesmo cliente tenha dois agendamentos ativos sobrepostos no tempo (em qualquer prestador).
- **FR-019**: O status inicial MUST ser `Confirmado` se o prestador habilitou confirmação automática, caso contrário `Pendente`.
- **FR-020**: O agendamento MUST registrar um retrato (snapshot) do nome, duração e preço do serviço no momento da reserva.
- **FR-021**: O sistema MUST aplicar a máquina de estados do agendamento (seção *Ciclo de vida*), rejeitando transições não permitidas e registrando quem alterou, quando e o motivo (motivo obrigatório em cancelamentos).
- **FR-022**: Atualizações concorrentes sobre o mesmo agendamento MUST ser detectadas; apenas a primeira é aplicada e a seguinte recebe erro de conflito.
- **FR-023**: Requisições repetidas de criação de agendamento (reenvio da mesma solicitação) MUST resultar em no máximo um agendamento.
- **FR-024**: O prestador MUST poder listar seus agendamentos (agenda) com paginação e filtros por período, status e serviço.

**Notificações**

- **FR-025**: O sistema MUST gerar notificações nos eventos: agendamento criado, confirmado, cancelado (por qualquer parte) e lembrete.
- **FR-026**: O sistema MUST gerar um lembrete único por agendamento `Confirmado` quando faltarem 24h para o início; agendamentos criados com menos de 24h de antecedência ou cancelados não recebem lembrete.
- **FR-027**: O envio MUST ser abstraído por um canal substituível; nesta etapa o canal é simulado e cada notificação é registrada com destinatário, tipo, conteúdo, status (pendente, enviada, falha), tentativas e data. Falhas de notificação MUST NOT desfazer ou bloquear operações de agendamento.

**Padrões transversais da API**

- **FR-028**: Todas as respostas de erro MUST seguir um formato padronizado legível por máquina, com tipo, título, status, detalhe e, para validação, a lista de campos inválidos; detalhes internos nunca são expostos.
- **FR-029**: Todas as listagens MUST suportar paginação padronizada (página, tamanho com máximo de 100, total de itens) e ordenação documentada.
- **FR-030**: A API MUST ser versionada (versão 1 nesta entrega) e documentada em formato interativo descrevendo todas as operações, payloads e erros.
- **FR-031**: Todos os horários trafegados MUST carregar indicação explícita de fuso/deslocamento; o armazenamento usa referência de tempo universal.

**Multi-tenancy e estrutura do prestador**

- **FR-032**: O sistema MUST operar como **plataforma única (marketplace)**: todos os prestadores ativos compõem um catálogo público comum, sem isolamento de dados por organização/assinante. O isolamento de dados é garantido apenas pelas regras de autorização por perfil e propriedade (FR-010).
- **FR-033**: Um "Prestador" MUST representar **um único profissional (ou estabelecimento tratado como uma unidade) com uma única agenda**: um expediente, um conjunto de serviços e no máximo um atendimento por vez (FR-017).

### Matriz de Regras de Disponibilidade *(regras de negócio críticas)*

Entradas: prestador **P**, serviço **S** (duração **d**), data **D** (no fuso de P). Parâmetros de P: passo **g**, intervalo de preparo **b**, antecedência mínima **m**, horizonte **h**.

| # | Regra | Efeito |
|---|-------|--------|
| R1 | P e S existem, estão ativos e S pertence a P | Caso contrário: erro (não encontrado / inválido) |
| R2 | D ∈ [hoje, hoje + h] no fuso de P | Fora disso: erro de validação |
| R3 | Faixas base do dia = exceção de **horário especial** de D, se houver; senão, faixas do expediente semanal do dia da semana de D | Sem faixas → lista vazia |
| R4 | Subtrair das faixas base todas as exceções de **fechamento** que cobrem D (dia inteiro remove tudo; parcial recorta) | Pode gerar lista vazia |
| R5 | Candidatos: inícios `t = início_faixa + k·g` (k ≥ 0) dentro de cada faixa resultante, com `t + d ≤ fim_faixa` | Serviço nunca atravessa intervalo/almoço ou fim de expediente |
| R6 | Ocupação: cada agendamento de P em status `Pendente`/`Confirmado` ocupa `[início, fim + b)` | Cancelados e concluídos não bloqueiam |
| R7 | Candidato válido somente se `[t, t + d + b)` não intersecta nenhuma ocupação (intervalos semiabertos; adjacência permitida) | Evita overbooking |
| R8 | Candidato válido somente se `t ≥ agora + m` | Remove horários passados e de última hora |
| R9 | Horários inexistentes por transição de horário de verão são descartados; ambíguos usam o primeiro deslocamento | Sem duplicidade |
| R10 | Resultado ordenado crescente, sem duplicatas, cada item com início e fim com deslocamento de fuso | Saída determinística |
| R11 | Na criação, R1–R9 são reavaliados atomicamente com a gravação; também verifica-se `t` alinhado à grade e FR-018 | Conflito → erro de conflito |

### Ciclo de vida do agendamento

| De \ Para | Confirmado | CanceladoPeloCliente | CanceladoPeloPrestador | Concluído |
|-----------|------------|----------------------|------------------------|-----------|
| **Pendente** | Prestador, Admin | Cliente (respeitando antecedência mínima), Admin | Prestador, Admin | — |
| **Confirmado** | — | Cliente (respeitando antecedência mínima), Admin | Prestador, Admin | Prestador, Admin (somente após o início) |
| **Cancelado\*** / **Concluído** | estados finais — nenhuma transição | | | |

### Key Entities *(include if feature involves data)*

- **User (Usuário)**: identidade de acesso. Atributos: identificador, nome, e-mail (único), credencial de senha protegida, perfil (Admin/Prestador/Cliente), ativo, datas de criação/atualização. Um User com perfil Prestador possui um Provider; com perfil Cliente possui um Customer.
- **Provider (Prestador)**: quem oferece serviços. Atributos: nome de exibição, descrição, categoria, contato (e-mail, telefone), endereço (logradouro, número, complemento, bairro, cidade, UF, CEP), fuso horário, regras de reserva (confirmação automática, passo, preparo, antecedências, horizonte), ativo. Possui muitos Services, um Schedule e muitos Appointments.
- **Customer (Cliente)**: perfil do cliente final. Atributos: nome, e-mail, telefone. Possui muitos Appointments (histórico).
- **Service (Serviço)**: oferta do prestador. Atributos: nome (único por prestador), descrição, duração em minutos, preço, ativo. Pertence a um Provider.
- **Schedule (Expediente)**: agregado da disponibilidade do prestador, composto por:
  - **WorkingHours (Faixa semanal)**: dia da semana, hora de início, hora de fim (sem sobreposição no mesmo dia).
  - **ScheduleException (Exceção)**: tipo (fechamento | horário especial), data/período, faixa opcional, motivo.
- **Appointment (Agendamento)**: reserva. Atributos: prestador, cliente, serviço, snapshot (nome, duração, preço), início e fim, status, observações do cliente, motivo de cancelamento, histórico de mudanças de status (quem, quando, de/para), marcador de versão para detectar alterações concorrentes. Invariantes: fim > início; sem sobreposição com outro ativo do mesmo prestador (FR-017) ou do mesmo cliente (FR-018).
- **Notification (Notificação)**: registro de comunicação. Atributos: agendamento, destinatário, canal, tipo (criado, confirmado, cancelado, lembrete), conteúdo, status (pendente/enviada/falha), tentativas, agendada para, enviada em. Unicidade de (agendamento, tipo lembrete).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero agendamentos sobrepostos para um mesmo prestador em testes de carga com 50 tentativas simultâneas de reserva do mesmo horário — exatamente 1 sucesso e 49 recusas por conflito, em 100% das execuções.
- **SC-002**: 95% das consultas de disponibilidade retornam em até 1 segundo para prestadores com até 500 agendamentos futuros.
- **SC-003**: Um cliente consegue encontrar um horário e concluir uma reserva em até 3 interações (escolher serviço/data, escolher horário, confirmar) e em menos de 2 minutos.
- **SC-004**: 100% dos horários retornados pela consulta de disponibilidade são aceitos na reserva imediatamente seguinte quando não há concorrência (consistência entre consulta e criação).
- **SC-005**: 99% dos lembretes são registrados em até 15 minutos após o marco de 24h antes do início, com zero lembretes duplicados.
- **SC-006**: 100% das respostas de erro seguem o formato padronizado e nenhuma expõe detalhes internos.
- **SC-007**: 100% das tentativas de acesso a dados de outro usuário/perfil são negadas nos testes de autorização.
- **SC-008**: Todos os cenários de aceitação e casos de borda desta Spec possuem testes automatizados unitários e de integração aprovados (conforme Constituição, Princípio V).

## Assumptions

- **Passo da grade** padrão de 15 minutos (configurável: 5–60); **intervalo de preparo** padrão 0 min.
- **Antecedência mínima de reserva** padrão 60 minutos; **horizonte** padrão 60 dias; **antecedência mínima de cancelamento pelo cliente** padrão 2 horas. Prestador e Admin podem cancelar a qualquer momento antes do início.
- **Confirmação automática** desativada por padrão (status inicial `Pendente`); agendamentos `Pendente` bloqueiam o horário e não expiram automaticamente nesta versão.
- Cada prestador possui **um único fuso horário**; datas de consulta são interpretadas nesse fuso.
- Um agendamento contém **um único serviço**; pacotes/combos e serviços em sequência estão fora do escopo.
- **Fora do escopo** nesta etapa: multi-tenancy com isolamento por organização, estabelecimentos com múltiplos profissionais/agendas e atendimentos paralelos, pagamentos/sinal, reagendamento como operação própria (equivale a cancelar + criar), lista de espera, recorrência de agendamentos, status "não compareceu", avaliações, integração real de e-mail/SMS/WhatsApp, agendamento por visitante sem conta, login social/recuperação de senha.
- Moeda única (BRL) e idioma principal pt-BR para mensagens de usuário.
- Usuário Admin inicial é provisionado na implantação.
- Dados pessoais seguem boas práticas de privacidade (LGPD): prestador vê dados de contato do cliente somente nos agendamentos que recebeu.
- Restrições técnicas (stack, arquitetura em camadas, validação, formato de erro RFC 7807, testes) são definidas pela Constituição do projeto e detalhadas em `/speckit-plan`; contratos de API (rotas, payloads, OpenAPI) serão gerados em `contracts/` na fase de planejamento.
