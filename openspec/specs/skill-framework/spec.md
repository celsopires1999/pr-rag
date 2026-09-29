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
The system SHALL expose the loaded skill catalog to the chat model in the system prompt as a manifest of skill names paired with their descriptions, and each description SHALL state positively when the skill applies.

#### Scenario: Manifest lists each loaded skill with its description
- **WHEN** a session is created and at least one skill is loaded
- **THEN** the system prompt contains a manifest section listing every loaded skill as its name followed by a non-empty description stating when to use that skill

#### Scenario: Manifest reflects newly loaded skills
- **WHEN** skills are reloaded and the set of loaded skills changes
- **THEN** the system prompt for subsequent requests reflects the updated manifest

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

### Requirement: Skill guidance persists across turns
The system SHALL keep an activated skill's guidance in effect for subsequent turns of the same conversation by storing the active skill id/body in the session state bag and re-injecting its instructions when conversation history indicates a skill is active. All skill-state read, injection, and clear logic SHALL be encapsulated in a dedicated state helper (over the `AgentSession` state bag), not inlined with magic keys in `ChatService`.

#### Scenario: Guidance restored on a following turn
- **WHEN** a client sends a follow-up request in the same session and a skill was active in a previous turn
- **THEN** the state helper detects the active skill from the session state bag and the system injects that skill's guidance into the conversation before the latest user turn, so the workflow continues

#### Scenario: Guidance absent without prior activation
- **WHEN** a conversation never activated a skill
- **THEN** no skill guidance is injected and the assistant behaves as normal Q&A

#### Scenario: Guidance cleared when the workflow completes
- **WHEN** the skill's workflow completes (for example a requisition is created)
- **THEN** the state helper clears the active skill from the session state bag so subsequent turns behave as normal Q&A

#### Scenario: State keys encapsulated
- **WHEN** skill state is read, injected, or cleared
- **THEN** the session state-bag keys and logic live in the dedicated helper, and `ChatService` does not inline the keys

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

### Requirement: A skill step whose result needs a tool names that tool and follows it
A skill step SHALL name the tool that produces the artifact it describes, and SHALL NOT instruct the reader to present, summarise, or request confirmation of an artifact before the step that calls the tool producing it. A step whose artifact is a staged draft SHALL take the values it presents from that tool's result rather than from a field template or a serialized example carried in the skill text, and a skill SHALL NOT carry a hand-maintained copy of a tool's parameter schema, because such a copy is a second source that can drift from the tool without anything failing.

This is the checkable form of the rule that a step whose result needs a tool is not performable by an agent that does not hold it: the ordering, not the wording, is what makes such a step renderable from the user's own input. The tools a skill names SHALL continue to belong to a single capability, and a skill SHALL NOT gain routing language in exchange.

#### Scenario: No artifact is presented before the call that produces it
- **WHEN** a skill instructs the reader to present a draft or ask the user to confirm one
- **THEN** that instruction appears only after the step that calls the tool staging the draft, so an agent without the tool has no earlier step it can satisfy in prose

#### Scenario: The presented values come from the staging tool's result
- **WHEN** a skill instructs the reader to present the draft back to the user
- **THEN** the values presented are the ones the staging tool returned, and the skill carries no field template and no serialized draft example of its own

#### Scenario: A stale schema copy cannot drift unnoticed
- **WHEN** a skill no longer carries its own copy of a tool's parameter names
- **THEN** a future change to the tool's parameters cannot leave the skill describing a different shape, because the tool's result is the only description of the artifact in the skill

#### Scenario: Removing a step does not leave a broken cross-reference
- **WHEN** a step is removed or reordered in a shipped skill
- **THEN** every reference to a step number in that skill still names the step it meant, so the procedure the model follows is the one the file describes

### Requirement: Skill activation states which capability owns the steps it returns
The result `activate_skill` returns SHALL state that the guidance it carries describes steps for the capability that owns the tools those steps name, and that activating a skill conveys guidance rather than capability. This statement SHALL be produced by the activation tool itself alongside the guidance, so that it is derived from the tool set the caller holds rather than from prose that can drift from it, and it SHALL NOT name a routing destination, because the graph decides who reaches whom and a skill that talks about routing is policed as such.

#### Scenario: The activation result carries the ownership statement
- **WHEN** a skill is activated
- **THEN** the returned result states that the steps belong to the capability owning the tools they name and that activation grants none of those tools, so the reader is told who performs the steps at the moment the guidance is handed over

#### Scenario: The statement is emitted by the tool, not by the file
- **WHEN** a capability's tools change
- **THEN** the ownership statement is derived from the capability's own registration rather than from skill text, so it cannot name a step the tool set no longer supports

#### Scenario: Activation still does not steer routing
- **WHEN** the activation result states which capability owns the steps
- **THEN** it names no routing destination and no specialist, so the handover of the turn remains the graph's decision

#### Scenario: Two channels carry the rule independently
- **WHEN** the reading agent's own instructions also state the capability rule
- **THEN** both statements remain, because the independence of the two is what makes the rule survive an agent that outweighs one of them

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

### Requirement: Skill guidance remains effective under agent routing
The system SHALL keep active-skill guidance reachable for the turn it applies to regardless of which agent ends up handling that turn, so that extracting a capability into its own agent does not silently drop the skill body for the turn.

#### Scenario: Guidance applies on a routed turn
- **WHEN** a turn is handed off to a participant agent while a skill is active
- **THEN** the skill guidance for that turn is still applied, and the procedure is not lost because the answering agent changed

#### Scenario: The guardrail does not depend on which agent runs
- **WHEN** a tool is reachable only from a specific agent and no skill is active
- **THEN** that tool's guardrails still hold on any turn, and skill activation is not presented as the way to make it available

