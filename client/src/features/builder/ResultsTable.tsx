import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { ResultsFooter } from '@/features/builder/results/ResultsFooter'
import { ResultsGrid } from '@/features/builder/results/ResultsGrid'
import { ResultsToolbar } from '@/features/builder/results/ResultsToolbar'
import { useResultsGrid } from '@/features/builder/results/useResultsGrid'
import type { QueryResultDto } from '@/types'

export function ResultsTable({
  result,
  onRowClick,
  rowActionLabel,
  onAddFilter,
}: {
  result: QueryResultDto
  /** Makes each row a button that opens the rows behind it (saved, aggregated queries only). */
  onRowClick?: (row: Record<string, unknown>) => void
  rowActionLabel?: string
  /** Shown beside the truncation warning. */
  onAddFilter?: () => void
}) {
  // Above the full-screen switch so entering or leaving full screen keeps search, sort and widths.
  const grid = useResultsGrid(result)
  const { view, setView, rows } = grid
  const [fullScreen, setFullScreen] = useState(false)
  const [range, setRange] = useState({ first: 0, last: 0 })
  const [selectedCells, setSelectedCells] = useState(0)
  const copyRef = useRef<{ copy: () => void }>(null)
  const fullScreenButton = useRef<HTMLButtonElement>(null)
  const wasFullScreen = useRef(false)

  useEffect(() => {
    if (!fullScreen) return
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    // The overlay is portalled to body, outside #root, so menus and tooltips (also on body) stay usable.
    const root = document.getElementById('root')
    root?.setAttribute('inert', '')
    return () => {
      document.body.style.overflow = previous
      root?.removeAttribute('inert')
    }
  }, [fullScreen])

  useEffect(() => {
    if (wasFullScreen.current !== fullScreen) fullScreenButton.current?.focus()
    wasFullScreen.current = fullScreen
  }, [fullScreen])

  if (result.columns.length === 0) {
    return <p className="p-(--space-section) text-center text-sm text-muted-foreground">Query returned no columns.</p>
  }

  const allNames = result.columns.map((c) => c.name)
  const pinned = [...view.pinnedLeft, ...view.pinnedRight].filter((n) => allNames.includes(n) && !view.hidden.includes(n))
  const noRows = rows.length === 0

  const content = (
    <div data-density={grid.density} className="flex h-full min-h-0 flex-col">
      <ResultsToolbar
        search={view.search}
        onSearchChange={(search) => setView((v) => ({ ...v, search }))}
        searchCount={result.rows.length}
        allColumns={result.columns}
        hiddenColumns={view.hidden}
        onToggleColumn={(name) =>
          setView((v) => ({
            ...v,
            hidden: v.hidden.includes(name) ? v.hidden.filter((n) => n !== name) : [...v.hidden, name],
          }))
        }
        density={grid.density}
        onDensityChange={(density) => setView((v) => ({ ...v, density }))}
        wrap={view.wrap}
        onWrapChange={(wrap) => setView((v) => ({ ...v, wrap }))}
        onCopy={() => copyRef.current?.copy()}
        isDefaultView={grid.isDefault}
        onReset={grid.reset}
        rowCount={result.rows.length}
        executionTimeMs={result.executionTimeMs}
        fullScreen={fullScreen}
        onToggleFullScreen={() => setFullScreen((f) => !f)}
        fullScreenRef={fullScreenButton}
      />

      {result.truncated && (
        <div
          role="status"
          className="flex flex-wrap items-center gap-2 border-b border-warning/40 bg-warning/10 px-(--space-row-x) py-1.5 text-xs text-warning"
        >
          <span className="size-2.5 shrink-0 bg-warning" aria-hidden />
          <span>
            More than {result.rowLimit.toLocaleString()} rows matched. You're seeing the first{' '}
            {result.rowLimit.toLocaleString()}, so sorting, search and copy here cover those rows only.
          </span>
          {onAddFilter && (
            <Button variant="link" size="sm" className="h-auto p-0 text-xs text-warning underline" onClick={onAddFilter}>
              Add a filter
            </Button>
          )}
        </div>
      )}

      {noRows ? (
        <Alert className="m-(--space-section) w-auto">
          <AlertDescription>
            {grid.totalRaw === 0 ? 'No rows matched your filters. Try widening a condition.' : 'No rows match your search.'}
          </AlertDescription>
        </Alert>
      ) : (
        <div className="min-h-0 flex-1">
          <ResultsGrid
            grid={grid}
            onRowClick={onRowClick}
            rowActionLabel={rowActionLabel}
            onVisibleRangeChange={(first, last) => setRange({ first, last })}
            onSelectionChange={setSelectedCells}
            copyRef={copyRef}
          />
        </div>
      )}

      <ResultsFooter
        first={range.first}
        last={range.last}
        filtered={rows.length}
        raw={grid.totalRaw}
        pinned={pinned}
        selectedCells={selectedCells}
      />
    </div>
  )

  if (!fullScreen) return <div className="h-full">{content}</div>

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex flex-col bg-background"
      onKeyDown={(e) => {
        if (e.key !== 'Escape' || e.defaultPrevented) return
        if ((e.target as HTMLElement).closest('[role="menu"], [role="listbox"], [role="dialog"]')) return
        setFullScreen(false)
      }}
    >
      {content}
    </div>,
    document.body,
  )
}

