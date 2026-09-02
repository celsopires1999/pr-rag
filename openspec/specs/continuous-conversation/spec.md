# Continuous Conversation

## Purpose

Provide a continuous, multi-turn chat experience by sending the full conversation history to the chat model on every turn and preserving tool-resolved context across turns.

## Requirements

### Requirement: Full conversation context to the model
The system SHALL send the complete conversation history — including all prior user and assistant messages and the current question — to the chat model on every turn.

#### Scenario: Prior turns included in the answer
- **WHEN** a client sends a current question together with prior user/assistant messages
- **THEN** the system generates the answer using the full history as conversation context

#### Scenario: Empty history behaves as single turn
- **WHEN** a client sends a first question with no prior messages
- **THEN** the system treats the current question as the only conversation message

### Requirement: Tool context persists across turns
The system SHALL let the chat model carry the results of retrieval tool calls into subsequent turns so follow-up questions can reference earlier retrieved requisitions without re-retrieving.

#### Scenario: Follow-up references earlier retrieval
- **WHEN** the model retrieved requisitions in an earlier turn and the user asks a follow-up about them
- **THEN** the system passes the prior tool results as part of the continuing conversation so the model can answer the follow-up consistently

### Requirement: Conversation grounding without separate rewriter
The system SHALL use the full conversation context for retrieval grounding on the current question, without requiring the client to repeat earlier questions.

#### Scenario: Current question grounds retrieval
- **WHEN** a client asks a follow-up question that depends on prior context
- **THEN** the system resolves context from the ongoing conversation and the current question's retrieval need