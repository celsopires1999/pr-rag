# Synthetic Data

## Purpose

Provide a generator that produces a realistic JSON dataset of purchase requisitions compatible with the ingestion schema, with recurring suppliers and items over recent months.

## Requirements

### Requirement: Synthetic purchase requisition dataset
The system SHALL provide a generator producing a realistic JSON dataset of purchase requisitions compatible with the ingestion schema.

#### Scenario: Generates thousands of rows over recent months
- **WHEN** the generator runs
- **THEN** it produces a JSON file with roughly thousands of records distributed across the last 18 months

#### Scenario: Compatible schema
- **WHEN** the generator produces the dataset
- **THEN** each record contains `purchase_requisition`, `supplier_code`, `supplier_name`, `item`, `item_name`, and `description` matching the ingestion format

#### Scenario: Recurring suppliers and items
- **WHEN** the generator runs
- **THEN** suppliers and items repeat across records so semantic queries about specific suppliers/items are meaningful

#### Scenario: Writes file to data path
- **WHEN** the generator runs
- **THEN** it writes the dataset to the bind-mounted data path so the ingestion pipeline picks it up
