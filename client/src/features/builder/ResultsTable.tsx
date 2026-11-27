import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { ColumnProfileView } from '@/features/builder/results/ColumnProfileView'
import { FormatTab } from '@/features/builder/results/FormatPanel'
import { DetailsPanel } from '@/features/builder/results/DetailsPanel'
import { RowDetails } from '@/features/builder/results/RowDetails'
import { FilterChips } from '@/features/builder/results/FilterChips'
import { ResultsFooter } from '@/features/builder/results/ResultsFooter'
import { ResultsGrid, type GridHandle } from '@/features/builder/results/ResultsGrid'
import { ResultsToolbar } from '@/features/builder/results/ResultsToolbar'
import { createGridMemory } from '@/features/builder/results/grid-memory'
import { createSelectionStore } from '@/features/builder/results/selection-store'
import { initialPanel, panelMode } from '@/features/builder/results/panel-layout'
import { isPlainLayout, type SavedLayout } from '@/features/builder/results/saved-layout'
import { useResultsGrid } from '@/features/builder/results/useResultsGrid'
import { AddToQueryDialog } from '@/features/builder/results/AddToQueryDialog'
import { withAggregates } from '@/features/builder/results/grid-totals'
import { planAddToQuery, type AddToQueryPlan } from '@/features/builder/results/filters-to-query'
import type { FilterCondition, FilterGroup, QueryColumn, QueryResultDto } from '@/types'

