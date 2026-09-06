## Why

The app already uses a `session_id` to keep conversation context across turns, but the value is hidden inside `localStorage` and there is no way for the user to see or reset it. This makes it impossible for the user to deliberately start a fresh conversation context or to notice that context is being carried over.

## What Changes

- Surface the current session id in the chat GUI so the user can see which session context is active.
- Add a "New session" control that discards the current session and starts a new one, clearing the local message history and the server-side context so the next turn begins from scratch.
- Add an in-memory server endpoint to discard a session by id (existing `InMemoryAgentSessionStore` already holds sessions; a way to remove one is required).

## Capabilities

### New Capabilities
- `session-management`: Ability to display the active session id and to discard it, starting a fresh conversation context.

### Modified Capabilities
<!-- No existing capability requirement changes; this is a new capability. -->

## Impact

- Frontend `src/pages/ChatPage.tsx` — render the session id and a "New session" action; clear `messages` and issue a new session id.
- Frontend `src/types.ts` — add response shape for session info if needed.
- Backend `PrRag.Application` — add an endpoint/handler to discard a session in `InMemoryAgentSessionStore` (new method on `IAgentSessionStore`, e.g. `RemoveAsync`).
- Backend `PrRag.Api` — expose an endpoint to discard a session (e.g. `DELETE /api/sessions/{id}`).
- No model or migration changes. `InMemoryAgentSessionStore` is per-process; discarding is best-effort and sessions vanish on restart regardless.