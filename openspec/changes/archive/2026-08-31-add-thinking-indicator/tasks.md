## 1. CSS animation

- [x] 1.1 Add a `@keyframes` for the staggered dot bounce/pulse in `web/src/index.css` (e.g., `thinking-dot`)
- [x] 1.2 Apply a `prefers-reduced-motion` fallback so the dots degrade to a static state for users with reduced-motion preferences

## 2. Indicator component

- [x] 2.1 Create a `ThinkingIndicator` component (e.g., `web/src/components/ThinkingIndicator.tsx`) rendering three dots with staggered animation plus the "Thinking…" label
- [x] 2.2 Mark the component with `role="status"`/`aria-live="polite"` for screen readers

## 3. Wire into chat page

- [x] 3.1 In `web/src/pages/ChatPage.tsx`, replace the static `'…'` placeholder (line 108) so the empty assistant bubble renders `ThinkingIndicator` while `streaming && !content`
- [x] 3.2 Confirm the indicator disappears as soon as the first token arrives (content becomes non-empty), and is cleared on Stop/abort (streaming becomes false)

## 4. Verify

- [x] 4.1 Run `cd web && npm install && npm run build` to confirm the front-end builds cleanly
