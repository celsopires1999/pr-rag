import type {
  ChatRequest,
  ChatResponse,
  IngestResult,
  SystemStatus,
  ChatStreamRequest,
  ApiError,
} from './types'

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8080'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    headers: init?.body ? { 'Content-Type': 'application/json' } : undefined,
    ...init,
  })

  if (!response.ok) {
    let message = `Request failed with status ${response.status}`
    try {
      const body = (await response.json()) as ApiError
      if (body?.error) message = body.error
    } catch {
      // ignore body parse errors
    }
    throw new Error(message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

export async function chat(body: ChatRequest): Promise<ChatResponse> {
  return request<ChatResponse>('/api/chat', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export async function ingest(): Promise<IngestResult> {
  return request<IngestResult>('/api/ingest', { method: 'POST' })
}

export async function status(): Promise<SystemStatus> {
  return request<SystemStatus>('/api/status', { method: 'GET' })
}

export async function chatStream(
  body: ChatStreamRequest,
  onToken: (token: string) => void,
  signal?: AbortSignal,
): Promise<string> {
  const response = await fetch(`${BASE_URL}/api/chat/stream`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  })

  if (!response.ok) {
    let message = `Request failed with status ${response.status}`
    try {
      const err = (await response.json()) as ApiError
      if (err?.error) message = err.error
    } catch {
      // ignore
    }
    throw new Error(message)
  }

  if (!response.body) {
    throw new Error('Streaming is not supported by this browser.')
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  let fullText = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })

    const lines = buffer.split('\n')
    buffer = lines.pop() ?? ''

    for (const line of lines) {
      const idx = line.indexOf('data:')
      if (idx === -1) continue
      // payload = tudo após "data:"; remove apenas o espaço do separador SSE,
      // preservando espaços de prefixo legítimos dos tokens (ex.: " Olá,")
      const raw = line.slice(idx + 5)
      const data = raw.startsWith(' ') ? raw.slice(1) : raw
      if (data === '[DONE]') return fullText
      if (data) {
        fullText += data
        onToken(data)
      }
    }
  }

  return fullText
}
