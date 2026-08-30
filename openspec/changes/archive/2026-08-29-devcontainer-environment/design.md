## Context

O projeto é uma solução .NET 10 (Api / Application / Infrastructure / Tests / DataGenerator) executada 100% em containers (ver `docker-compose.yml`: `db` = `pgvector/pgvector:pg18`, `api` = imagem publicada com o `Dockerfile`, `datagen` sob profile `tools`). O host de desenvolvimento **não possui `dotnet` instalado** e isso é intencional — toda compilação/teste acontece dentro de containers (`Dockerfile.test` + `docker-compose.test.yml`).

Essa restrição torna o desenvolvimento iterativo desconfortável: sem IntelliSense/sem navegação completa do SDK, sem debug com breakpoints, e sem um padrão claro de ferramentas para contribuidores. O objetivo deste design é prover um **DevContainer** que entrega o SDK .NET 10, extensões recomendadas e configuração de debug/execução pré-pronta, mantendo o modelo de containers já existente.

## Goals / Non-Goals

**Goals:**
- Fornecer um ambiente de desenvolvimento reproduzível via DevContainer (VS Code / Codespaces) com SDK .NET 10.
- Integrar o DevContainer ao `docker-compose.yml` existente, reutilizando o serviço `db` (`pgvector`).
- Instalar automaticamente as extensões recomendadas (C#/C# Dev Kit, Docker, PostgreSQL, MSBuild, etc.).
- Conter `launch.json` (debug do `PrRag.Api` via `dotnet run`) e `tasks.json` (restore/build/test) prontos, sem depender de SDK no host.
- Manter as funcionalidades existentes (chat/injestão/synthetic-data) intactas; esta mudança é de infraestrutura de desenvolvimento.

**Non-Goals:**
- Alterar os `Dockerfile`/`Dockerfile.test`/`Dockerfile.datagen` de produção/teste.
- Alterar comportamento da aplicação em runtime.
- Implementar depuração remota avançada (SSH/attach a processos fora do DevContainer), pipelines CI ou `dotnet` no host.
- Cobertura de outras IDEs além do VS Code/Codespaces.

## Decisions

### D1 — DevContainer único baseado no SDK .NET 10 com `dockerComposeFile`
**Decisão**: `devcontainer.json` usa `"image": "mcr.microsoft.com/devcontainers/dotnet:1-10.0"` (ou Dockerfile dedicado) e referencia o `docker-compose.yml` existente (`"dockerComposeFile": ["../docker-compose.yml"]`) adicionando o serviço `devcontainer` com `forwardPorts`, `workspaceFolder`, e integração ao `db`.
**Por quê**: reaproveita o `db` (`pgvector`) já provisionado — o banco sobe com o DevContainer e o app dev conecta a ele, eliminando duplicação de infra e mantendo um único arquivo compose como fonte de verdade.
**Alternativas consideradas**: imagem standalone sem compose (exigiria duplicar a config do db) — rejeitado por duplicação; DevContainer com instalação local de Postgres — rejeitado por divergir do modelo de containers do projeto.

### D2 — Tools dev via `.devcontainer/Dockerfile` (não apenas imagem upstream)
**Decisão**: caso a imagem base não inclua utilitários desejados (curl, git, psql, etc.), criar `.devcontainer/Dockerfile` que acrescenta essas ferramentas e chama `dotnet restore` do sln para pré-aquecer o cache de pacotes.
**Por quê**: garante conjunto de ferramentas determinístico e primeira experiência de build rápida, e não depende do host ter `dotnet`.
**Alternativas**: usar somente a imagem upstream com `features` do devcontainer — viável para ferramentas simples, mas um Dockerfile permite pré-restore e ajustes finos.

### D3 — Extensões recomendadas via `.vscode/extensions.json` + `customizations.vscode.extensions`
**Decisão**: definir o conjunto recomendado em dois lugares complementares: `customizations.vscode.extensions` no `devcontainer.json` (instalação automática no contêiner) e `extensions.json` (sugestão para quem já está fora do contêiner).
**Conjunto**: `ms-dotnettools.csharp`, `ms-dotnettools.csdevkit`, `ms-dotnettools.vscode-dotnet-runtime`, `ms-azuretools.vscode-docker`, `cweijan.vscode-postgresql-client2`, `EditorConfig.EditorConfig`.
**Por quê**: padroniza tooling para todos os contribuidores; as extensões C# fornecem IntelliSense/debug no SDK disponível dentro do contêiner.

### D4 — Debug e tarefas pré-configurados no VS Code
**Decisão**: adicionar `.vscode/tasks.json` (tarefas `build`, `watch`, `test`) e `.vscode/launch.json` (configuração `"PreRag.Api (DevContainer)"` que executa `dotnet run --project src/PrRag.Api --no-launch-profile` com a `db` já de pé via compose). `justMyCode` e env vars (`ConnectionStrings__Default`, `OpenAI__*`, `RAG__*`, `Data__FilePath`) apontando para o contêiner `db`.
**Por quê**: entrega "debug preparado" de forma out‑of‑the‑box — o F5 sobe o app dev ligado ao pgvector do compose, sem passo manual no host.
**Alternativas**: attach a processo já rodando no contêiner (`docker attach`) — mais complexo e menos ergonômico para iterar; mantido como opção futura, não padrão.

### D5 — Documentação do fluxo de desenvolvimento
**Decisão**: seção dedicada no `README.md` descrevendo "Reopen in Container", o serviço `db` associado, como rodar testes (`dotnet test` ou `docker compose --profile test up`), e como depurar (F5) sem `dotnet` no host.
**Por quê**: é o contrato com o usuário sobre como usar o ambiente; reduz fricção de onboarding.

## Risks / Trade-offs

- **Config interface compartilhada** → como `devcontainer.json` referencia o compose existente, um serviço novo (`devcontainer`) é acrescentado ao mesmo archivo; ajustes de porta/nome devem ser consonantes com `db`/`api`. → Mitigação: nomear claramente o serviço `devcontainer`, `forwardPorts` explícito e documentar no compose via comentário.
- **Imagem base / versão SDK desatualizada** → o DevContainer pode divergir da versão do `Dockerfile` de build se ambos não forem mantidos em sincronia. → Mitigação: fixar a mesma família `10.0`/`1-10.0` em ambos e adicionar tarefa/referência no README para atualizar juntos.
- **Segredos** → variáveis como `OpenAI__ApiKey` lidas do ambiente; o DevContainer não deve commitar nenhuma chave. → Mitigação: apontar env via `.env`/`${localEnv:*}` e manter `.env` no `.gitignore` (já existente).
- **Baixa adoção / divergência entre contributors** → repositórios com DevContainer reduzem, mas não eliminam, ambientes improvisados. → Mitigação: `extensions.json` + `devcontainer.json` + README cobrem o fluxo recomendado central.

## Migration Plan

1. Adicionar `.devcontainer/devcontainer.json` (+ `.devcontainer/Dockerfile` se necessário) e registrar o serviço `devcontainer` junto ao `docker-compose.yml`.
2. Adicionar `.vscode/extensions.json`, `.vscode/tasks.json`, `.vscode/launch.json`.
3. Atualizar `README.md` com o fluxo de desenvolvimento via DevContainer.
4. Rollback: remover os arquivos `.devcontainer`/`.vscode` e a entrada `devcontainer` do compose — nada em runtime é afetado.

## Open Questions

- Deve o DevContainer expor diretamente o serviço `api` publicado, ou apenas o app dev via `dotnet run`? (Provável resposta: apenas `dotnet run` para iteração + debug; `api` de produção continua via compose normal.)
