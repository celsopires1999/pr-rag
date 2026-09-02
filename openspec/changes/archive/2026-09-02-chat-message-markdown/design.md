## Context

The chat front-end (`ChatPage.tsx`) currently renders assistant messages as plain text using `whitespace-pre-wrap`. The API already returns Markdown-formatted answers (fenced code blocks, bold, lists, headings), but the UI strips all visual structure. The project uses Vite + React 19 + Tailwind 4 with shadcn/ui (radix-nova style). The existing component library is in `components/ui/`. The devcontainer environment runs as `vscode` user.

## Goals / Non-Goals

**Goals:**
- Render Markdown in assistant chat messages with proper formatting (headings, bold, italic, lists, tables, links, fenced code blocks with syntax highlighting).
- Match the existing shadcn/ui theme — code blocks should use the project's color palette.
- Handle streaming correctly: the renderer must re-render cleanly as tokens arrive incrementally.
- Keep bundle size reasonable; avoid heavy Markdown engines.

**Non-Goals:**
- Rendering Markdown in user messages (user messages stay plain text).
- LaTeX / KaTeX math rendering.
- Interactive or editable Markdown.
- Changing the API response format.

## Decisions

### 1. Markdown library: `react-markdown` + `remark-gfm` + `rehype-highlight`

**Chosen:** `react-markdown` (lightweight, React-native, no dangerouslySetInnerHTML by default) with `remark-gfm` for GitHub Flavored Markdown (tables, task lists, strikethrough) and `rehype-highlight` for fenced code block syntax highlighting.

**Alternatives considered:**
- `marked` + manual DOM: lower-level, requires manual sanitization. More work, same result.
- `markdown-it`: battle-tested but not React-native; requires a wrapper. Heavier.
- `rehype-raw` (for HTML in Markdown): not needed — the API does not return raw HTML.

**Rationale:** `react-markdown` is the most common choice in React ecosystems, actively maintained, and tree-shakeable. `rehype-highlight` uses highlight.js under the hood, which is well-established for syntax highlighting without a full editor integration.

### 2. Code highlighting theme

Use highlight.js's `github-dark` theme (or similar) imported as a CSS file, adapted to match the shadcn neutral dark palette. The code block wrapper will receive a shadcn `Card`-like background.

**Rationale:** Matches the existing dark UI. A single CSS import is low-cost and consistent.

### 3. Component structure

Create a single `MarkdownRenderer` component in `components/ui/markdown-renderer.tsx` (shadcn convention). It wraps `react-markdown` with the necessary plugins and custom renderers (e.g. custom `code` block renderer for syntax highlighting + copy button).

**Rationale:** Keeps it in the `ui/` directory consistent with other shadcn primitives, even though it's more of a composite component. The project's convention is all UI primitives in `components/ui/`.

### 4. Streaming compatibility

`react-markdown` re-renders on each prop change. During streaming, `content` grows with each token. This is fine for typical message lengths (< 10k chars). No special debouncing is needed — React's batched updates handle the rest.

**Rationale:** Simplicity. Debouncing adds complexity with no measurable benefit for this use case.

### 5. No sanitization layer beyond react-markdown defaults

`react-markdown` does not use `dangerouslySetInnerHTML` by default and escapes everything. No additional DOMPurify layer is needed.

**Rationale:** Secure by default. The API content is LLM-generated text, not user-uploaded HTML.

## Risks / Trade-offs

- **Bundle size increase**: `react-markdown` + `remark-gfm` + `rehype-highlight` add ~30-40 kB gzipped. → Acceptable for a chat UI; these are well-optimized libraries.
- **highlight.js CSS weight**: The full highlight.js theme CSS is ~10 kB. → Can be scoped to only the languages the API actually returns (SQL, Python, JSON, etc.) via rehype-highlight options.
- **Incomplete Markdown rendering on partial tokens**: Streaming may produce temporary broken Markdown (e.g. unclosed code fence). → `react-markdown` handles this gracefully — it renders what it can and completes when the closing fence arrives.
- **Theme drift**: Code block colors may diverge from shadcn over time. → Mitigated by using CSS custom properties from the theme where possible.
