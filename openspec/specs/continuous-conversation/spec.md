# Continuous Conversation

## Purpose

Provide a continuous, multi-turn chat experience by sending the full conversation history to the chat model on every turn and preserving tool-resolved context across turns.
## Requirements
### Requirement: Full conversation context to the model
The system SHALL own the conversation per client-supplied `session_id` in an application-held session record, and SHALL replay that conversation into every turn's run so the answering agent sees all prior user/assistant messages and the current question. The MAF `AgentSession` SHALL be created per turn and SHALL NOT be used to carry state across turns. The active skill and any staged requisition draft SHALL be held in the same application-owned record rather than in `AgentSession.StateBag`.

#### Scenario: Prior turns included in the answer
- **WHEN** a client sends a current question with a `session_id` that has prior turns
- **THEN** the system replays the stored conversation into the turn's run and generates the answer from it

#### Scenario: New session behaves as single turn
- **WHEN** a client sends a first question with a `session_id` that has no prior turns
- **THEN** the turn runs with only the current question

#### Scenario: A reloaded skill manifest reaches a live conversation
- **WHEN** a skill is added or changed on disk during a live conversation
- **THEN** the next turn's agent instructions are composed from the reloaded manifest, because the graph is composed per turn and no cached session pins an earlier composition

#### Scenario: Active skill and staged draft survive the turn boundary
- **WHEN** a turn ends with a skill activated or a requisition draft staged
- **THEN** the next turn of the same `session_id` still sees both, because they are held in the application-owned session record

#### Scenario: The client's session id still identifies one conversation
- **WHEN** a client reuses a `session_id` across turns and one of those turns was routed to a specialist
- **THEN** the client still sees a single continuous conversation, and the per-turn agent and session machinery is not surfaced to it

### Requirement: Tool context persists across turns
The system SHALL use `AgentSession` to carry the results of tool calls into subsequent turns so follow-up questions can reference earlier retrieved requisitions without re-retrieving.

#### Scenario: Follow-up references earlier retrieval
- **WHEN** the agent retrieved requisitions in an earlier turn and the user asks a follow-up about them
- **THEN** the session preserves the prior tool results so the agent can answer the follow-up consistently

### Requirement: Conversation grounding without separate rewriter
The system SHALL use the full `AgentSession` conversation context for retrieval grounding on the current question, without requiring the client to repeat earlier questions.

#### Scenario: Current question grounds retrieval
- **WHEN** a client asks a follow-up question that depends on prior context
- **THEN** the agent resolves context from the ongoing session conversation and the current question's retrieval need

### Requirement: Retrieved context persists across turns as answer text
The system SHALL record each turn's question and the answer text it returned into the application-owned session record, so a follow-up can be answered from the grounded rows that answer already quoted, and SHALL preserve that continuity across a handoff so that a turn routed to a different agent than the previous turn does not lose the earlier answer.

The system SHALL NOT record tool calls or tool results into that record. Neither half of a call/result pair is replayable: across a handoff the specialist made the call, so the workflow surfaces the result without it, and the framework batches results into one message whose last call is emitted as a later assistant message, so no prefix of a tool transcript is well-formed. A chat API rejects such a replay outright and fails the whole turn, so persistence of tool traffic is not a weaker guarantee but an unavailable one.

#### Scenario: Follow-up references earlier retrieval
- **WHEN** the agent retrieved requisitions in an earlier turn and the user asks a follow-up about them
- **THEN** the earlier answer, carrying the rows it quoted, is replayed into the follow-up turn so the agent can answer consistently

#### Scenario: A follow-up handled by a different agent still sees the results
- **WHEN** a turn is routed to a participant that did not run the earlier retrieval
- **THEN** the earlier answer remains available to it, so a follow-up question about earlier results does not force a silent re-retrieval or an unanswerable reply

#### Scenario: A replayed turn cannot be rejected as malformed
- **WHEN** a turn's tool calls and results are recorded alongside its text
- **THEN** no follow-up fails with a rejected request, because no recorded message carries a tool call or a tool result whose counterpart is missing

#### Scenario: Agents do not keep a second copy of the history
- **WHEN** a turn runs
- **THEN** each agent's chat history provider contributes no messages, so the application-owned record is the only history and a turn cannot see the conversation twice

