# Chat Message Markdown

## Purpose

Render assistant chat messages as formatted Markdown, including GitHub Flavored Markdown (tables, task lists, strikethrough) and syntax-highlighted fenced code blocks, styled consistently with the shadcn/ui theme.

## Requirements

### Requirement: Markdown rendering for assistant messages
The chat front-end SHALL render assistant messages as Markdown, interpreting headings, bold, italic, inline code, fenced code blocks, lists (ordered and unordered), tables, links, images, task lists, and strikethrough.

#### Scenario: Render fenced code block
- **WHEN** the assistant message contains a fenced code block (e.g. ````sql\nSELECT ...\n````)
- **THEN** the code is displayed in a styled `<pre><code>` block with syntax highlighting appropriate to the language tag

#### Scenario: Render bold and italic text
- **WHEN** the assistant message contains `**bold**` or `*italic*` Markdown
- **THEN** the rendered output shows bold and italic styled text respectively

#### Scenario: Render tables
- **WHEN** the assistant message contains a GFM table
- **THEN** the table is rendered as an HTML `<table>` with visible borders and cell padding matching the theme

#### Scenario: Render lists
- **WHEN** the assistant message contains ordered or unordered list items
- **THEN** the items are rendered as `<ol>` / `<ul>` with proper indentation

#### Scenario: Render links
- **WHEN** the assistant message contains a `[text](url)` link
- **THEN** the link is rendered as a clickable `<a>` element that opens in a new tab

### Requirement: Code block copy button
Each fenced code block in the rendered Markdown SHALL include a "Copy" button that copies the code content to the clipboard.

#### Scenario: Copy code to clipboard
- **WHEN** the user clicks the Copy button on a code block
- **THEN** the code content is copied to the system clipboard and a brief visual confirmation is shown

### Requirement: Streaming-compatible rendering
The Markdown renderer SHALL re-render correctly as content arrives incrementally during SSE streaming, without visual glitches or errors from incomplete Markdown syntax.

#### Scenario: Partial code fence during streaming
- **WHEN** the assistant is streaming and the current content contains an opening code fence but no closing fence yet
- **THEN** the partial content is rendered without errors (e.g. shown as plain preformatted text until the closing fence arrives)

### Requirement: Theme-consistent styling
The rendered Markdown SHALL use typography, colors, and spacing consistent with the existing shadcn/ui neutral theme.

#### Scenario: Dark mode code blocks
- **WHEN** the Markdown renderer displays a code block
- **THEN** the code block background and text colors match the project's dark theme palette

#### Scenario: Typography matches UI
- **WHEN** the Markdown renderer displays headings, paragraphs, or lists
- **THEN** font families and sizes are consistent with the rest of the chat interface
