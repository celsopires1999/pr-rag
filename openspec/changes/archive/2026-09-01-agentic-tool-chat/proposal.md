## Why

The current chat pipeline always runs the same fixed retrieval sequence (reg-parse → query rewriter → vector search) on every question, then stuffs results into a prompt. It cannot adapt to questions that need no retrieval, only exact lookups, or multiple distinct lookups. Moving to agentic tool use lets the chat model decide whether (and which) PostgreSQL tools to invoke — choosing what context to retrieve instead of running a rigid pipeline every time.

## What Changes

- Replace the hardcoded retrieval pipeline in `ChatService` with a tool-calling flow: the chat model is given the full ongoing conversation and decides whether to call one of two PostgreSQL tools.
- Introduce two retrievable tools backed by two PostgreSQL queries:
  - **Exact-match lookup**: query represented by exact `ITM-*` / `SUP*` codes (bound to `SearchByCodesAsync`).
  - **Semantic vector search**: query embedded and matched by similarity (bound to `SearchAsync`).
- Send the **full conversation context** (all prior user/assistant turns plus the current question) to the chat model, so follow-up questions remain a continuous experience and tool results from earlier turns stay available.
- Ground the final answer on the context the model actually retrieved via tool calls, and route both non-streaming and streaming paths through the same tool-calling flow.
- Keep `top_k` / `min_similarity` configurable and preserved on the API contract. **BREAKING** for the internal retrieval-only semantics of `ChatService` (the rewriter and reg-based dispatch are removed), though the public `POST /api/chat` and `/api/chat/stream` response shapes stay compatible.

## Capabilities

### New Capabilities
- `agentic-retrieval`: The chat model may call tool functions that execute PostgreSQL lookups (exact code lookup and semantic vector search) to choose what context to retrieve.
- `continuous-conversation`: The chat flow sends the full conversation history and returns tool-resolved context per turn so the conversation is continuous across multiple turns.

### Modified Capabilities
- `chat-query`: Superseded by the agentic retrieval flow for non-streaming answers.
- `chat-streaming`: Superseded by the agentic retrieval flow for streamed answers.

## Impact

- `src/PrRag.Application` — `ChatService`, `IChatService`, DTOs (`ChatStreamRequest` / `ChatRequest`), new tool definitions and retrieval abstractions.
- `src/PrRag.Infrastructure` — function-calling chat client wiring; `PurchaseRequisitionRepository` gains tool-accessible methods; tool-call translation.
- `src/PrRag.Api` — RAG settings registration, tool-function registration into DI.
- `openspec/specs` — `chat-query`, `chat-streaming` requirement deltas; new `agentic-retrieval`, `continuous-conversation` specs.
- `tests/PrRag.Tests` — fakes for tool-calling client; integration coverage for tool-selected retrieval and full-context grounding.
- `web/` — verify it sends the full rolling history for continuity.
