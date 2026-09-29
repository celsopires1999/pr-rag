## ADDED Requirements

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
