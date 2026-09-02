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
  let pendingData: string | null = null

  while (true) {
    const { done, value } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })

    const lines = buffer.split('\n')
    buffer = lines.pop() ?? ''

    for (const line of lines) {
      if (line === '') {
        // Event boundary — flush accumulated multiline data
        if (pendingData !== null) {
          fullText += pendingData
          onToken(pendingData)
          pendingData = null
        }
        continue
      }

      const idx = line.indexOf('data:')
      if (idx === -1) continue

      const raw = line.slice(idx + 5)
      const data = raw.startsWith(' ') ? raw.slice(1) : raw

      if (data === '[DONE]') {
        if (pendingData !== null) {
          fullText += pendingData
          onToken(pendingData)
        }
        return fullText
      }

      // SSE spec: multiple data: lines in one event are joined with \n
      pendingData = pendingData !== null ? pendingData + '\n' + data : data
    }
  }

  // Flush any remaining data if [DONE] was not received
  if (pendingData !== null) {
    fullText += pendingData
    onToken(pendingData)
  }

  return fullText
}
