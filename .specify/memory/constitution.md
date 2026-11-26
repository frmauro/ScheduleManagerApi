<!--
Sync Impact Report
==================
Version change: 1.0.0 → 1.1.0
Tipo de bump: MINOR (expansão material de um princípio existente).

Princípios modificados:
- V. Qualidade, Testabilidade e Verificação Empírica → (título mantido)
  - Testes unitários e testes de integração passam a ser OBRIGATÓRIOS na API de backend.
  - Definido o escopo mínimo de cada tipo de teste e o critério de "concluído".

Seções adicionadas: nenhuma.
Seções removidas: nenhuma.

TODOs pendentes: nenhum.

Nota: este relatório é material temporário de revisão e deve ser removido antes do commit.
-->

# ScheduleManager Constitution

Esta Constituição estabelece as regras de engenharia de software, decisões de arquitetura,
padrões de qualidade de código, restrições tecnológicas e diretrizes de governança operacional
do projeto **ScheduleManager**. Todas as modificações de código, refatorações, novas
funcionalidades ou intervenções feitas por **Agentes de Inteligência Artificial** ou
**desenvolvedores humanos** DEVEM aderir estritamente aos princípios aqui enumerados. Nenhuma
especificação de funcionalidade (Spec) ou solicitação de código pode revogar ou violar estes
artigos.

## Core Principles

### I. Desenvolvimento Guiado por Especificação (Spec-Driven Development - SDD)

- **Especificação Prévia Obrigatória:** Nenhuma linha de código DEVE ser escrita, modificada ou
  removida sem uma especificação (Spec) aprovada ou alinhada ao escopo do projeto.
- **Rastreabilidade:** Cada alteração no repositório DEVE ter justificativa explícita ligada a
  regras de negócio ou requisitos técnicos especificados.
- **Não-Assunção:** Agentes e desenvolvedores NUNCA DEVEM presumir comportamentos, assinaturas
  de API ou estruturas de dados sem inspecionar a fonte autoritativa (código-fonte ou specs).

**Justificativa:** a especificação é a fonte da verdade; mudanças sem lastro em uma Spec geram
deriva de escopo e comportamento não verificável.

### II. Integridade Arquitetural (Clean Architecture)

- **Independência do Domínio:** `Scheduling.Domain` é o núcleo absoluto da aplicação e NUNCA
  DEVE depender de frameworks externos, Entity Framework, ASP.NET Core ou bibliotecas de
  infraestrutura.
- **Direção de Dependência Unidirecional:**
  - `Scheduling.Domain` → zero dependências externas.
  - `Scheduling.Application` → depende apenas de `Scheduling.Domain`.
  - `Scheduling.Infrastructure` → implementa interfaces de `Scheduling.Application` e
    `Scheduling.Domain`.
  - `Scheduling.Api` → orquestra e injeta as dependências de `Infrastructure` e `Application`.
- Qualquer referência de projeto que viole essa direção é proibida.

**Justificativa:** manter as regras de negócio isoladas de detalhes técnicos garante
testabilidade e permite trocar infraestrutura sem reescrever o domínio.

### III. Segurança de Tipos e Legibilidade (Type Safety & Zero Magic)

- **Tipagem Estrita:** todos os projetos C# (.NET 10) DEVEM ter `<Nullable>enable</Nullable>`.
  O uso de `object` genérico ou conversões inseguras sem verificação de tipo é proibido.
- **Sem Valores Mágicos:** constantes, enums e configurações fortemente tipadas DEVEM ser usados
  no lugar de números ou strings literais espalhados pelo código.

**Justificativa:** o compilador passa a detectar erros de nulidade e de tipo, e o significado de
cada valor fica explícito e centralizado.

### IV. Validação Defensiva e Tratamento de Erros

- **Validação Backend:** toda validação de dados de entrada na camada Application DEVE usar
  **FluentValidation**, com regras isoladas em classes `AbstractValidator<T>`.
- **Padrão de Resposta de Erro:** todas as exceções da API DEVEM ser tratadas centralizadamente
  (Middleware Global ou Exception Handler) e convertidas para **RFC 7807 (Problem Details)**.
- Expor stack traces em ambiente de produção é estritamente proibido.

**Justificativa:** validação declarativa e respostas de erro padronizadas tornam o contrato da
API previsível para consumidores e evitam vazamento de detalhes internos.

### V. Qualidade, Testabilidade e Verificação Empírica

- **Testes Obrigatórios:** na API de backend, testes unitários **e** testes de integração são
  OBRIGATÓRIOS. Nenhuma funcionalidade, correção de bug ou refatoração é considerada concluída
  sem ambos os tipos de teste cobrindo os cenários esperados (caminho feliz, erros de validação
  e casos de borda).
