## MODIFIED Requirements

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