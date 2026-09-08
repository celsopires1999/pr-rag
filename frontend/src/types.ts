export interface ChatRequest {
  question: string
  session_id?: string
  top_k?: number
  min_similarity?: number
}

export interface ChatResponse {
  answer: string
  retrievedCount: number
  session_id?: string
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
  session_id?: string
  top_k?: number
  min_similarity?: number
}

export interface CreatedRequisition {
  id: string
  supplierCode: string
  item: string
  description: string
  quantity: number
  date: string
  requester: string
  sessionId?: string | null
  createdAt: string
}

export interface CreatedRequisitionPage {
  items: CreatedRequisition[]
  total: number
  page: number
  pageSize: number
}

export type CreatedRequisitionSortField =
  | 'supplierCode'
  | 'item'
  | 'description'
  | 'quantity'
  | 'date'
  | 'requester'
  | 'createdAt'

export interface CreatedRequisitionQuery {
  page?: number
  pageSize?: number
  sortBy?: CreatedRequisitionSortField
  sortDir?: 'asc' | 'desc'
  supplierCode?: string
  item?: string
  description?: string
  requester?: string
  minQuantity?: number
  maxQuantity?: number
  dateFrom?: string
  dateTo?: string
  createdFrom?: string
  createdTo?: string
  sessionId?: string
}

export interface ApiError {
  error: string
}
