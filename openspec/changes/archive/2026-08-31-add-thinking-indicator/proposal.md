## Why

The API performs retrieval (query rewrite, embedding, vector search) before the first token streams, so there is a multi-second gap between submit and the first word. The front-end currently shows a static `…` in the assistant bubble during that window, which reads as frozen or broken and leaves the user unsure the question was received and is being worked on.

## What Changes

- Replace the static `…` placeholder shown in the assistant bubble during the "sent, no token yet" window with an animated **"Thinking…"** indicator.
- The indicator consists of three animated dots (staggered keyframes) plus the label "Thinking…", styled as an assistant bubble consistent with the existing chat layout.
- The indicator appears only while the request is streaming with no content yet, and is automatically replaced by the streamed text as soon as the first token arrives.
- The existing Stop/abort behavior is preserved: aborting during the thinking state clears the empty assistant bubble.
- Front-end only. No API or streaming contract changes.

## Capabilities

### New Capabilities
- `chat-loading-indicator`: Animated "Thinking…" indicator shown in the assistant message bubble while a chat request is in flight and before any answer tokens have been received.

### Modified Capabilities
<!-- No existing capability requirements change; the indicator is a distinct visual feature captured as a new capability. -->

## Impact

- `web/src/pages/ChatPage.tsx` — render the animated indicator in the empty assistant bubble while streaming.
- `web/src/index.css` — add a small `@keyframes` for the staggered dot animation used by the indicator.
- Optional small component/extraction in `web/src/components` for the indicator.
- No changes to `PrRag.*` (server) or the SSE/streaming contract.
