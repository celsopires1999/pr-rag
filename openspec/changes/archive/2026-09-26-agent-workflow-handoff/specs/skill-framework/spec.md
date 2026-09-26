## MODIFIED Requirements

### Requirement: Skills guide without granting new capabilities
A skill SHALL only add conversational instructions, and SHALL NOT add, remove, or change the tool functions that any agent holds, nor determine which agent holds which tool. The guidance the model receives about skills SHALL state that activating a matching skill improves the guided conversation, and SHALL NOT imply that activating a skill is required for any tool to become callable or for any workflow guardrail to hold.

The capability a skill may reference SHALL be scoped to the fixed union of tools across all agents (`search_by_codes`, `search_semantic`, `get_suppliers_by_item`, `activate_skill`, `create_requisition_draft`, `confirm_requisition_draft`, `create_requisition`), which SHALL NOT change when a skill is active. Which of those tools a given agent holds, and which agent handles a given turn, is the orchestrator's routing decision and is outside a skill's authority to influence.

#### Scenario: Tool set unchanged during a skill
- **WHEN** a skill is active in a conversation
- **THEN** the union of tools across all agents remains the fixed framework set (`search_by_codes`, `search_semantic`, `get_suppliers_by_item`, `activate_skill`, `create_requisition_draft`, `confirm_requisition_draft`, `create_requisition`), with no tools added, removed, or reassigned between agents by the skill

#### Scenario: Workflow guardrails do not depend on activation
- **WHEN** the model has not activated any skill
- **THEN** every tool guardrail still holds, and the guidance the model received did not present skill activation as a precondition for any tool call or any correctness rule

#### Scenario: A skill cannot grant a capability an agent does not hold
- **WHEN** a skill's text asserts that a particular tool is available
- **THEN** the assertion does not cause any agent to be given that tool, because tool assignment is decided by the orchestrator's routing and not by skill content

#### Scenario: A skill cannot steer routing
- **WHEN** a skill's text describes a procedure
- **THEN** the procedure guides how the responsible agent carries out its work, and does not determine which agent is handed the turn

## ADDED Requirements

### Requirement: Skill guidance remains effective under agent routing
The system SHALL keep active-skill guidance reachable for the turn it applies to regardless of which agent ends up handling that turn, so that extracting a capability into its own agent does not silently drop the skill body for the turn.

#### Scenario: Guidance applies on a routed turn
- **WHEN** a turn is handed off to a participant agent while a skill is active
- **THEN** the skill guidance for that turn is still applied, and the procedure is not lost because the answering agent changed

#### Scenario: The guardrail does not depend on which agent runs
- **WHEN** a tool is reachable only from a specific agent and no skill is active
- **THEN** that tool's guardrails still hold on any turn, and skill activation is not presented as the way to make it available
