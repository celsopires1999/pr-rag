## Why

O projeto roda integralmente em containers, e o host não possui `dotnet` instalado (compilação/testes só acontecem dentro de Docker). Isso dificulta o desenvolvimento iterativo: não há *debug* de código com breakpoints, navegabilidade de código/IntelliSense pelo SDK, nem um conjunto padronizado de ferramentas para novos contribuidores. Um DevContainer resolve isso ao fornecer um ambiente de desenvolvimento completo, reproduzível e pré-configurado (SDK + extensões + perfis de debug), diretamente dentro do VS Code / Codespaces.

## What Changes

- Adicionar `.devcontainer/devcontainer.json` definindo um contêiner de desenvolvimento baseado no SDK .NET 10, integrado ao `docker-compose.yml` existente (DB `pgvector` como serviço dependente).
- Adicionar `.devcontainer/Dockerfile` para provisionar ferramentas/utilitários dev ausentes no SDK base (já que o host não tem `dotnet`).
- Adicionar o conjunto de **extensões recomendadas** (C#, Microsoft.Extensions.AI/OpenAI apoio, Docker, PostgreSQL, etc.) instaladas automaticamente no contêiner.
- Configurar **debug pronto**: `launch.json` (launch com `dotnet run`/attach) e `tasks.json` (build/test) para depuração do `PrRag.Api` sem depender de host com SDK.
- Documentar no `README.md` como abrir o projeto via DevContainer (`devcontainer` / "Reopen in Container") e as opções de debug/test.
- Garantir que ao reabrir no contêiner, `dotnet restore`/`build`/`test` e `docker compose up` continuem funcionando como hoje.

## Capabilities

### New Capabilities
- `devcontainer-environment`: ambiente de desenvolvimento reproduzível via DevContainer com SDK .NET 10, extensões recomendadas e configuração de debug/execução pré-pronta.

### Modified Capabilities
- (nenhuma) — as capabilities funcionais existentes (`chat-query`, `data-ingestion`, `synthetic-data`) não mudam em nível de requisito; esta mudança é de infraestrutura de desenvolvimento.

## Impact

- **Novos arquivos**: `.devcontainer/devcontainer.json`, `.devcontainer/Dockerfile`, `.vscode/launch.json`, `.vscode/tasks.json`, `.vscode/extensions.json` (extensões recomendadas).
- **Modificados**: `README.md` (seção de desenvolvimento com DevContainer), `.gitignore` se necessário (ex.: artefatos locais do dev em volume).
- **Build/Docker**: usa o `docker-compose.yml` existente para o serviço `db` (`pgvector:pg18`); não altera os Dockerfiles de produção/teste existentes.
- **Ferramentas**: VS Code (ou Codespaces); sem dependência de `dotnet` no host — o DevContainer fornece o SDK.
