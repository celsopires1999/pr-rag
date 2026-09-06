## Why

The chat assistant currently only answers questions over purchase requisitions. Users performing a recurring task — like creating a new purchase requisition — get generic text back and must gather the required item codes, quantities, suppliers, and justification elsewhere. We want the assistant to recognize these recurring intents and run a guided workflow. Because the set of possible workflows is open-ended, the guidance must be defined as **extensible skills** (content, not code), so new skills can be added without a schema change or redeployment beyond a volume.

## What Changes

- Introduce a **skills capability** where a skill is a versioned, human-authored definition (markdown) with metadata, a trigger description, and step-by-step guidance for the assistant to follow.
- **Skill detection**: the chat model routes the user's request to a matching skill using a lightweight manifest (name + trigger description) provided to it; skills not matched leave the assistant in normal Q&A mode.
- **Skill execution**: when a skill matches, its instructions are injected into the conversation and the assistant guides the user through the skill's steps (asking for the data it needs, validating item/supplier codes with the existing retrieval tools, and producing the skill's deliverable, e.g. a draft purchase requisition).
- **Requisition creation tool**: a `create_requisition` tool lets the assistant persist a finished requisition as a JSON file under a writable `requisitions/` folder (bound-mounted, like `reports/`). The file carries exactly `SupplierCode`, `Item`, `Description`, `Quantity`, `Date`, and `Requester`.
- **BREAKING**: none — the `POST /api/chat` and `/api/chat/stream` contracts remain unchanged; unanswered free-form Q&A behavior is preserved when no skill matches.
- **Extensibility**: skills are loaded from a bind-mounted directory (mirroring the existing `data/purchase.json` pattern) and discovered at startup plus watched for changes, so new skills are added by dropping a file — no code changes.
- **Example skill shipped**: a `create-purchase-requisition` skill that guides the user through collecting item codes, quantities, unit of measure, supplier, expected delivery date, and justification, validating code references via the tool-based retrieval already in place, and persisting the confirmed requisition with the `create_requisition` tool.
- **Observability**: skill activation and progression are recorded in the existing RAG observability report.

## Capabilities

### New Capabilities

- `skill-framework`: loading, detection, activation, and guided execution of extensible skills in the chat assistant, including the bundled create-purchase-requisition skill.

### Modified Capabilities

- `chat-query`: the chat pipeline SHALL additionally detect when a request matches a registered skill and engage the skill's guided workflow instead of answering in free-form; behavior remains unchanged when no skill matches.

## Impact

- **PrRag.Application**: new `ISkillService`/skill model and repository abstractions (skill metadata + content loading), skill routing integrated into `ChatService` (system prompt construction and skill instructions), the `NewPurchaseRequisition` DTO and `IRequisitionWriter` abstraction, report DTO fields for skill activation/progression.
- **PrRag.Infrastructure**: `SkillsDirectory` loader + file watcher (new skill markdown set, e.g. `data/skills/`), `FileRequisitionWriter` that persists requisitions to a writable `requisitions/` directory, DI registration.
- **PrRag.Api**: no new endpoints expected; existing chat endpoints surface skill behavior transparently.
- **PrRag.DataGenerator / data**: no change; example skill shipped as content under the data volume.
- **Frontend**: no API changes; optional — chat UI may render the guided steps/questions as normal assistant messages (no UI work required for the backend capability).
- **Ops/Docker**: `data/skills/` added to the bind-mounted volume in `docker-compose.yml` (read-only inside the API container); a writable `requisitions/` bind mount is added for persisting requisitions, mirroring the `reports/` volume.