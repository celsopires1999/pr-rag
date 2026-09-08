import { type ReactNode, useCallback, useEffect, useRef, useState } from 'react'
import {
  ChevronLeft,
  ChevronRight,
  ChevronsUpDown,
  ChevronUp,
  ChevronDown,
  ListFilter,
  RefreshCw,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { listCreatedRequisitions } from '@/api'
import { cn } from '@/lib/utils'
import type {
  CreatedRequisition,
  CreatedRequisitionPage,
  CreatedRequisitionQuery,
  CreatedRequisitionSortField,
} from '@/types'

const SESSION_ID_KEY = 'prrag.session_id'
const PAGE_SIZE = 20
const FILTER_DEBOUNCE_MS = 300

type SessionScope = 'all' | 'current'

interface Filters {
  supplierCode: string
  item: string
  description: string
  requester: string
  minQuantity: string
  maxQuantity: string
  dateFrom: string
  dateTo: string
  createdFrom: string
  createdTo: string
}

const EMPTY_FILTERS: Filters = {
  supplierCode: '',
  item: '',
  description: '',
  requester: '',
  minQuantity: '',
  maxQuantity: '',
  dateFrom: '',
  dateTo: '',
  createdFrom: '',
  createdTo: '',
}

interface Column {
  field: CreatedRequisitionSortField
  label: string
  className?: string
}

const COLUMNS: Column[] = [
  { field: 'supplierCode', label: 'Supplier' },
  { field: 'item', label: 'Item' },
  { field: 'description', label: 'Description' },
  { field: 'quantity', label: 'Quantity' },
  { field: 'date', label: 'Date' },
  { field: 'requester', label: 'Requester' },
  { field: 'createdAt', label: 'Created at' },
]

function currentSessionId(): string | null {
  return localStorage.getItem(SESSION_ID_KEY)
}

function formatDate(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number)
  const date = new Date(year, month - 1, day)
  return Number.isNaN(date.getTime()) ? isoDate : date.toLocaleDateString()
}

function excerpt(text: string, maxWords = 10): string {
  const trimmed = text.trim()
  const words = trimmed.split(/\s+/).filter(Boolean)
  if (words.length <= maxWords) return trimmed
  return `${words.slice(0, maxWords).join(' ')}...`
}

const COLUMN_FILTER_KEYS: Record<
  CreatedRequisitionSortField,
  ReadonlyArray<keyof Filters>
> = {
  supplierCode: ['supplierCode'],
  item: ['item'],
  description: ['description'],
  quantity: ['minQuantity', 'maxQuantity'],
  date: ['dateFrom', 'dateTo'],
  requester: ['requester'],
  createdAt: ['createdFrom', 'createdTo'],
}

function isColumnFiltered(
  column: CreatedRequisitionSortField,
  filters: Filters,
): boolean {
  return COLUMN_FILTER_KEYS[column].some((key) => filters[key] !== '')
}

