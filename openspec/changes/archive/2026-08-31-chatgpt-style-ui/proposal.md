## Why

The current React front-end is functional but does not match the experience users already know: there is no left-side navigation, the system options are scattered across separate panels, and the chat is neither streaming nor multi-turn. To make the tool feel familiar and pleasant, we will adopt the ChatGPT interaction model and organize the system options into a left sidebar.

## What Changes

- Replace the ad-hoc UI with a **Shadcn/ui** component library (Radix + Tailwind) for consistent, accessible primitives.
- Add a **left Sidebar** holding:
  - **Navigation** between pages: Chat and System Status.
  - Bottom section with **RAG parameters** (`top_k`, `min_similarity`) and an **ingest** button (system options).
- Rework the **Chat page** to a ChatGPT-style layout: message list + a prominent input at the bottom.
- Add **real streaming**: a new backend SSE endpoint that returns the assistant answer token-by-token.
- Add **multi-turn** conversation within the browser session: conversation history is sent to the backend as context and included in the model prompt.
- Move **System Status** to its own page (no longer a side panel).
- Add the new API contract to the front-end types/api, and keep the existing `/api/chat`, `/api/ingest`, `/api/status` endpoints intact (backward compatible).

## Capabilities

### New Capabilities

- `chat-streaming`: Real-time (SSE) token streaming of assistant answers, plus multi-turn conversation history sent as context for each request.

### Modified Capabilities

- `web-frontend` (existing capability, change `openspec/specs/web-frontend/`): updated to describe the Shadcn/ui SPA with a left sidebar for navigation and system options, a ChatGPT-style chat page, and streaming.
- `chat-query` (existing): the chat interaction gains a streaming + history mode while remaining backward compatible.

## Impact

- **Backend** (`PrRag.Application`, `PrRag.Infrastructure`, `PrRag.Api`): new streaming method/endpoint using `IChatClient.GetStreamingResponseAsync`; new request DTO that carries conversation history.
- **Front-end** (`web/`): Tailwind + Shadcn/ui setup, sidebar layout, routing between Chat and Status, streaming chat client, updated types.
- **Containerization**: no new services; the existing `web` and `api` containers keep serving the updated UI.
- **Docs**: README/AGENTS updates for the new UI and the streaming/multi-turn behavior.
