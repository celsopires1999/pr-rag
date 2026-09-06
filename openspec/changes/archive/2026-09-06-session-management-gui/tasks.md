## 1. Backend: session discard in Application layer

- [x] 1.1 Add `Task<bool> RemoveAsync(string sessionId, CancellationToken cancellationToken = default)` to `IAgentSessionStore`
- [x] 1.2 Implement `RemoveAsync` in `InMemoryAgentSessionStore` using `_sessions.TryRemove` and return whether the session existed
- [x] 1.3 Add a unit/integration test verifying `RemoveAsync` drops a created session and returns `false` for unknown ids

## 2. Backend: discard endpoint in API

- [x] 2.1 Add `DELETE /api/sessions/{id}` in `PrRag.Api/Program.cs` that resolves `IAgentSessionStore`, calls `RemoveAsync`, and returns `204 No Content` for a removed session or `404 Not Found` when it did not exist
- [x] 2.2 Add an API-level integration test for `DELETE /api/sessions/{id}` covering the removed and not-found cases

## 3. Frontend: session display and reset

- [x] 3.1 Add a `discardSession(sessionId)` function in `frontend/src/api.ts` that calls `DELETE /api/sessions/{id}` and tolerates `404`
- [x] 3.2 In `ChatPage.tsx`, expose the active session id (short form: first 8 characters) in the GUI header with the full id as a `title` tooltip
- [x] 3.3 Add a "New session" button in the header that is disabled while `streaming`, aborts any in-flight request, calls `discardSession` (best-effort), generates a new id via `getOrCreateSessionId`-style logic persisted to `localStorage`, and clears `messages`/`error`

## 4. Verification

- [x] 4.1 Run `dotnet build backend/PrRag.sln` and `dotnet test backend/tests/PrRag.Tests` (with `TEST_CONNECTION_STRING`)
- [x] 4.2 Run `cd frontend && npm install && npm run build`
- [x] 4.3 Manually verify in the browser: session id shows in header, sending a question creates context, "New session" resets the id and clears history, and a follow-up question on the new session does not reference the prior conversation