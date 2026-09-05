import { useState } from 'react'
import type { IngestResult } from '@/types'
import { ingest } from '@/api'

export function useIngest() {
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<IngestResult | null>(null)

  async function run() {
    setError(null)
    setResult(null)
    setLoading(true)
    try {
      setResult(await ingest())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong.')
    } finally {
      setLoading(false)
    }
  }

  return { run, loading, error, result }
}
