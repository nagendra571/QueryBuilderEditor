import { useCallback, useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { ColumnProfileView } from '@/features/builder/results/ColumnProfileView'
import { DetailsPanel } from '@/features/builder/results/DetailsPanel'
import { RowDetails } from '@/features/builder/results/RowDetails'
import { ResultsFooter } from '@/features/builder/results/ResultsFooter'
import { ResultsGrid, type GridHandle } from '@/features/builder/results/ResultsGrid'
import { ResultsToolbar } from '@/features/builder/results/ResultsToolbar'
import { createGridMemory } from '@/features/builder/results/grid-memory'
import { createSelectionStore } from '@/features/builder/results/selection-store'
import { initialPanel, panelMode } from '@/features/builder/results/panel-layout'
import { useResultsGrid } from '@/features/builder/results/useResultsGrid'
import type { QueryResultDto } from '@/types'

export function ResultsTable({
  result,
  onRowClick,
  rowActionLabel,
  onAddFilter,
}: {
  result: QueryResultDto
  /** Adds an icon button in each row's number cell that opens the rows behind it (saved, aggregated queries only). */
  onRowClick?: (row: Record<string, unknown>) => void
  rowActionLabel?: string
  /** Shown beside the truncation warning. */
  onAddFilter?: () => void
}) {
  // Above the full-screen switch so entering or leaving full screen keeps search, sort and widths.
  const grid = useResultsGrid(result)
  const { view, setView, rows } = grid
  const [fullScreen, setFullScreen] = useState(false)
  // Full screen remounts the grid in a portal; this carries its scroll position and selection across.
  const [memory] = useState(createGridMemory)
  const [range, setRange] = useState({ first: 0, last: 0 })
  // Like memory, outlives the full-screen remount; the footer and later the panel read it without re-rendering this component.
  const [store] = useState(createSelectionStore)
  const gridRef = useRef<GridHandle>(null)
  const fullScreenButton = useRef<HTMLButtonElement>(null)
  const detailsButton = useRef<HTMLButtonElement>(null)
  const wasFullScreen = useRef(false)
  // Lives here, not in the grid, so it survives full screen and re-runs; Reset view leaves it alone.
  const [panel, setPanel] = useState(initialPanel)
  const [areaWidth, setAreaWidth] = useState(0)
  const bandObserver = useRef<ResizeObserver | null>(null)
  // A callback ref: the band remounts when full screen toggles.
  const bandRef = useCallback((el: HTMLDivElement | null) => {
    bandObserver.current?.disconnect()
    bandObserver.current = null
    if (!el) return
    setAreaWidth(el.clientWidth)
    bandObserver.current = new ResizeObserver(() => setAreaWidth(el.clientWidth))
    bandObserver.current.observe(el)
  }, [])

  // A run that returns no columns renders no grid or exit button, so it must not stay in full screen.
  if (fullScreen && result.columns.length === 0) setFullScreen(false)

  useEffect(() => {
    if (!fullScreen) return
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    // The overlay is portalled to body, outside #root, so menus and tooltips (also on body) stay usable.
    const root = document.getElementById('root')
    root?.setAttribute('inert', '')
    // On the document, after React's handlers: a control that consumes Escape calls preventDefault first, and
    // the press still exits when focus has fallen back to body (e.g. after a resize handle lost focus).
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== 'Escape' || e.defaultPrevented) return
      if ((e.target as Element | null)?.closest?.('[role="menu"], [role="listbox"], [role="dialog"]')) return
      setFullScreen(false)
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.body.style.overflow = previous
      root?.removeAttribute('inert')
      document.removeEventListener('keydown', onKeyDown)
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
        detailsOpen={panel.open}
        onToggleDetails={() => setPanel((p) => ({ ...p, open: !p.open }))}
        detailsRef={detailsButton}
        onCopy={() => gridRef.current?.copy()}
        copyDisabled={noRows}
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
          className="flex flex-wrap items-center gap-2 border-b border-warning/40 bg-warning/20 px-(--space-row-x) py-1.5 text-xs text-foreground"
        >
          <span className="size-2.5 shrink-0 bg-warning" aria-hidden />
          <span>
            More than {result.rowLimit.toLocaleString()} rows matched. You're seeing the first{' '}
            {result.rowLimit.toLocaleString()}, so sorting, search and copy here cover those rows only.
          </span>
          {onAddFilter && (
            <Button variant="link" size="sm" className="h-auto p-0 text-xs text-foreground underline" onClick={onAddFilter}>
              Add a filter
            </Button>
          )}
        </div>
      )}

      <div ref={bandRef} className="relative flex min-h-0 flex-1">
        {noRows ? (
          <Alert className="m-(--space-section) h-fit min-w-0 flex-1">
            <AlertDescription>
              {grid.totalRaw === 0 ? 'No rows matched your filters. Try widening a condition.' : 'No rows match your search.'}
            </AlertDescription>
          </Alert>
        ) : (
          <div className="min-h-0 min-w-0 flex-1">
            <ResultsGrid
              grid={grid}
              onRowClick={onRowClick}
              rowActionLabel={rowActionLabel}
              onVisibleRangeChange={(first, last) => setRange({ first, last })}
              store={store}
              gridRef={gridRef}
              memory={memory}
            />
          </div>
        )}
        {panel.open && (
          <DetailsPanel
            state={panel}
            onChange={setPanel}
            onClose={() => {
              setPanel((p) => ({ ...p, open: false }))
              if (store.get().range) gridRef.current?.focus()
              else detailsButton.current?.focus()
            }}
            mode={panelMode(areaWidth)}
            areaWidth={areaWidth}
            row={<RowDetails store={store} gridRef={gridRef} onRowClick={onRowClick} rowActionLabel={rowActionLabel} />}
            column={<ColumnProfileView store={store} result={result} />}
          />
        )}
      </div>

      <ResultsFooter
        first={range.first}
        last={range.last}
        filtered={rows.length}
        raw={grid.totalRaw}
        pinned={pinned}
        store={store}
      />
    </div>
  )

  if (!fullScreen) return <div className="h-full">{content}</div>

  return createPortal(
    <div className="fixed inset-0 z-50 flex flex-col bg-background">
      {content}
    </div>,
    document.body,
  )
}

