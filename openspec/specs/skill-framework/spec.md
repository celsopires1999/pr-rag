# Skill Framework

## Purpose

A skill catalog that lets the chat model route guided, task-specific workflows (such as creating a purchase requisition) instead of free-form answers, using markdown skill files with YAML front-matter that are discovered at startup and activated via tool calls without code changes.

## Requirements

### Requirement: Skill discovery and loading
The system SHALL discover skills as markdown files with YAML front-matter (id/name, trigger `description`, `version`) located in the configured skills directory (default `/data/skills`), and SHALL load them into an in-memory manifest at startup without failing when the directory is absent or empty.

#### Scenario: Skills loaded at startup
- **WHEN** the system starts with a skills directory containing one markdown file per skill
- **THEN** each valid file is parsed and registered in the skill manifest with its name, description and version

#### Scenario: Missing or empty skills directory
- **WHEN** the system starts without a skills directory or with an empty directory
- **THEN** the system starts normally with an empty skill manifest and no skill behavior is available

### Requirement: Skill manifest exposed to the chat model
The system SHALL expose the skill manifest — each skill's name and trigger description — in the system prompt so the chat model can decide when a request matches a skill.

#### Scenario: Manifest rendered into the system prompt
- **WHEN** the system constructs the system prompt for a chat request
- **THEN** the prompt lists every loaded skill with its name and description in a dedicated Skills section

#### Scenario: Manifest reflects newly loaded skills
- **WHEN** skills are reloaded and the set of loaded skills changes
- **THEN** the system prompt for subsequent requests reflects the updated manifest

### Requirement: Skill activation via tool call
The system SHALL expose an `activate_skill` tool that, when called with a skill name by the chat model, returns that skill's guidance body to the conversation; a call for an unknown skill SHALL return an error listing the available skills.

#### Scenario: Model activates a known skill
- **WHEN** the chat model calls `activate_skill` with the name of a loaded skill
- **THEN** the tool returns the skill's guidance body and the model follows it to guide the conversation

#### Scenario: Model activates an unknown skill
- **WHEN** the chat model calls `activate_skill` with a name not present in the manifest
- **THEN** the tool returns a message stating the skill is unknown and lists the available skill names

### Requirement: Skill guidance persists across turns
The system SHALL keep an activated skill's guidance in effect for subsequent turns of the same conversation by re-injecting its instructions when conversation history indicates that a skill was active. While a skill is active, the assistant's reply SHALL begin with a `[Skill: <skill-name>]` marker line — the system prompt instructs the model to emit it and the system enforces it on responses — and the marker SHALL be dropped once the skill's workflow is complete.

#### Scenario: Guidance restored on a following turn
- **WHEN** a client sends a follow-up stream request whose history contains an assistant message starting with `[Skill: <loaded-skill-name>]`
- **THEN** the system injects that skill's guidance into the conversation before the latest user turn, so the workflow continues

#### Scenario: Guidance absent without prior activation
- **WHEN** a conversation never activated a skill
- **THEN** no skill guidance is injected and the assistant behaves as normal Q&A

### Requirement: Skills guide without granting new capabilities
A skill SHALL only add conversational instructions, and SHALL NOT add, remove, or change the tool functions available to the chat model.

#### Scenario: Tool set unchanged during a skill
- **WHEN** a skill is active in a conversation
- **THEN** the tools available to the model remain the fixed framework set (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`), with no tools added, removed, or changed by the skill

### Requirement: No-skill graceful behavior
The system SHALL preserve normal Q&A chat behavior when no loaded skill matches the user's request.

#### Scenario: Plain question with no matching skill
- **WHEN** a user sends a question that does not match any loaded skill
- **THEN** the assistant answers in free-form using the existing retrieval pipeline without skill guidance

### Requirement: Skill usage observability
The system SHALL record skill activation in the RAG observability report for requests where a skill is active.

#### Scenario: Report reflects active skill
- **WHEN** a chat request is answered while a skill is active
- **THEN** the observability report for that request includes the skill id, skill name, and an activation flag

#### Scenario: Report without skill
- **WHEN** a chat request is answered with no skill active
- **THEN** the observability report for that request carries no skill id/name and the activation flag is false

### Requirement: Extensibility of the skill catalog
The system SHALL make any markdown skill file placed in the skills directory available for activation without code changes or a database migration.

#### Scenario: New skill file becomes available
- **WHEN** a new valid skill markdown file is added to the skills directory and reloaded
- **THEN** the skill appears in the manifest and can be activated by the model in subsequent conversations

### Requirement: Purchase requisition file creation tool
The system SHALL expose a `create_requisition` tool that persists a confirmed purchase requisition as a JSON file under the configured requisitions directory (default `./requisitions`, set via the `Requisitions__Directory` environment variable and resolving to `/app/requisitions` inside the API image), with the file containing exactly `SupplierCode`, `Item`, `Description`, `Quantity`, `Date`, and `Requester`. The tool SHALL refuse to write when required fields are missing or invalid, and SHALL be described to the model as callable only after explicit user confirmation of a drafted requisition.

#### Scenario: Confirmed requisition written to disk
- **WHEN** the model calls `create_requisition` with all six fields present and valid
- **THEN** the system writes a JSON file to the requisitions directory containing exactly those fields and returns the created file name to the model

#### Scenario: Required field missing or invalid
- **WHEN** the model calls `create_requisition` with a missing required field or an invalid value (e.g. non-numeric `Quantity`, unparseable `Date`)
- **THEN** the system does not write any file and returns an error describing the invalid or missing fields

### Requirement: Purchase requisition creation skill
The system SHALL ship a `create-purchase-requisition` skill that guides the user step by step through drafting a new purchase requisition — collecting item code(s), quantity, unit of measure, supplier, expected delivery date, requester, and justification, validating referenced `ITM-*`/`SUP*` codes with the existing search tool, and closing with a structured draft for confirmation; once the user confirms, the skill SHALL persist the requisition with the `create_requisition` tool.

#### Scenario: User asks to create a purchase requisition
- **WHEN** the user asks the assistant to create a new purchase requisition
- **THEN** the model activates `create-purchase-requisition` and guides the user through collecting the required fields one at a time

#### Scenario: Item and supplier codes are validated
- **WHEN** the user provides item or supplier codes during the guided flow
- **THEN** the model calls the exact-code search tool to validate them and flags any code with no match before finalizing the draft

#### Scenario: Guided flow produces a draft for confirmation
- **WHEN** all required fields are collected and validated
- **THEN** the model presents the draft requisition (item, quantity, unit, supplier, delivery date, requester, justification) as a structured summary and asks the user to confirm

#### Scenario: User confirms and the requisition is persisted
- **WHEN** the user confirms the drafted requisition and the model calls `create_requisition` with the collected fields
- **THEN** the tool writes the JSON file to the requisitions directory and the assistant reports the created file