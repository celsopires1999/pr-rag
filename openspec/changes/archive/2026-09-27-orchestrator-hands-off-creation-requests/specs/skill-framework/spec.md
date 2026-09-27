## MODIFIED Requirements

### Requirement: Skills guide without granting new capabilities
A skill SHALL only add conversational instructions, and SHALL NOT add, remove, or change the tool functions that any agent holds, nor determine which agent holds which tool. The guidance the model receives about skills SHALL state that activating a matching skill improves the guided conversation, and SHALL NOT imply that activating a skill is required for any tool to become callable or for any workflow guardrail to hold. A skill's steps SHALL be performed by the agent that owns the capability they act on, and activation alone SHALL NOT make the activating agent the performer, because guidance is delivered to every agent in the run and an agent holding none of the tools a step needs can satisfy that step in prose without anything failing.

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

#### Scenario: Activating a skill does not make the activating agent its performer
- **WHEN** an agent that activated a skill is asked to carry out a step whose result can only be obtained from a tool it does not hold
- **THEN** the step is handed to the agent that holds that tool, because activation conveys guidance and not capability, and a step performed without its tool produces a description of work rather than the work

#### Scenario: A step requiring an absent tool is not performed in prose
- **WHEN** activated guidance instructs an agent to present an artifact, such as a draft summary or a confirmation request, that its own tools cannot produce
- **THEN** the agent does not compose that artifact from the user's own input, because narrating an artifact no tool returned asserts a state the system is not in and no guardrail downstream can contradict

#### Scenario: Activated guidance does not displace the receiving agent's own instructions
- **WHEN** a run carries activated skill guidance
- **THEN** each agent in that run still holds its own instructions, so guidance arriving later in the run does not remove the rule governing what that agent may perform
