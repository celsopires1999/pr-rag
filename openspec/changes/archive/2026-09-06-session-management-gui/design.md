## Context

The app spans a React frontend (`frontend/`) and a .NET minimal API (`backend/`). Conversation context is already carried across turns via a client-supplied `session_id` (`SESSION_ID_KEY = 'prrag.session_id'` in `ChatPage.tsx`, persisted in `localStorage`) and a server-side `AgentSession` held in the process-local `InMemoryAgentSessionStore`. Today there is no visibility into the session and no way to reset it — the id is generated once and reused forever (until the browser storage is cleared or the server restarts).

Stakeholders are end users of the chat GUI who want to deliberately start a clean conversation.

## Goals / Non-Goals

**Goals:**
- Show the currently active session id in the chat GUI.
- Provide a "New session" action that discards the current session (server-side) and generates a fresh one, clearing the on-screen message history so the next turn starts with an empty context.
- Keep the change small and local: no schema/model changes, no new dependencies.

**Non-Goals:**
- Session listing, renaming, authentication, or multi-user isolation.
- Persisting sessions across server restarts (the store is deliberately in-memory per process).
- Full conversation history persistence/history recall feature.

## Decisions

### 1. Discard via server endpoint `DELETE /api/sessions/{id}` backed by a new `IAgentSessionStore.RemoveAsync`
The session data lives in-process on the API. To truly reset context, the server must drop the stored `AgentSession`; simply clearing the browser UI is insufficient because the id would still resolve to the old conversation on the next turn.
- **Chosen:** Add `Task<bool> RemoveAsync(string sessionId, CancellationToken ct = default)` to `IAgentSessionStore` and implement it in `InMemoryAgentSessionStore` with `_sessions.TryRemove`. Expose `DELETE /api/sessions/{id}` in `Program.cs` that resolves the store from DI and returns `204 No Content` (or `404` if the session was unknown).
- **Alternative considered:** A `POST /api/sessions/reset` that also hands back a new id. Rejected — the frontend already generates ids and owns the persistence, so the API only needs to evict; keeping the endpoint idempotent and stateless wrt new id generation is simpler.

### 2. Frontend keeps owning session id generation and persistence
The frontend already generates and stores the id (`crypto.randomUUID()` + `localStorage`). Reuse this path for the "New session" flow.
- **Chosen:** On "New session", call the discard endpoint for the old id (best-effort), generate a new id, persist it in `localStorage`, clear `messages`/`error`, and update `sessionIdRef`. The header shows the short form of the current id.
- **Alternative considered:** Server-issued session ids. Rejected — no benefit for a single-user local app and it would churn the existing streaming/type contracts.

### 3. Show a short form of the session id in the GUI header
A full UUID is noisy.
- **Chosen:** Render the first 8 characters (e.g. `83a1b2c4`), with the full value available as a `title` tooltip. A "New session" button (rotating icon) sits beside it.
- **Alternative considered:** Full id in text. Rejected for visual noise; truncated + tooltip keeps it compact.

## Risks / Trade-offs

- **[Session not found on discard]** → Endpoint returns `404`; the frontend treats it as success anyway (best-effort reset) since the outcome is the same.
- **[Race: a streaming turn in flight during discard]** → The action is only enabled when not streaming; the frontend also aborts any in-flight controller before switching ids.
- **[In-memory store loses sessions on restart]** → Accepted, unchanged behavior; the GUI label/session still reflects the fresh id the client now holds.
- **[Refresh data loss]** → The current design persists only the session id in `localStorage`, not the messages. On reload the id persists but the on-screen history is empty — matches today's behavior, not a regression introduced here.

## Migration Plan

- Backend: no database changes; deploy code (add method + endpoint) behind the same process restart.
- Frontend: shipped together with the backend change; no storage migration needed (old session ids continue to work and are simply discarded on the first "New session").
- Rollback: revert the endpoint + method and the header UI; old ids remain valid, so behavior degrades gracefully.