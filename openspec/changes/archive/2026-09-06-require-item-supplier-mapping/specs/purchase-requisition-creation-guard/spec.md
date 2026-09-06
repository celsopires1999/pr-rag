## ADDED Requirements

### Requirement: Requisition creation requires existing item-supplier combination
The system SHALL refuse to create a purchase requisition unless at least one existing purchase requisition in the database has the exact same item code (`Item`) AND supplier code (`SupplierCode`) combination as the one being created; when the combination does not exist, `create_requisition` SHALL not write any file and SHALL return an error explaining that the supplier is not registered for that item.

#### Scenario: Known item-supplier combination allows creation
- **WHEN** the model calls `create_requisition` and the database contains at least one requisition with the same item code and supplier code
- **THEN** the system persists the requisition as a JSON file and returns the created file name

#### Scenario: Unknown item-supplier combination blocks creation
- **WHEN** the model calls `create_requisition` and no existing requisition has the same item code and supplier code combination
- **THEN** the system does not write any file and returns an error stating the supplier is not registered for that item, so no requisition was created

#### Scenario: Combination check independent of individual code existence
- **WHEN** the item code and the supplier code each exist separately in the database but never appear together in any requisition
- **THEN** the system treats the combination as unknown and refuses to create the requisition