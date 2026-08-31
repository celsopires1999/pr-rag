## 1. Backend: streaming service + endpoint

- [x] 1.1 Add `ChatStreamRequest` DTO (`Question`, `TopK`, `MinSimilarity`, `Messages`) and a `ChatMessageDto` (`Role`, `Content`) in `PrRag.Application/DTOs`
- [x] 1.2 Add `IAsyncEnumerable<string> StreamAsync(ChatStreamRequest, CancellationToken)` to `IChatService`
- [x] 1.3 Refactor `ChatService` to share the RAG retrieval logic between `AnswerAsync` and the new `StreamAsync`
- [x] 1.4 Implement `StreamAsync`: retrieve context for the current question, build a prompt that includes conversation history + retrieved context, stream tokens via `IChatClient.GetStreamingResponseAsync`, and write the RAG report once
- [x] 1.5 Add `POST /api/chat/stream` SSE endpoint in `PrRag.Api/Program.cs`

## 2. Front-end: Shadcn/ui + Tailwind + routing

- [x] 2.1 Install Tailwind CSS and initialize Shadcn/ui in `web/` (`npx shadcn init`), add Tailwind config/postcss
- [x] 2.2 Add Shadcn components: button, input, card, label, scroll-area, separator, sidebar, dialog, tooltip
- [x] 2.3 Add `react-router-dom`, define routes `/` (Chat) and `/status`, with a persistent `Layout` (sidebar + Outlet)

## 3. Front-end: sidebar, context, API

- [x] 3.1 Build the Shadcn `Sidebar`: nav links (Chat, Status) + bottom section with `top_k`/`min_similarity` inputs and an ingest button
- [x] 3.2 Add a `RagSettingsContext` providing `top_k`/`min_similarity` shared between the sidebar and the Chat page
- [x] 3.3 Add `chatStream()` in `web/src/api.ts` that posts to `/api/chat/stream` and reads the SSE stream incrementally; keep existing `chat()` for compatibility

## 4. Front-end: pages

- [x] 4.1 Build the ChatGPT-style Chat page: message list (user/assistant bubbles) + bottom input; streams the assistant reply and renders tokens as they arrive; sends multi-turn history
- [x] 4.2 Build the System Status page (requisition/embedded counts + last sync, auto-refresh)
- [x] 4.3 Update `web/src/types.ts` with `ChatStreamRequest`, `ChatMessage` and related types
- [x] 4.4 Replace the old `App.tsx` panels wiring with the new routed layout

## 5. Verification

- [x] 5.1 `cd web && npm run build` passes (types + vite build)
- [x] 5.2 `dotnet build PrRag.sln` passes (SDK container)
- [x] 5.3 `dotnet test tests/PrRag.Tests` passes (SDK container, db running)
- [x] 5.4 End-to-end via `docker compose --profile demo up --build web api`; confirm sidebar nav, RAG params, ingest, and streamed multi-turn chat
- [x] 5.5 Update README/AGENTS for the new UI and streaming endpoint
