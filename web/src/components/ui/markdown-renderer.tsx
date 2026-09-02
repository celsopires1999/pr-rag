import { useState, useCallback, type ReactNode } from 'react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import rehypeHighlight from 'rehype-highlight'
import { Copy, Check } from 'lucide-react'
import 'highlight.js/styles/github-dark.css'

function CodeBlock({ children, className, ...props }: { children?: ReactNode; className?: string }) {
  const [copied, setCopied] = useState(false)
  const match = /language-(\w+)/.exec(className || '')
  const language = match ? match[1] : ''
  const code = String(children).replace(/\n$/, '')

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(code).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    })
  }, [code])

  if (!className) {
    return (
      <code className="rounded bg-muted px-1.5 py-0.5 text-sm" {...props}>
        {children}
      </code>
    )
  }

  return (
    <div className="group relative my-3">
      {language && (
        <div className="absolute top-0 left-0 rounded-br rounded-tl bg-muted px-2 py-0.5 text-xs text-muted-foreground">
          {language}
        </div>
      )}
      <button
        onClick={handleCopy}
        className="absolute top-2 right-2 rounded-md border bg-muted p-1 opacity-0 transition-opacity hover:bg-accent group-hover:opacity-100"
        aria-label="Copy code"
      >
        {copied ? <Check className="size-3.5 text-green-500" /> : <Copy className="size-3.5" />}
      </button>
      <pre className="overflow-x-auto rounded-lg border bg-[#0d1117] p-4 text-sm leading-relaxed">
        <code className={className} {...props}>
          {children}
        </code>
      </pre>
    </div>
  )
}

interface MarkdownRendererProps {
  content: string
}

export function MarkdownRenderer({ content }: MarkdownRendererProps) {
  return (
    <Markdown
      remarkPlugins={[remarkGfm]}
      rehypePlugins={[rehypeHighlight]}
      components={{
        code: CodeBlock,
        table: ({ children }) => (
          <div className="my-3 overflow-x-auto rounded-lg border">
            <table className="w-full border-collapse text-sm">{children}</table>
          </div>
        ),
        th: ({ children }) => (
          <th className="border-b bg-muted px-3 py-2 text-left font-medium">{children}</th>
        ),
        td: ({ children }) => (
          <td className="border-b px-3 py-2">{children}</td>
        ),
        a: ({ href, children }) => (
          <a
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            className="font-medium text-primary underline underline-offset-4 hover:text-primary/80"
          >
            {children}
          </a>
        ),
        ul: ({ children }) => (
          <ul className="my-2 list-disc space-y-1 pl-6">{children}</ul>
        ),
        ol: ({ children }) => (
          <ol className="my-2 list-decimal space-y-1 pl-6">{children}</ol>
        ),
        li: ({ children }) => <li className="text-sm leading-relaxed">{children}</li>,
        h1: ({ children }) => (
          <h1 className="mt-6 mb-3 text-xl font-bold first:mt-0">{children}</h1>
        ),
        h2: ({ children }) => (
          <h2 className="mt-5 mb-2 text-lg font-semibold first:mt-0">{children}</h2>
        ),
        h3: ({ children }) => (
          <h3 className="mt-4 mb-2 text-base font-semibold first:mt-0">{children}</h3>
        ),
        p: ({ children }) => (
          <p className="my-2 text-sm leading-relaxed last:mb-0">{children}</p>
        ),
        blockquote: ({ children }) => (
          <blockquote className="my-3 border-l-4 border-primary/30 pl-4 text-muted-foreground italic">
            {children}
          </blockquote>
        ),
        hr: () => <hr className="my-4 border-border" />,
        strong: ({ children }) => <strong className="font-semibold">{children}</strong>,
        em: ({ children }) => <em className="italic">{children}</em>,
      }}
    >
      {content}
    </Markdown>
  )
}
