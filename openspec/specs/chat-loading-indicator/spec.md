# chat-loading-indicator

## Purpose

Display visual feedback to the user while a chat response is being generated.

## Requirements

### Requirement: Animated loading indicator while waiting for the first token
The front-end SHALL display an animated "Thinking…" indicator in the empty assistant message bubble whenever a chat request is in flight and no answer content has been received yet, so the user can see the question was submitted and is being processed.

#### Scenario: Indicator shown after submitting a question
- **WHEN** the user submits a question and the assistant bubble has no content yet
- **THEN** the front-end displays an animated "Thinking…" indicator (three staggered dots plus the label) inside the assistant bubble

#### Scenario: Indicator hidden once streaming begins
- **WHEN** the first answer token arrives
- **THEN** the "Thinking…" indicator is replaced by the streamed answer text

#### Scenario: Indicator cleared on user abort
- **WHEN** the user stops the request while the indicator is still shown and before any content arrives
- **THEN** the empty assistant bubble is removed and no indicator is left behind
