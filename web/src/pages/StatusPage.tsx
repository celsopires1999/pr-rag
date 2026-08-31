import { useEffect, useState } from 'react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { status } from '@/api'
import type { SystemStatus } from '@/types'

const REFRESH_MS = 10000

export function StatusPage() {
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [value, setValue] = useState<SystemStatus | null>(null)

  async function load() {
    try {
      setValue(await status())
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    load()
    const id = setInterval(load, REFRESH_MS)
    return () => clearInterval(id)
  }, [])

  return (
    <div className="mx-auto max-w-2xl">
      <h1 className="mb-4 text-2xl font-semibold">System Status</h1>
      {loading && !value && <p className="text-muted-foreground">Loading…</p>}
      {error && <p className="mb-2 text-sm text-destructive">{error}</p>}
      {value && (
        <Card>
          <CardHeader>
            <CardTitle>Ingestion</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2 text-sm">
            <div className="flex justify-between border-b py-2">
              <span className="text-muted-foreground">Requisitions</span>
              <span className="font-medium">{value.requisitionCount}</span>
            </div>
            <div className="flex justify-between border-b py-2">
              <span className="text-muted-foreground">Embedded</span>
              <span className="font-medium">{value.embeddedCount}</span>
            </div>
            <div className="flex justify-between py-2">
              <span className="text-muted-foreground">Last sync</span>
              <span className="font-medium">
                {value.lastSync
                  ? new Date(value.lastSync).toLocaleString()
                  : 'never'}
              </span>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
