## 1. Dependencies

- [x] 1.1 Install `react-markdown`, `remark-gfm`, `rehype-highlight`, and `highlight.js` in `web/`
- [x] 1.2 Add highlight.js CSS theme import (e.g. `github-dark`) to the project entry point or component

## 2. Markdown Renderer Component

- [x] 2.1 Create `web/src/components/ui/markdown-renderer.tsx` with a `MarkdownRenderer` component that wraps `react-markdown` with `remark-gfm` and `rehype-highlight`
- [x] 2.2 Add a custom `code` block renderer with a "Copy to clipboard" button and visual confirmation
- [x] 2.3 Style code blocks, tables, lists, and links to match the shadcn/ui neutral theme

## 3. Integrate into ChatPage

- [x] 3.1 In `ChatPage.tsx`, replace the plain-text rendering (`whitespace-pre-wrap` + `m.content`) for assistant messages with `<MarkdownRenderer content={m.content} />`
- [x] 3.2 Keep user messages rendered as plain text (no Markdown)
- [x] 3.3 Verify streaming behavior: partial Markdown (unclosed fences) renders without errors

## 4. Verify

- [x] 4.1 Run `cd web && npm run build` to confirm no build errors
- [x] 4.2 Manual smoke test: send a question that triggers a code block, verify syntax highlighting and copy button work
- [x] 4.3 Verify tables, bold, lists, and links render correctly in the chat UI
