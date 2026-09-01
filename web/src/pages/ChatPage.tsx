import { useEffect, useRef, useState } from 'react'
import { Send } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { Card, CardContent } from '@/components/ui/card'
import { ThinkingIndicator } from '@/components/ThinkingIndicator'
import { chatStream } from '@/api'
import { useRagSettings } from '@/context/RagSettingsContext'
import type { ChatMessage } from '@/types'
import { cn } from '@/lib/utils'

export function ChatPage() {
  const { topK, minSimilarity } = useRagSettings()
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [input, setInput] = useState('')
  const [streaming, setStreaming] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const endRef = useRef<HTMLDivElement>(null)
  const abortRef = useRef<AbortController | null>(null)

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    const question = input.trim()
    if (!question || streaming) return

    const userMessage: ChatMessage = { role: 'user', content: question }
    const history = [...messages]
    setMessages([...history, userMessage])
    setInput('')
    setError(null)
    setStreaming(true)

    const assistantMessage: ChatMessage = { role: 'assistant', content: '' }
    setMessages((prev) => [...prev, assistantMessage])

    const controller = new AbortController()
    abortRef.current = controller

    try {
      await chatStream(
        {
          question,
          top_k: topK ?? undefined,
          min_similarity: minSimilarity ?? undefined,
          messages: history,
        },
        (token) => {
          setMessages((prev) => {
            const next = [...prev]
            const last = next[next.length - 1]
            if (last && last.role === 'assistant') {
              next[next.length - 1] = { ...last, content: last.content + token }
            }
            return next
          })
        },
        controller.signal,
      )
    } catch (err) {
      if (err instanceof Error && err.name === 'AbortError') {
        // aborted by the user
      } else {
        setError(err instanceof Error ? err.message : 'Something went wrong.')
      }
    } finally {
      setStreaming(false)
      abortRef.current = null
    }
  }

  function handleStop() {
    abortRef.current?.abort()
  }

  return (
    <div className="flex h-[calc(100vh-6rem)] flex-col">
      <div className="flex-1 overflow-y-auto">
        <div className="mx-auto flex max-w-3xl flex-col gap-6 py-4">
          {messages.length === 0 && (
            <div className="mt-16 text-center text-muted-foreground">
              <p className="text-lg font-medium">How can I help you today?</p>
              <p className="text-sm">
                Ask about purchase requisitions, suppliers, or items.
              </p>
            </div>
          )}

          {messages.map((m, i) => (
            <div
              key={i}
              className={cn(
                'flex',
                m.role === 'user' ? 'justify-end' : 'justify-start',
              )}
            >
              <Card
                className={cn(
                  'max-w-[80%] border',
                  m.role === 'user'
                    ? 'bg-primary text-primary-foreground'
                    : 'bg-muted',
                )}
              >
                <CardContent className="whitespace-pre-wrap px-4 py-3 text-sm leading-relaxed">
                  {streaming && !m.content ? <ThinkingIndicator /> : m.content}
                </CardContent>
              </Card>
            </div>
          ))}

          {error && <p className="text-center text-sm text-destructive">{error}</p>}
          <div ref={endRef} />
        </div>
      </div>

      <form
        onSubmit={handleSubmit}
        className="mx-auto w-full max-w-3xl border-t pt-3"
      >
        <div className="flex items-end gap-2">
          <Textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault()
                handleSubmit(e)
              }
            }}
            placeholder="Ask a question…"
            rows={1}
            className="max-h-40 flex-1 resize-none"
          />
          {streaming ? (
            <Button type="button" variant="secondary" onClick={handleStop}>
              Stop
            </Button>
          ) : (
            <Button type="submit" disabled={!input.trim()}>
              <Send className="size-4" />
              Send
            </Button>
          )}
        </div>
      </form>
    </div>
  )
}
