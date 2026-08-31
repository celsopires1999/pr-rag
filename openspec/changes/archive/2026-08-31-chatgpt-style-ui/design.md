## Context

The current front-end (`web/`) is a single-page React app with three side-by-side panels (Chat, Ingestion, Status) and no routing, no component library, and a non-streaming, single-turn chat. The backend exposes synchronous endpoints:

- `POST /api/chat` → `ChatResponse { Answer, RetrievedCount }` (single-turn)
- `POST /api/ingest` → `IngestResult`
- `GET /api/status` → `SystemStatus`

The chat service (`ChatService.AnswerAsync`) does RAG retrieval and calls `IChatClient.GetResponseAsync` (Microsoft.Extensions.AI). The OpenAI `IChatClient` is registered via `DependencyInjection.AddChatClient` and supports streaming through `GetStreamingResponseAsync`.

We want a ChatGPT-like experience: a left sidebar for navigation + system options, a streaming and multi-turn chat, and a separate Status page, all built on Shadcn/ui.

## Goals / Non-Goals

**Goals:**
- Adopt Shadcn/ui (Radix + Tailwind) as the component foundation.
- Left sidebar: page navigation (Chat, Status) + RAG params (`top_k`, `min_similarity`) + ingest button.
- ChatGPT-style Chat page: message list + bottom input, streaming response, multi-turn session history.
- Separate System Status page.
- Keep existing `/api/chat`, `/api/ingest`, `/api/status` backward compatible.

**Non-Goals:**
- Persisting conversations across sessions/server (in-memory/session only).
- Streaming for the Status/Ingest pages.
- Changing RAG retrieval semantics beyond passing history as context.
- Authentication / multi-user.

## Decisions

### D1: Streaming via SSE over a new endpoint — `POST /api/chat/stream`
Add a new `POST /api/chat/stream` that returns `text/event-stream`. It streams the assistant answer token-by-token using `IChatClient.GetStreamingResponseAsync`. SSE is the simplest, dependency-free browser-friendly transport for one-way token streaming.

- **Alternative considered:** WebSocket (overkill for one-way streaming; more protocol/code), `fetch` ReadableStream with NDJSON (SSE is more standard/browser-native). Chosen: SSE.

### D2: Streaming pipeline = single `IChatService.StreamAsync` method
Refactor `ChatService` so the shared retrieval logic is reused. `StreamAsync`:
1. Runs the same RAG retrieval as `AnswerAsync` (codes + vector search).
2. Builds a prompt that includes the **conversation history** (previous user/assistant turns) plus the current user question and the retrieved context.
3. Calls `GetStreamingResponseAsync` and yields tokens through an `IAsyncEnumerable<string>` / channel.
4. Also writes the RAG observability report (reuse `WriteReportAsync`).

- **Choice:** Keep `AnswerAsync` for backward compat; add `StreamAsync`. The report is written once (after streaming), not per token.

### D3: Multi-turn contract — `ChatStreamRequest` with a `messages` array
New DTO `ChatStreamRequest { Question, TopK, MinSimilarity, Messages }` where `Messages` is the ordered history of `{ Role: "user"|"assistant", Content }` **excluding** the current question (the current question is sent as `Question`). This keeps a single, well-defined "current question" for RAG retrieval while giving the LLM full conversation context in the prompt.

- **Alternative considered:** sending all messages and deriving the last user turn as the question. Chosen the explicit `Question` + `Messages` split because the RAG retrieval is tied to the latest user question and this avoids ambiguity about which turn to retrieve on.

### D4: Front-end Shadcn/ui adoption — Tailwind + Radix via CLI
Initialize Tailwind CSS and add Shadcn/ui via `npx shadcn@latest init` and `npx shadcn@latest add`. This installs Radix primitives (Dialog, Button, Input, ScrollArea, etc.) and sets up `cn()` + theme tokens. We use a small set of components (button, input, card, scroll-area, sidebar, dialog, separators, label) to keep surface area minimal.

### D5: Client routing with a lightweight router
Use React Router (`react-router-dom`) for two routes: `/` (Chat) and `/status`. This matches the "pages" mental model (Chat and Status) requested.

- **Alternative considered:** hand-rolled state-based view switching. Chosen React Router for standard nested layout (sidebar persists across routes).

### D6: Layout — persistent Sidebar + routed content
A `Layout` component renders the Shadcn `Sidebar` (navigation links + bottom RAG params + ingest) and the routed `<Outlet/>`. RAG params are stored in a small shared context so the Chat page reads the current `top_k`/`min_similarity` from the sidebar.

### D7: SSE consumption on the front-end
Use `fetch` with `ReadableStream` (or `EventSource` if the backend emits named events) to read the SSE stream incrementally and append tokens to the current assistant message as they arrive. The current `api.ts` `chat()` sync path stays for backward compat; a new `chatStream()` reads the stream.

### D8: RAG params live in the sidebar (bottom) and are read by Chat
The sidebar's RAG controls (top_k, min_similarity) are the single source of truth via a `RagSettingsContext`. The Chat page sends them on each streaming request. This satisfies "system options in the sidebar".

## Risks / Trade-offs

- **Client disconnects mid-stream** → SSE is naturally resumable per request; on abort the backend stops streaming via the request `CancellationToken`. Front-end aborts the `AbortController` on navigation.
- **Observability report written after streaming** → No per-token writes; report is written once the stream completes (or errors). Acceptable; matches current behavior scope.
- **Long histories bloat the prompt/context** → Session history is limited client-side to a reasonable window (e.g. last ~10 messages); noted as a follow-up knob.
- **Shadcn/ui adds Tailwind + Radix dependencies** → Isolated to `web/`; does not affect the .NET build.
- **Backward compatibility** → `/api/chat` unchanged; new `/api/chat/stream` is additive. Rollback = stop using the stream endpoint; the old UI path remains functional.
- **SSE won't auto-reconnect partial messages** → Each user question starts a fresh request; no cross-request resumption required.

## Migration Plan

1. Backend: add `ChatStreamRequest` DTO + `IChatService.StreamAsync`; refactor shared retrieval; add `/api/chat/stream` SSE endpoint.
2. Front-end: init Tailwind + Shadcn/ui; add routing + Layout + Sidebar; add `chatStream()` API and RagSettingsContext.
3. Build Chat (ChatGPT-style) and Status pages; update types.
4. Wire sidebar nav + RAG params + ingest.
5. Verify: `cd web && npm run build`; `dotnet build PrRag.sln`; `dotnet test` (in SDK container); end-to-end via `docker compose --profile demo up --build web api`.

Rollback: revert the new endpoint/UI; existing `/api/chat` continues to work. No DB migration.

## Open Questions

- Message-history window limit (default chosen ~10; can be tuned).
- Whether the Status page should also expose the ingest button (kept in the sidebar only, per the requested "system options in sidebar").