export function ResultsTable({
  result: ran,
  onRowClick,
  rowActionLabel,
  onAddFilter,
  queryDefinition,
  onAddToQuery,
  addToQueryDisabledReason,
  savedLayout,
  canSaveLayout,
  onSaveLayout,
  isSavingLayout,
  layoutRef,
}: {
  result: QueryResultDto
  /** Adds an icon button in each row's number cell that opens the rows behind it (saved, aggregated queries only). */
  onRowClick?: (row: Record<string, unknown>) => void
  rowActionLabel?: string
  /** Shown beside the truncation warning. */
  onAddFilter?: () => void
  /** The query as built, so the grid's filters can be planned against it. Without it there is no Add to query. */
  queryDefinition?: { columns: QueryColumn[]; filters: FilterGroup; having?: FilterGroup | null }
  onAddToQuery?: (where: FilterCondition[], having: FilterCondition[], filterCount: number) => boolean
  addToQueryDisabledReason?: string
  /** The layout saved with the query; applied to everyone who opens it. */
  savedLayout?: unknown
  canSaveLayout?: boolean
  onSaveLayout?: (layout: SavedLayout | null) => void
  isSavingLayout?: boolean
  /** Filled with a reader of the layout shown now, so the page can carry it into a Save as copy. */
  layoutRef?: React.MutableRefObject<(() => { layout: SavedLayout; plain: boolean }) | null>
}) {
  // Aggregates are stamped on the columns so formats and totals know which are sums or averages; keyed on the
  // columns so a re-render with an equal definition keeps the same result object and the grid's view.
  const definitionColumns = queryDefinition?.columns
  const result = useMemo(
    () => ({ ...ran, columns: withAggregates(ran.columns, definitionColumns ? { columns: definitionColumns } : undefined) }),
    [ran, definitionColumns],
  )
  // Above the full-screen switch so entering or leaving full screen keeps search, sort and widths.
  const grid = useResultsGrid(result, savedLayout)
  const { view, setView, rows } = grid
  const shownNames = useMemo(() => result.columns.map((c) => c.name), [result.columns])
  const plainLayout = useMemo(() => isPlainLayout(view, shownNames), [view, shownNames])
  const { currentLayout } = grid
  useEffect(() => {
    if (!layoutRef) return
    layoutRef.current = () => ({ layout: currentLayout(), plain: plainLayout })
    return () => {
      layoutRef.current = null
    }
  }, [layoutRef, currentLayout, plainLayout])
  const [fullScreen, setFullScreen] = useState(false)
  // Full screen remounts the grid in a portal; this carries its scroll position and selection across.
  const [memory] = useState(createGridMemory)
  const [range, setRange] = useState({ first: 0, last: 0 })
  // Like memory, outlives the full-screen remount; the footer and later the panel read it without re-rendering this component.
  const [store] = useState(createSelectionStore)
  const gridRef = useRef<GridHandle>(null)
  // Here, not in the grid, so a filter chip can open a column's popover.
  const [filterOpenFor, setFilterOpenFor] = useState<string | null>(null)
  const [addPlan, setAddPlan] = useState<AddToQueryPlan | null>(null)
  const focusGridAfterClose = useRef(false)
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

  // A filter applied from the Details panel clears the selection (the rows changed); put the active cell back
  // in the same column so the panel keeps showing something.
  const recenterColumn = useRef<string | null>(null)
  const fromPanel = <A extends unknown[]>(fn: (...args: A) => void) => (...args: A) => {
    recenterColumn.current = args[0] as string
    fn(...args)
  }
  useEffect(() => {
    const column = recenterColumn.current
    if (column === null) return
    recenterColumn.current = null
    const col = grid.columns.findIndex((c) => c.name === column)
    if (col >= 0 && grid.rows.length > 0) gridRef.current?.moveTo({ row: 0, col })
  }, [grid.filters, grid.rows, grid.columns])
  if (result.columns.length === 0) {
    return <p className="p-(--space-section) text-center text-sm text-muted-foreground">Query returned no columns.</p>
  }

  const allNames = result.columns.map((c) => c.name)
  const pinned = [...view.pinnedLeft, ...view.pinnedRight].filter((n) => allNames.includes(n) && !view.hidden.includes(n))
  const noRows = rows.length === 0
  const hasSearch = view.search.trim() !== ''
  const hasGridFilters = grid.filters.length > 0
  const emptyMessage =
    hasSearch && hasGridFilters
      ? 'No rows match your search and filters.'
      : hasGridFilters
        ? "No rows match the grid's filters."
        : 'No rows match your search.'

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
        totalsOn={view.totals.on}
        onTotalsChange={grid.setTotals}
        onCopy={() => gridRef.current?.copy()}
        copyDisabled={noRows}
        isDefaultView={grid.isDefault}
        onReset={grid.reset}
        rowCount={result.rows.length}
        executionTimeMs={result.executionTimeMs}
        fullScreen={fullScreen}
        onToggleFullScreen={() => setFullScreen((f) => !f)}
        fullScreenRef={fullScreenButton}
        layoutMenu={
          canSaveLayout && onSaveLayout
            ? {
                dirty: grid.layoutDirty && !(!grid.baselineIsSaved && plainLayout),
                hasSaved: grid.baselineIsSaved,
                saving: !!isSavingLayout,
                onSave: () => onSaveLayout(grid.currentLayout()),
                onClear: () => onSaveLayout(null),
              }
            : undefined
        }
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

      <FilterChips
        filters={grid.filters}
        columns={grid.allColumns}
        hidden={view.hidden}
        truncated={result.truncated}
        loadedRows={result.rows.length}
        onOpen={setFilterOpenFor}
        onRemove={grid.clearColumnFilter}
        onClearAll={grid.clearFilters}
        onAddToQuery={
          queryDefinition && onAddToQuery
            ? () => setAddPlan(planAddToQuery(grid.filters, result.columns, result.rows, queryDefinition))
            : undefined
        }
        addDisabledReason={addToQueryDisabledReason}
        onEmptied={() => {
          if (gridRef.current) gridRef.current.focus()
          else document.querySelector<HTMLInputElement>('input[aria-label="Search rows"]')?.focus()
        }}
      />

      <div ref={bandRef} className="relative flex min-h-0 flex-1">
        {noRows ? (
          <Alert className="m-(--space-section) h-fit min-w-0 flex-1">
            <AlertDescription>
              {grid.totalRaw === 0 ? 'No rows matched your filters. Try widening a condition.' : emptyMessage}
            </AlertDescription>
          </Alert>
        ) : (
          <div className="isolate flex min-h-0 min-w-0 flex-1 flex-col">
            <div className="min-h-0 flex-1">
            <ResultsGrid
              grid={grid}
              onRowClick={onRowClick}
              rowActionLabel={rowActionLabel}
              onVisibleRangeChange={(first, last) => setRange({ first, last })}
              store={store}
              gridRef={gridRef}
              memory={memory}
              filterOpenFor={filterOpenFor}
              onFilterOpenChange={setFilterOpenFor}
              onOpenRecord={() => setPanel((p) => ({ ...p, open: true, tab: 'row' }))}
              onFormat={(name) => {
                setPanel((p) => ({ ...p, open: true, tab: 'format' }))
                const col = grid.columns.findIndex((c) => c.name === name)
                if (col >= 0) gridRef.current?.moveTo({ row: store.get().range?.focus.row ?? 0, col })
              }}
            />
            </div>
            {view.totals.on && result.truncated && (
              <div role="status" className="shrink-0 border-t border-warning/40 bg-warning/20 px-(--space-row-x) py-1 text-xs text-foreground">
                Totals cover the first {result.rowLimit.toLocaleString()} loaded rows. Export and charts use all matching rows.
              </div>
            )}
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
            row={<RowDetails store={store} gridRef={gridRef} onRowClick={onRowClick} rowActionLabel={rowActionLabel} onShowOnly={fromPanel(grid.showOnlyValue)} textFor={(c, v) => grid.compiled(c).text(v)} />}
            column={
              <ColumnProfileView
                store={store}
                result={result}
                rows={grid.filterRows}
                filtered={grid.filters.length > 0}
                compiled={grid.compiled}
                formats={grid.formats}
                onShowOnly={fromPanel(grid.showOnlyValue)}
                onHide={fromPanel(grid.hideColumnValue)}
                onRange={(f) => {
                  recenterColumn.current = f.column
                  grid.setColumnFilter(f)
                }}
              />
            }
            format={
              <FormatTab
                store={store}
                columns={result.columns}
                rows={grid.filterRows}
                formats={grid.formats}
                totals={view.totals}
                onChange={grid.setColumnFormat}
                onTotalChange={grid.setTotalFn}
                onMove={(pos) => gridRef.current?.moveTo(pos)}
              />
            }
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

  const confirmAdd = () => {
    if (!addPlan || !onAddToQuery) return
    const where = addPlan.add.filter((p) => p.clause === 'where').flatMap((p) => p.conditions)
    const having = addPlan.add.filter((p) => p.clause === 'having').flatMap((p) => p.conditions)
    if (!onAddToQuery(where, having, addPlan.add.length)) {
      setAddPlan(null)
      return
    }
    const added = addPlan.add.map((p) => p.column)
    grid.removeFilters(added)
    // With no chips left the bar unmounts, taking the "Add to query" trigger Radix would return focus to.
    focusGridAfterClose.current = grid.filters.every((f) => added.includes(f.column))
    setAddPlan(null)
  }
  const dialog = (
    <AddToQueryDialog
      plan={addPlan}
      onConfirm={confirmAdd}
      onCancel={() => setAddPlan(null)}
      onCloseAutoFocus={(e) => {
        if (!focusGridAfterClose.current) return
        focusGridAfterClose.current = false
        e.preventDefault()
        if (gridRef.current) gridRef.current.focus()
        else document.querySelector<HTMLInputElement>('input[aria-label="Search rows"]')?.focus()
      }}
    />
  )

  if (!fullScreen) {
    return (
      <div className="h-full">
        {content}
        {dialog}
      </div>
    )
  }

  return createPortal(
    <div className="fixed inset-0 z-50 flex flex-col bg-background">
      {content}
      {dialog}
    </div>,
    document.body,
  )
}