export function RequisitionsPage() {
  const [sessionScope, setSessionScope] = useState<SessionScope>('all')
  const [sortField, setSortField] = useState<CreatedRequisitionSortField>('createdAt')
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc')
  const [page, setPage] = useState(1)
  const [filters, setFilters] = useState<Filters>(EMPTY_FILTERS)
  const [data, setData] = useState<CreatedRequisitionPage | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<CreatedRequisition | null>(null)
  const [openFilter, setOpenFilter] = useState<CreatedRequisitionSortField | null>(null)
  const abortRef = useRef<AbortController | null>(null)

  function buildQuery(): CreatedRequisitionQuery {
    const query: CreatedRequisitionQuery = {
      page,
      pageSize: PAGE_SIZE,
      sortBy: sortField,
      sortDir,
    }
    if (filters.supplierCode) query.supplierCode = filters.supplierCode
    if (filters.item) query.item = filters.item
    if (filters.description) query.description = filters.description
    if (filters.requester) query.requester = filters.requester
    if (filters.minQuantity) query.minQuantity = Number(filters.minQuantity)
    if (filters.maxQuantity) query.maxQuantity = Number(filters.maxQuantity)
    if (filters.dateFrom) query.dateFrom = filters.dateFrom
    if (filters.dateTo) query.dateTo = filters.dateTo
    if (filters.createdFrom) query.createdFrom = filters.createdFrom
    if (filters.createdTo) query.createdTo = filters.createdTo
    if (sessionScope === 'current') {
      const sessionId = currentSessionId()
      if (sessionId) query.sessionId = sessionId
    }
    return query
  }

  const load = useCallback(
    async () => {
      abortRef.current?.abort()
      const controller = new AbortController()
      abortRef.current = controller
      const signal = controller.signal
      setLoading(true)
      setError(null)
      try {
        const result = await listCreatedRequisitions(buildQuery(), signal)
        if (signal.aborted) return
        setData(result)
      } catch (err) {
        if (signal.aborted) return
        setError(err instanceof Error ? err.message : 'Something went wrong.')
      } finally {
        if (!signal.aborted) setLoading(false)
      }
    },
    // load rebuilds its query from these inputs.
    // oxlint-disable-next-line react-hooks/exhaustive-deps
    [sessionScope, sortField, sortDir, page, filters],
  )

  // Re-run whenever the query inputs change. The page remounts on
  // navigation, so this also serves as refetch-on-navigation.
  useEffect(() => {
    const timer = setTimeout(load, FILTER_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [load])

  useEffect(() => () => abortRef.current?.abort(), [])

  function toggleSort(field: CreatedRequisitionSortField) {
    if (field === sortField) {
      setSortDir((dir) => (dir === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortField(field)
      setSortDir('desc')
    }
    setPage(1)
  }

  function setFilter(key: keyof Filters, value: string) {
    setFilters((prev) => ({ ...prev, [key]: value }))
    setPage(1)
  }

  function clearColumnFilter(column: CreatedRequisitionSortField) {
    setFilters((prev) => {
      const next = { ...prev }
      for (const key of COLUMN_FILTER_KEYS[column]) next[key] = ''
      return next
    })
    setPage(1)
    setOpenFilter(null)
  }

  function changeSessionScope(scope: SessionScope) {
    setSessionScope(scope)
    setPage(1)
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1
  const sessionHasCurrent = currentSessionId() !== null
  const effectiveScope: SessionScope =
    sessionScope === 'current' && !sessionHasCurrent ? 'all' : sessionScope

  return (
    <div className="mx-auto max-w-6xl">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">Requisitions</h1>
        <div className="flex items-center gap-2">
          <div className="flex overflow-hidden rounded-md border">
            <button
              type="button"
              onClick={() => changeSessionScope('all')}
              className={cn(
                'px-3 py-1.5 text-sm',
                effectiveScope === 'all'
                  ? 'bg-muted font-medium text-foreground'
                  : 'bg-background text-muted-foreground hover:text-foreground',
              )}
            >
              All sessions
            </button>
            <button
              type="button"
              onClick={() => changeSessionScope('current')}
              className={cn(
                'px-3 py-1.5 text-sm',
                effectiveScope === 'current'
                  ? 'bg-muted font-medium text-foreground'
                  : 'bg-background text-muted-foreground hover:text-foreground',
              )}
            >
              Current session
            </button>
          </div>
          <Button
            variant="outline"
            onClick={() => load()}
            disabled={loading}
            title="Refresh"
          >
            <RefreshCw className={cn('size-4', loading && 'animate-spin')} />
            <span className="sr-only">Refresh</span>
          </Button>
        </div>
      </div>

      {error && <p className="mb-2 text-sm text-destructive">{error}</p>}

      <Card>
        <CardHeader>
          <CardTitle className="text-sm font-medium text-muted-foreground">
            {loading && !data
              ? 'Loading…'
              : data
                ? `${data.total} requisition${data.total === 1 ? '' : 's'}`
                : ''}
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow className="hover:bg-transparent">
                  {COLUMNS.map((column) => (
                    <TableHead key={column.field} className="align-bottom">
                    <div className="flex items-center justify-between gap-1">
                      <button
                        type="button"
                        onClick={() => toggleSort(column.field)}
                        className="flex items-center gap-1 font-medium text-foreground hover:text-foreground"
                      >
                        {column.label}
                        {sortField === column.field ? (
                          sortDir === 'asc' ? (
                            <ChevronUp className="size-3.5" />
                          ) : (
                            <ChevronDown className="size-3.5" />
                          )
                        ) : (
                          <ChevronsUpDown className="size-3.5 text-muted-foreground" />
                        )}
                      </button>
                      <Popover
                        open={openFilter === column.field}
                        onOpenChange={(open) =>
                          setOpenFilter(open ? column.field : null)
                        }
                      >
                        <PopoverTrigger asChild>
                          <button
                            type="button"
                            aria-label={`Filter by ${column.label}`}
                            title={isColumnFiltered(column.field, filters)
                              ? 'Filter active'
                              : 'Filter'}
                            className={cn(
                              'flex size-6 shrink-0 items-center justify-center rounded',
                              isColumnFiltered(column.field, filters)
                                ? 'bg-primary text-primary-foreground'
                                : 'text-muted-foreground hover:bg-muted hover:text-foreground',
                            )}
                          >
                            <ListFilter className="size-3.5" />
                          </button>
                        </PopoverTrigger>
                        <PopoverContent className="w-64">
                          <div className="space-y-3">
                            <FilterControls
                              column={column.field}
                              filters={filters}
                              onFilter={setFilter}
                            />
                            <Button
                              variant="outline"
                              size="sm"
                              className="w-full"
                              onClick={() => clearColumnFilter(column.field)}
                            >
                              Clear
                            </Button>
                          </div>
                        </PopoverContent>
                      </Popover>
                    </div>
                  </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody>
                {loading && !data
                  ? Array.from({ length: 5 }).map((_, i) => (
                      <TableRow key={i}>
                        {COLUMNS.map((column) => (
                          <TableCell key={column.field}>
                            <Skeleton className="h-4 w-full" />
                          </TableCell>
                        ))}
                      </TableRow>
                    ))
                  : data && data.items.length === 0
                    ? (
                      <TableRow>
                        <TableCell
                          colSpan={COLUMNS.length}
                          className="h-24 text-center text-muted-foreground"
                        >
                          No requisitions match the current filters.
                        </TableCell>
                      </TableRow>
                    )
                    : (data?.items ?? []).map((item) => (
                        <TableRow
                          key={item.id}
                          className="cursor-pointer"
                          onClick={() => setSelected(item)}
                        >
                          <TableCell className="font-mono">{item.supplierCode}</TableCell>
                          <TableCell className="font-mono">{item.item}</TableCell>
                          <TableCell className="max-w-64 break-words whitespace-normal">
                            {excerpt(item.description)}
                          </TableCell>
                          <TableCell>{item.quantity}</TableCell>
                          <TableCell>{formatDate(item.date)}</TableCell>
                          <TableCell>{item.requester}</TableCell>
                          <TableCell>
                            {new Date(item.createdAt).toLocaleString()}
                          </TableCell>
                        </TableRow>
                      ))}
              </TableBody>
            </Table>
          </div>

          {data && (
            <div className="flex items-center justify-between text-sm">
              <span className="text-muted-foreground">
                Page {data.page} of {totalPages}
              </span>
              <div className="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={data.page <= 1 || loading}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                >
                  <ChevronLeft className="size-4" />
                  <span className="sr-only">Previous</span>
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={data.page >= totalPages || loading}
                  onClick={() => setPage((p) => p + 1)}
                >
                  <ChevronRight className="size-4" />
                  <span className="sr-only">Next</span>
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      <Dialog open={selected !== null} onOpenChange={(open) => !open && setSelected(null)}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>Requisition details</DialogTitle>
            <DialogDescription>
              Created on {selected ? new Date(selected.createdAt).toLocaleString() : ''}
            </DialogDescription>
          </DialogHeader>
          {selected && (
            <div className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
              <DetailRow label="Supplier code" value={selected.supplierCode} mono />
              <DetailRow label="Item" value={selected.item} mono />
              <DetailRow label="Quantity" value={String(selected.quantity)} />
              <DetailRow label="Date" value={formatDate(selected.date)} />
              <DetailRow label="Requester" value={selected.requester} />
              <DetailRow
                label="Session"
                value={selected.sessionId ?? '—'}
                mono={Boolean(selected.sessionId)}
              />
              <div className="col-span-2">
                <p className="mb-1 text-xs text-muted-foreground">Description</p>
                <p className="rounded-md bg-muted/50 p-3">{selected.description}</p>
              </div>
            </div>
          )}
        </DialogContent>
      </Dialog>
    </div>
  )
}

function FilterControls({
  column,
  filters,
  onFilter,
}: {
  column: CreatedRequisitionSortField
  filters: Filters
  onFilter: (key: keyof Filters, value: string) => void
}) {
  const inputClass =
    'h-8 w-full px-2 text-xs focus-visible:ring-1'

  switch (column) {
    case 'supplierCode':
      return (
        <FilterField label="Supplier code">
          <Input
            type="text"
            placeholder="Filter…"
            autoFocus
            className={inputClass}
            value={filters.supplierCode}
            onChange={(e) => onFilter('supplierCode', e.target.value)}
          />
        </FilterField>
      )
    case 'item':
      return (
        <FilterField label="Item">
          <Input
            type="text"
            placeholder="Filter…"
            autoFocus
            className={inputClass}
            value={filters.item}
            onChange={(e) => onFilter('item', e.target.value)}
          />
        </FilterField>
      )
    case 'description':
      return (
        <FilterField label="Description">
          <Input
            type="text"
            placeholder="Filter…"
            autoFocus
            className={inputClass}
            value={filters.description}
            onChange={(e) => onFilter('description', e.target.value)}
          />
        </FilterField>
      )
    case 'requester':
      return (
        <FilterField label="Requester">
          <Input
            type="text"
            placeholder="Filter…"
            autoFocus
            className={inputClass}
            value={filters.requester}
            onChange={(e) => onFilter('requester', e.target.value)}
          />
        </FilterField>
      )
    case 'quantity':
      return (
        <div className="grid grid-cols-2 gap-2">
          <FilterField label="Min">
            <Input
              type="number"
              placeholder="0"
              className={inputClass}
              value={filters.minQuantity}
              onChange={(e) => onFilter('minQuantity', e.target.value)}
            />
          </FilterField>
          <FilterField label="Max">
            <Input
              type="number"
              placeholder="∞"
              className={inputClass}
              value={filters.maxQuantity}
              onChange={(e) => onFilter('maxQuantity', e.target.value)}
            />
          </FilterField>
        </div>
      )
    case 'date':
      return (
        <div className="grid grid-cols-2 gap-2">
          <FilterField label="From">
            <Input
              type="date"
              className={inputClass}
              value={filters.dateFrom}
              onChange={(e) => onFilter('dateFrom', e.target.value)}
            />
          </FilterField>
          <FilterField label="To">
            <Input
              type="date"
              className={inputClass}
              value={filters.dateTo}
              onChange={(e) => onFilter('dateTo', e.target.value)}
            />
          </FilterField>
        </div>
      )
    case 'createdAt':
      return (
        <div className="grid grid-cols-2 gap-2">
          <FilterField label="From">
            <Input
              type="date"
              className={inputClass}
              value={filters.createdFrom}
              onChange={(e) => onFilter('createdFrom', e.target.value)}
            />
          </FilterField>
          <FilterField label="To">
            <Input
              type="date"
              className={inputClass}
              value={filters.createdTo}
              onChange={(e) => onFilter('createdTo', e.target.value)}
            />
          </FilterField>
        </div>
      )
  }
}

function FilterField({
  label,
  children,
}: {
  label: string
  children: ReactNode
}) {
  return (
    <label className="block space-y-1 text-xs text-muted-foreground">
      {label}
      {children}
    </label>
  )
}

function DetailRow({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div>
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className={mono ? 'font-mono' : ''}>{value}</p>
    </div>
  )
}