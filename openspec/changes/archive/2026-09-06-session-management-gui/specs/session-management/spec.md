## ADDED Requirements

### Requirement: Show active session in chat GUI
The system SHALL display the currently active session id in the chat interface so the user can see which conversation context is active.

#### Scenario: Session id visible
- **WHEN** the chat page loads
- **THEN** the active session id (short form, first 8 characters) is visible in the GUI header with the full id available as a tooltip

#### Scenario: Session id shown after reload
- **WHEN** the page is reloaded and a session id was persisted in `localStorage`
- **THEN** the same session id continues to be shown as active

### Requirement: Discard current session and start fresh
The system SHALL let the user discard the current session and start a new one, resetting the conversation context so the next turn behaves as a single-turn session.

#### Scenario: New session clears context
- **WHEN** the user clicks "New session" while no stream is in flight
- **THEN** the server discards the current session, the client generates and persists a new session id, the on-screen message history is cleared, and the next question starts a brand-new session

#### Scenario: New session root on a subsequent turn
- **WHEN** the user asks a question on the newly created session that references earlier turns
- **THEN** the system answers without context from the discarded session (prior turns are not carried over)

### Requirement: Discard endpoint on API
The system SHALL expose a server-side endpoint to discard a session by id so the stored conversation context for that id is removed.

#### Scenario: Discard known session
- **WHEN** a `DELETE /api/sessions/{id}` request is made for a session that exists in the in-memory store
- **THEN** the store removes that session and the endpoint returns success (`204 No Content`)

#### Scenario: Discard unknown session
- **WHEN** a `DELETE /api/sessions/{id}` request is made for an id with no stored session
- **THEN** the endpoint returns `404 Not Found`, and the client continues to treat the reset flow as successful