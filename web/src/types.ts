export interface ChatRequest {
  question: string
  top_k?: number
  min_similarity?: number
}

export interface ChatResponse {
  answer: string
  retrievedCount: number
}

export interface IngestResult {
  totalRecords: number
  inserted: number
  updated: number
  embedded: number
}

export interface SystemStatus {
  requisitionCount: number
  embeddedCount: number
  lastSync?: string | null
}

export interface ChatMessage {
  role: 'user' | 'assistant'
  content: string
}

export interface ChatStreamRequest {
  question: string
  top_k?: number
  min_similarity?: number
  messages: ChatMessage[]
}

export interface ApiError {
  error: string
}
