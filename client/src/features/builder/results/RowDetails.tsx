import { useSyncExternalStore } from 'react'
import { toast } from 'sonner'
import { Check, ChevronDown, ChevronUp, Copy, Filter, X as XIcon } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { buildRecordText } from './copy-selection.ts'
import { copyText } from './copy-text.ts'
import { displayString } from './grid-columns.ts'
import { profileKind } from './column-profile.ts'
import { TypeBadge } from './TypeBadge'
import type { GridHandle } from './ResultsGrid'
import type { SelectionStore } from './selection-store.ts'

interface Props {
  store: SelectionStore
  gridRef: React.RefObject<GridHandle | null>
  onRowClick?: (row: Record<string, unknown>) => void
  rowActionLabel?: string
  onShowOnly: (column: string, raw: unknown) => void
}

export function RowDetails({ store, gridRef, onRowClick, rowActionLabel, onShowOnly }: Props) {
  const { range, rows, columns } = useSyncExternalStore(store.subscribe, store.get)
  const focus = range?.focus
  const row = focus ? rows[focus.row] : undefined
  if (!range || !focus || !row) {
    return <p className="p-3 text-xs text-muted-foreground">Select a cell to see its row.</p>
  }

  const copyRecord = () => {
    if (copyText(buildRecordText(row, columns))) toast('Copied record')
    else toast.error('Copy failed.')
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex shrink-0 flex-wrap items-center gap-1 border-b border-border px-3 py-2">
        <span aria-live="polite" aria-atomic="true" className="font-numeric mr-auto text-sm font-semibold tracking-wide tabular-nums">
          ROW {(focus.row + 1).toLocaleString()} OF {rows.length.toLocaleString()}
        </span>
        <Button
          variant="outline"
          size="icon-sm"
          aria-label="Previous row"
          aria-disabled={focus.row <= 0 ? 'true' : undefined}
          className={focus.row <= 0 ? 'opacity-50' : undefined}
          onClick={() => focus.row > 0 && gridRef.current?.moveTo({ row: focus.row - 1, col: focus.col })}
        >
          <ChevronUp />
        </Button>
        <Button
          variant="outline"
          size="icon-sm"
          aria-label="Next row"
          aria-disabled={focus.row >= rows.length - 1 ? 'true' : undefined}
          className={focus.row >= rows.length - 1 ? 'opacity-50' : undefined}
          onClick={() => focus.row < rows.length - 1 && gridRef.current?.moveTo({ row: focus.row + 1, col: focus.col })}
        >
          <ChevronDown />
        </Button>
        <Button variant="outline" size="sm" onClick={copyRecord}>
          <Copy /> Copy record
        </Button>
        {onRowClick && (
          <Button variant="outline" size="sm" onClick={() => onRowClick(row)}>
            {rowActionLabel ?? 'See these rows'}
          </Button>
        )}
      </div>
      <dl className="min-h-0 flex-1 overflow-y-auto">
        {columns.map((c, i) => {
          const value = row[c.name]
          const isNumber = profileKind(c.dataType) === 'number'
          const active = i === focus.col
          return (
            <div
              key={c.name}
              aria-current={active ? 'true' : undefined}
              className={`group/field border-b border-border px-3 py-2 ${active ? 'bg-primary/10' : ''}`}
            >
              <dt className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <span className="min-w-0 truncate">{c.name}</span>
                <TypeBadge dataType={c.dataType} />
                <button
                  type="button"
                  aria-label="Show only this value"
                  title="Show only this value"
                  onClick={() => onShowOnly(c.name, value)}
                  className="ml-auto flex size-6 shrink-0 items-center justify-center text-muted-foreground opacity-0 group-focus-within/field:opacity-100 group-hover/field:opacity-100 hover:text-foreground focus-visible:opacity-100 focus-visible:outline-2 focus-visible:outline-ring"
                >
                  <Filter className="size-3.5" />
                </button>
              </dt>
              <dd
                className={`mt-0.5 text-sm text-foreground ${
                  isNumber ? 'font-numeric text-right tabular-nums' : 'whitespace-pre-wrap break-words'
                }`}
              >
                <DetailValue value={value} />
              </dd>
            </div>
          )
        })}
      </dl>
    </div>
  )
}

function DetailValue({ value }: { value: unknown }) {
  if (value === null || value === undefined || value === '') {
    return <span className="text-muted-foreground italic">empty</span>
  }
  if (typeof value === 'boolean') {
    return value ? (
      <Check className="inline size-4 text-success" aria-label="true" />
    ) : (
      <XIcon className="inline size-4 text-muted-foreground" aria-label="false" />
    )
  }
  return <>{displayString(value)}</>
}
