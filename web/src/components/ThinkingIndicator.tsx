import { cn } from '@/lib/utils'

const DOTS = [0, 1, 2]

export function ThinkingIndicator() {
  return (
    <div
      className="flex items-center gap-1.5"
      role="status"
      aria-live="polite"
    >
      <span className="sr-only">Thinking</span>
      {DOTS.map((i) => (
        <span
          key={i}
          className={cn(
            'thinking-dot size-1.5 rounded-full bg-current',
            'animate-[thinking-dot_1.2s_ease-in-out_infinite]',
          )}
          style={{ animationDelay: `${i * 0.2}s` }}
        />
      ))}
      <span aria-hidden="true">Thinking…</span>
    </div>
  )
}
