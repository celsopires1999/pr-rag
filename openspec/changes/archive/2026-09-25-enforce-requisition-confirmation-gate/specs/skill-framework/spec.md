## MODIFIED Requirements

### Requirement: Skills guide without granting new capabilities
A skill SHALL only add conversational instructions, and SHALL NOT add, remove, or change the tool functions available to the chat model. The guidance the model receives about skills SHALL state that activating a matching skill improves the guided conversation, and SHALL NOT imply that activating a skill is required for any tool to become callable or for any workflow guardrail to hold.

#### Scenario: Tool set unchanged during a skill
- **WHEN** a skill is active in a conversation
- **THEN** the tools available to the model remain the fixed framework set (`search_by_codes`, `search_semantic`, `get_suppliers_by_item`, `activate_skill`, `create_requisition_draft`, `confirm_requisition_draft`, `create_requisition`), with no tools added, removed, or changed by the skill

#### Scenario: Workflow guardrails do not depend on activation
- **WHEN** the model has not activated any skill
- **THEN** every tool guardrail still holds, and the guidance the model received did not present skill activation as a precondition for any tool call or any correctness rule

### Requirement: Skill manifest exposed to the chat model
The system SHALL expose the loaded skill catalog to the chat model in the system prompt as a manifest of skill names paired with their descriptions, and each description SHALL state positively when the skill applies.

#### Scenario: Manifest lists each loaded skill with its description
- **WHEN** a session is created and at least one skill is loaded
- **THEN** the system prompt contains a manifest section listing every loaded skill as its name followed by a non-empty description stating when to use that skill

#### Scenario: Empty manifest degrades gracefully
- **WHEN** a session is created and no skill is loaded
- **THEN** the system prompt states that no skills are available and does not present skill activation as a possible step

#### Scenario: Description is framed positively
- **WHEN** a skill's description is surfaced in the manifest
- **THEN** the description states what the skill is for and when to use it, rather than leading with a restrictive exception that narrows its intended use

### Requirement: Skill activation via tool call
The system SHALL activate a skill when the model calls `activate_skill` with the skill's name, and the model SHALL be instructed to call `activate_skill` whenever the user's stated intent matches a skill in the manifest — including the common, plainly-worded phrasings of that intent, not only explicit or unusual ones.

#### Scenario: Activation on a natural intent phrasing
- **WHEN** the user states the intent a skill covers using an ordinary conversational phrasing, without naming the skill
- **THEN** the model calls `activate_skill` for the matching skill and follows its instructions

#### Scenario: Activation when the user names the skill
- **WHEN** the user explicitly asks for a skill by name
- **THEN** the model calls `activate_skill` for that skill and follows its instructions

#### Scenario: Unknown skill name reported
- **WHEN** the model calls `activate_skill` with a name that matches no loaded skill
- **THEN** the system activates nothing and returns an error listing the available skill names

## ADDED Requirements

### Requirement: Purchase requisition file creation tool
The system SHALL expose a `create_requisition` tool that persists a confirmed purchase requisition into a dedicated Postgres schema, with the persisted record containing exactly `SupplierCode`, `Item`, `Description`, `Quantity`, `Date`, and `Requester`. The tool SHALL refuse to persist when required fields are missing or invalid, SHALL refuse to persist when no existing purchase requisition has the same item code AND supplier code combination (verifying the supplier is registered for that item), and SHALL refuse to persist unless the session holds a draft the user explicitly confirmed. The confirmation requirement SHALL be enforced in code by the tool rather than stated only in the tool's description to the model.

#### Scenario: Confirmed requisition persisted to the database
- **WHEN** the model calls `create_requisition` with all six fields present and valid, a confirmed draft exists, and an existing requisition has the same item and supplier combination
- **THEN** the system persists a row in the dedicated Postgres schema containing exactly those fields and returns the created requisition id to the model

#### Scenario: Required field missing or invalid
- **WHEN** the model calls `create_requisition` with a missing required field or an invalid value (e.g. non-numeric `Quantity`, unparseable `Date`)
- **THEN** the system does not persist anything and returns an error describing the invalid or missing fields

#### Scenario: Unknown item-supplier combination refused
- **WHEN** the model calls `create_requisition` with valid fields but no existing requisition has the same item code and supplier code combination
- **THEN** the system does not persist anything and returns an error stating the supplier is not registered for that item and no requisition was created

#### Scenario: Unconfirmed call refused regardless of the tool description
- **WHEN** the model calls `create_requisition` with six valid fields and no confirmed draft, having skipped the draft and confirmation tools
- **THEN** the system persists nothing and returns a refusal naming the required prior steps, because the gate is enforced in code and not dependent on the model having read the tool description

### Requirement: Purchase requisition creation skill
The system SHALL ship a `create-purchase-requisition` skill that guides the user step by step through drafting a new purchase requisition — collecting item code(s), quantity, unit of measure, supplier, expected delivery date, requester, and justification, validating referenced `ITM-*`/`SUP*` codes with the existing search tool and confirming the **combination** of the item code and supplier code exists in the database, and closing with a structured draft for confirmation; once the user confirms, the skill SHALL stage the draft with `create_requisition_draft`, record the confirmation with `confirm_requisition_draft`, and persist with the `create_requisition` tool. The same steps SHALL also be available to the model without the skill, so the skill improves the conversation but is not required for the workflow to complete correctly.

#### Scenario: User asks to create a purchase requisition
- **WHEN** the user asks the assistant to create a new purchase requisition
- **THEN** the model activates `create-purchase-requisition` and guides the user through collecting the required fields one at a time

#### Scenario: Item and supplier codes are validated
- **WHEN** the user provides item or supplier codes during the guided flow
- **THEN** the model calls the exact-code search tool to validate them and flags any code with no match before finalizing the draft

#### Scenario: Item-supplier combination is validated
- **WHEN** both the item code and supplier code are collected
- **THEN** the model confirms (via the exact-code search tool filtering by both codes simultaneously) that at least one requisition exists with that exact item code + supplier code combination, and warns the user if the supplier has never purchased that item before finalizing the draft

#### Scenario: Guided flow produces a draft for confirmation
- **WHEN** all required fields are collected and validated
- **THEN** the model presents the draft requisition (item, quantity, unit, supplier, delivery date, requester, justification) as a structured summary and asks the user to confirm

#### Scenario: User confirms and the requisition is persisted
- **WHEN** the user confirms the drafted requisition, the model records the confirmation, and the model calls `create_requisition` with the collected fields
- **THEN** the tool persists the requisition to the dedicated Postgres schema and the assistant reports the created requisition id

#### Scenario: Workflow completes without the skill
- **WHEN** the user initiates the same requisition creation but the model never activates `create-purchase-requisition`
- **THEN** the model still drafts, asks for confirmation, and persists, because the steps are present in the base prompt and the tool descriptions and the confirmation gate is enforced by the tool

### Requirement: Skill guidance is not the sole carrier of a workflow procedure
A workflow procedure that a skill describes SHALL ALSO be stated in the base system prompt or in the relevant tool description, so that a failure to activate the skill degrades the conversation's helpfulness without silently removing a documented step. No correctness guardrail SHALL depend on a skill being activated.

#### Scenario: Procedure survives a skill routing miss
- **WHEN** a user initiates a workflow covered by a skill and the model does not activate that skill
- **THEN** the model still follows the workflow's documented steps because they are present in the base prompt or the tool descriptions

#### Scenario: Guardrail holds without the skill
- **WHEN** any tool guardrail would be violated and no skill is active
- **THEN** the guardrail is still enforced by the tool itself, independently of skill activation
