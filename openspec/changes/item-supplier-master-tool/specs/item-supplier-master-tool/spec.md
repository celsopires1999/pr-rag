## ADDED Requirements

### Requirement: Item-supplier master data tool
The system SHALL expose a `search_item_supplier_master` agent tool that returns distinct Item + Supplier master-data pairs from the `purchase_requisitions` table, independent of requisition search semantics.

#### Scenario: Query by item code
- **WHEN** the tool is invoked with `item = "ITM-000000001"` and no supplier parameter
- **THEN** the system returns all distinct `(SupplierCode, SupplierName, Item, ItemName)` tuples where `Item = "ITM-000000001"`, with no row limit

#### Scenario: Query by supplier code
- **WHEN** the tool is invoked with `supplier = "SUP00001"` and no item parameter
- **THEN** the system returns all distinct `(SupplierCode, SupplierName, Item, ItemName)` tuples where `SupplierCode = "SUP00001"`, with no row limit

#### Scenario: Query by both item and supplier
- **WHEN** the tool is invoked with both `item = "ITM-000000001"` and `supplier = "SUP00001"`
- **THEN** the system returns all distinct `(SupplierCode, SupplierName, Item, ItemName)` tuples matching both filters

#### Scenario: No parameters provided
- **WHEN** the tool is invoked with neither `item` nor `supplier`
- **THEN** the system returns an error message indicating at least one parameter is required

#### Scenario: No matching rows
- **WHEN** the tool is invoked with a valid parameter value that matches no rows
- **THEN** the system returns an empty result set

### Requirement: Tool result shape
The tool SHALL return a list of `ItemSupplierMasterResult` records containing exactly four fields: `SupplierCode`, `SupplierName`, `Item`, `ItemName`. The result SHALL NOT include `PurchaseRequisitionId`, `Description`, `Similarity`, or any other requisition-specific fields.

#### Scenario: Result contains only master fields
- **WHEN** the tool returns results for a supplier query
- **THEN** each result record contains only `SupplierCode`, `SupplierName`, `Item`, and `ItemName` — no additional fields are present

### Requirement: No row limit
The tool SHALL NOT apply any `TopK` or `LIMIT` clause. All matching distinct `(SupplierCode, Item)` pairs SHALL be returned regardless of count.

#### Scenario: Full result set returned
- **WHEN** the tool is invoked with a supplier code that has 50 distinct items
- **THEN** all 50 distinct `(SupplierCode, SupplierName, Item, ItemName)` tuples are returned

### Requirement: Distinct results
The tool SHALL return each `(SupplierCode, Item)` pair at most once, even if the underlying table contains multiple requisition rows with the same combination.

#### Scenario: Deduplication across requisitions
- **WHEN** three requisition rows exist for supplier `SUP00001` and item `ITM-000000001`
- **THEN** the tool returns exactly one `(SupplierCode, SupplierName, Item, ItemName)` tuple for that combination