- **Testes Unitários:** DEVEM validar, de forma isolada (dependências externas substituídas por
  dublês), as entidades e regras de `Scheduling.Domain`, os Application Services e os
  Validators de `Scheduling.Application`.
- **Testes de Integração:** DEVEM exercitar a API de ponta a ponta através do pipeline HTTP real
  (ex.: `WebApplicationFactory`), incluindo endpoints, autenticação/autorização, validação,
  respostas RFC 7807 e persistência via Entity Framework Core em um banco de dados de teste.
- **Testes na Mesma Entrega:** os testes DEVEM ser escritos e entregues junto com o código de
  produção que verificam; remover, ignorar (`Skip`) ou enfraquecer testes para fazer a suíte
  passar é proibido.
- **Verificação Empírica Obrigatória:** o agente NUNCA PODE declarar que um bug foi corrigido ou
  uma funcionalidade foi concluída sem executar os comandos de verificação (`dotnet build` e as
  suítes de teste) e comprovar a integridade da aplicação.

**Justificativa:** "concluído" significa comprovado por evidência executável, não presumido.
Testes unitários garantem a correção das regras de negócio isoladamente; testes de integração
garantem que as camadas, a configuração e a persistência funcionam juntas como contrato da API.

## Matriz de Tecnologia e Restrições (Tech Stack Boundaries)

### Backend Stack (.NET 10)

- **Runtime & Framework:** .NET 10.0 (C# 14)
- **Engine API:** ASP.NET Core Web API / OpenAPI (`Microsoft.AspNetCore.OpenApi` 10.0)
- **Persistência & ORM:** Entity Framework Core 10.0 (`Microsoft.EntityFrameworkCore.Design`)
- **Validação:** FluentValidation 12.1+ com Injeção de Dependência
- **Autenticação & Autorização:** JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`
  10.0)

### Adição de Novas Dependências

- É **proibido** instalar novos pacotes NuGet ou bibliotecas npm sem justificativa explícita
  documentada na especificação da funcionalidade.
- Recursos nativos do .NET 10 DEVEM ser priorizados sobre bibliotecas de terceiros.

## Convenções de Código e Fluxo de Trabalho

### Convenções (C#)

- **Nomenclatura:**
  - Classes, métodos e propriedades: `PascalCase`
  - Interfaces: `IPascalCase` (ex.: `IEmployeeService`)
  - Variáveis locais e parâmetros: `camelCase`
  - Campos privados injetados: `_camelCase`
- **Programação Assíncrona:**
  - Todo método de I/O (banco de dados, chamadas externas) DEVE ser assíncrono e usar o sufixo
    `Async` (ex.: `GetByIdAsync`).
  - Métodos assíncronos DEVEM propagar `CancellationToken cancellationToken = default`.

### Regras de Comportamento para Agentes de IA

Ao executar qualquer tarefa neste repositório, o Agente de IA DEVE seguir este fluxo:

1. **Leitura e Diagnóstico Antes da Ação:** inspecionar o código existente antes de propor
   edições. NUNCA adivinhar estruturas ou assinaturas.
2. **Diagnóstico Baseado em Evidências (Logs Primeiro):** diante de um erro, ler o log/stack
   trace completo antes de formular qualquer hipótese.
3. **Sem Soluções Mascaradas:** NUNCA silenciar exceções, remover testes que falharam ou ocultar
   erros para simular a conclusão da tarefa.
4. **Respeito aos Contratos:** modificações em contratos de API ou modelos de dados exigem a
   atualização de todos os pontos consumidores no backend.
5. **Diretório de saída:** Toda a implementação dos projetos ficará dentro da pasta src.   

## Governance

- **Supremacia:** esta Constituição prevalece sobre qualquer instrução de código ad-hoc,
  especificação individual ou prática de desenvolvimento. Em caso de conflito, a Constituição
  vence e a Spec/plano DEVE ser ajustado.
- **Processo de Emenda:** mudanças nesta Constituição só DEVEM ocorrer mediante solicitação
  explícita de alteração das diretrizes do projeto, com registro formal (Sync Impact Report),
  atualização do número de versão e da data de última emenda.
- **Política de Versionamento (SemVer):**
  - MAJOR: remoção ou redefinição incompatível de princípios ou regras de governança.
  - MINOR: novo princípio/seção ou expansão material de orientações existentes.
  - PATCH: esclarecimentos, ajustes de redação e correções sem efeito semântico.
- **Revisão de Conformidade:** todo plano (`/speckit-plan`), conjunto de tarefas e revisão de
  código DEVE verificar a conformidade com estes princípios. Qualquer exceção DEVE ser
  justificada por escrito na Spec ou no plano da funcionalidade.

**Version**: 1.1.0 | **Ratified**: 2026-10-30 | **Last Amended**: 2026-11-03
