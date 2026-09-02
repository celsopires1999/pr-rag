## MODIFIED Requirements

### Requirement: ChatGPT-style chat page
The front-end SHALL render the Chat page as a message list with a prominent input at the bottom, matching the familiar ChatGPT layout, and SHALL stream assistant answers incrementally and render them as Markdown-formatted content as they arrive.

#### Scenario: Stream assistant answer
- **WHEN** the user submits a question
- **THEN** the application displays the user message and streams the assistant reply token-by-token into the message list, rendering Markdown formatting (code blocks, bold, lists, tables, links) in real time

#### Scenario: Multi-turn in session
- **WHEN** a conversation already has prior user/assistant messages
- **THEN** the current question is sent together with the prior history so the assistant has conversation context
