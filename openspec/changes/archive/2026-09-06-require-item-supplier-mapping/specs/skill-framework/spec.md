## MODIFIED Requirements

### Requirement: Purchase requisition file creation tool
The system SHALL expose a `create_requisition` tool that persists a confirmed purchase requisition as a JSON file under the configured requisitions directory (default `./requisitions`, set via the `Requisitions__Directory` environment variable and resolving to `/app/requisitions` inside the API image), with the file containing exactly `SupplierCode`, `Item`, `Description`, `Quantity`, `Date`, and `Requester`. The tool SHALL refuse to write when required fields are missing or invalid, SHALL refuse to write when no existing purchase requisition has the same item code AND supplier code combination (verifying the supplier is registered for that item), and SHALL be described to the model as callable only after explicit user confirmation of a drafted requisition.

#### Scenario: Confirmed requisition written to disk
- **WHEN** the model calls `create_requisition` with all six fields present and valid and an existing requisition has the same item and supplier combination
- **THEN** the system writes a JSON file to the requisitions directory containing exactly those fields and returns the created file name to the model

#### Scenario: Required field missing or invalid
- **WHEN** the model calls `create_requisition` with a missing required field or an invalid value (e.g. non-numeric `Quantity`, unparseable `Date`)
- **THEN** the system does not write any file and returns an error describing the invalid or missing fields

#### Scenario: Unknown item-supplier combination refused
- **WHEN** the model calls `create_requisition` with valid fields but no existing requisition has the same item code and supplier code combination
- **THEN** the system does not write any file and returns an error stating the supplier is not registered for that item and no requisition was created

### Requirement: Purchase requisition creation skill
The system SHALL ship a `create-purchase-requisition` skill that guides the user step by step through drafting a new purchase requisition — collecting item code(s), quantity, unit of measure, supplier, expected delivery date, requester, and justification, validating referenced `ITM-*`/`SUP*` codes with the existing search tool and confirming the **combination** of the item code and supplier code exists in the database, and closing with a structured draft for confirmation; once the user confirms, the skill SHALL persist the requisition with the `create_requisition` tool.

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
- **WHEN** the user confirms the drafted requisition and the model calls `create_requisition` with the collected fields
- **THEN** the tool writes the JSON file to the requisitions directory and the assistant reports the created file