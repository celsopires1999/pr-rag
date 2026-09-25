## MODIFIED Requirements

### Requirement: Skill activation via tool call
The system SHALL expose an `activate_skill` tool that, when called with a skill name by the chat model, returns a structured activation result carrying an explicit found flag and, on success, that skill's guidance body for the model to follow; a call for an unknown skill SHALL return the same result shape with the found flag false and a message listing the available skills.

#### Scenario: Model activates a known skill
- **WHEN** the chat model calls `activate_skill` with the name of a loaded skill
- **THEN** the tool returns a result with the found flag true and the skill's guidance body, and the model follows it to guide the conversation

#### Scenario: Model activates an unknown skill
- **WHEN** the chat model calls `activate_skill` with a name not present in the manifest
- **THEN** the tool returns a result with the found flag false and a message stating the skill is unknown and listing the available skill names

### Requirement: Skills guide without granting new capabilities
A skill SHALL only add conversational instructions, and SHALL NOT add, remove, or change the tool functions available to the chat model.

#### Scenario: Tool set unchanged during a skill
- **WHEN** a skill is active in a conversation
- **THEN** the tools available to the model remain the fixed framework set (`search_by_codes`, `search_semantic`, `activate_skill`, `create_requisition`, `get_suppliers_by_item`), with no tools added, removed, or changed by the skill

### Requirement: Purchase requisition file creation tool
The system SHALL expose a `create_requisition` tool that persists a confirmed purchase requisition into a dedicated Postgres schema, with the persisted record containing exactly `SupplierCode`, `Item`, `Description`, `Quantity`, `Date`, and `Requester`. The tool SHALL return a structured result carrying an explicit success flag, the created requisition id on success, and a human-readable error message when the write is refused. The tool SHALL refuse to persist when required fields are missing or invalid, SHALL refuse to persist when no existing purchase requisition has the same item code AND supplier code combination (verifying the supplier is registered for that item), and SHALL be described to the model as callable only after explicit user confirmation of a drafted requisition.

#### Scenario: Confirmed requisition persisted to the database
- **WHEN** the model calls `create_requisition` with all six fields present and valid and an existing requisition has the same item and supplier combination
- **THEN** the system persists a row in the dedicated Postgres schema containing exactly those fields and returns a result with the success flag true and the created requisition id

#### Scenario: Required field missing or invalid
- **WHEN** the model calls `create_requisition` with a missing required field or an invalid value (e.g. non-numeric `Quantity`, unparseable `Date`)
- **THEN** the system persists nothing and returns a result with the success flag false and a message describing the invalid or missing fields

#### Scenario: Unknown item-supplier combination refused
- **WHEN** the model calls `create_requisition` with valid fields but no existing requisition has the same item code and supplier code combination
- **THEN** the system persists nothing and returns a result with the success flag false and a message stating the supplier is not registered for that item and no requisition was created
