## Why

The chat assistant returns Markdown-formatted text (headings, code blocks, lists, bold, etc.) but the front-end renders everything as plain text with `whitespace-pre-wrap`. This truncates visual structure and makes responses hard to read. We need a proper Markdown renderer so the UI respects formatting from the API.

## What Changes

- Add a Markdown rendering component that parses assistant messages and renders formatted HTML (headings, code blocks with syntax highlighting, lists, bold, links, tables).
- Replace the plain-text `whitespace-pre-wrap` rendering in `ChatPage.tsx` with the new Markdown component for assistant messages.
- Add a Markdown-capable npm dependency (e.g. `react-markdown` + `remark-gfm` + `rehype-highlight` or similar).
- Ensure the Markdown component is styled to match the existing shadcn/ui theme (colors, typography, code block appearance).

## Capabilities

### New Capabilities
- `chat-message-markdown`: Markdown rendering for assistant chat messages, including GFM support (tables, task lists, strikethrough) and fenced code block highlighting.

### Modified Capabilities
- `web-frontend`: The chat page message rendering requirement changes from plain-text display to Markdown-rendered display for assistant messages.

## Impact

- **Frontend code**: `ChatPage.tsx` message rendering block changes; new component added to `components/ui/` or `components/`.
- **Dependencies**: New npm packages for Markdown parsing and syntax highlighting.
- **Styling**: Code block and typography styles must integrate with the existing Tailwind + shadcn theme.
- **Streaming**: The renderer must handle incremental content updates as tokens arrive during streaming.
- **No API changes**: The backend already returns Markdown; this is purely a frontend rendering change.
