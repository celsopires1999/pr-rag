## Context

The chat page (`web/src/pages/ChatPage.tsx`) renders assistant messages inside a muted `Card` and currently shows a static `…` when the message has no content while streaming (`ChatPage.tsx:108`). Because the server performs retrieval (query rewrite + embedding + vector search) before the first SSE token, the user stares at this static ellipsis for potentially several seconds, with no visual feedback that the question was received and is being worked on.

The state to target is already detectable locally: `streaming === true` and the last assistant message has empty `content`. No server or SSE contract change is needed.

## Goals / Non-Goals

**Goals:**
- Provide visible motion in the empty assistant bubble during the "sent, no token yet" window.
- Replace only the static `…`; keep the existing layout, theming, and message rendering.
- Auto-hide the indicator the moment the first token arrives.
- Preserve the existing Stop/abort behavior.

**Non-Goals:**
- No changes to the API or the SSE/streaming contract.
- No server-sent progress/phase events (e.g., "searching" vs "composing").
- No changes to the sidebar, status page, or other pages.

## Decisions

### Decision: Front-end only, no contract change
The "sent but no token" state is fully knowable client-side via the existing `streaming` flag and empty content. Sending phase/progress events from the server would require changing `IAsyncEnumerable<string>` to a typed event stream and rewriting the client parser — significant scope for marginal benefit. We keep this change purely visual.
- Alternative considered: SSE phase events (`retrieving`/`generating`). Rejected as over-scoped.

### Decision: Animated three-dot indicator with "Thinking…" label
Render a small indicator inside the empty assistant bubble consisting of three dots with staggered animation plus the label "Thinking…".
- The label makes the state explicit and is screen-reader friendly.
- `tw-animate-css` is already imported (`web/src/index.css:2`), providing `animate-bounce`/`animate-pulse`; a tiny custom `@keyframes` gives a clean staggered "typing" effect.
- Alternative considered: skeleton shimmer bubble (ChatGPT-style placeholder). Rejected — heavier visual footprint; the label + dots better fits the current minimal chat UI.

### Decision: Simple conditional in ChatPage, not a separate message model
The assistant message bubble renders `ThinkingIndicator` when `streaming && !content`, otherwise the accumulated text. This avoids introducing new state or a separate message kind.
- Alternative considered: adding a `pending` boolean to `ChatMessage`. Rejected as unnecessary — the existing flags fully derive the state.

### Decision: Accessibility via `role="status"`/`aria-live`
The indicator is marked as a status region so assistive technology announces the thinking state.

## Risks / Trade-offs

- [Fast response race] If retrieval is very fast, the indicator may flash briefly before text replaces it. -> Acceptable; a short flash is standard and harmless.
- [Reduced motion] Users sensitive to animation may find the dots distracting. -> Respect `prefers-reduced-motion` by falling back to a static label/pulse if desired.
- [Long silence under slow retrieval] The indicator conveys "working" but not how long remains. -> Acceptable for the stated goal; note as non-goal (no phase granularity).
