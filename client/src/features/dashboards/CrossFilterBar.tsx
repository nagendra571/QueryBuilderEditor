import { Filter, Info, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { chipLabel } from '@/features/dashboards/cross-filters'
import type { CrossFilterValue } from '@/features/drill/drill-url'

/**
 * The strip of click-to-filter values above a dashboard's tiles. With the switch off the link's filters are not
 * applied, and the bar says so instead of showing chips that would mislead.
 */
export function CrossFilterBar({
  filters,
  enabled,
  onRemove,
  onClear,
}: {
  filters: CrossFilterValue[]
  enabled: boolean
  onRemove: (column: string) => void
  onClear: () => void
}) {
  if (filters.length === 0) return null

  if (!enabled) {
    return (
      <div role="status" className="flex items-center gap-2 rounded-md border border-border bg-muted/40 px-3 py-2 text-xs text-muted-foreground">
        <Info className="size-3.5 shrink-0" aria-hidden="true" />
        <span className="min-w-0 flex-1">Click-to-filter is off for this dashboard.</span>
        <Button size="sm" variant="ghost" className="h-6 text-xs" onClick={onClear}>
          Clear
        </Button>
      </div>
    )
  }

  return (
    <div role="group" aria-label="Dashboard filters" className="flex flex-wrap items-center gap-2 rounded-md border border-border bg-card px-3 py-2">
      <span className="map-label flex items-center gap-1 text-muted-foreground">
        <Filter className="size-3" aria-hidden="true" />
        Filtered by
      </span>
      {filters.map((f) => (
        <span
          key={f.column}
          className="flex max-w-full items-center gap-1 rounded-sm border border-border bg-zone-yellow/25 py-0.5 pr-0.5 pl-2 text-xs font-medium"
        >
          <span className="min-w-0 truncate">{chipLabel(f)}</span>
          <button
            type="button"
            aria-label={`Remove filter ${chipLabel(f)}`}
            className="flex size-5 shrink-0 items-center justify-center rounded-sm text-muted-foreground hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
            onClick={() => onRemove(f.column)}
          >
            <X className="size-3" aria-hidden="true" />
          </button>
        </span>
      ))}
      <Button size="sm" variant="ghost" className="ml-auto h-6 text-xs" onClick={onClear}>
        Clear all
      </Button>
    </div>
  )
}
