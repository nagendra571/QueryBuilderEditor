import { useCallback, useMemo, useRef, useState, type ReactNode, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import { toast } from 'sonner'
import { useDrill } from '@/hooks/useDrill'
import { ApiError } from '@/lib/api-client'
import type {
  DashboardContext,
  QueryResultColumnDto,
  VisualizationDataDto,
  VisualizationOptions,
  VisualizationType,
} from '@/types'
import { DrillMenu } from './DrillMenu'
import { DrillTrail } from './DrillTrail'
import { ExploreValuesButton } from './ExploreValuesButton'
import { isAggregated, menuItems, stepsForMark, type ClickedMark } from './drill-model'
import {
  allowedBreakdownColumns,
  breakdownChoicesFor,
  breakdownCrumbs,
  clickedCount,
  drillUrl,
  exceedsTrail,
  mergeSteps,
  popBreakdown,
  pushBreakdown,
  type BreakdownEntry,
} from './drill-nav'
import { exploreItems } from './explore-items'
import type { CrossFilterValue, TrailStep } from './drill-url'

export interface DrillView {
  type: VisualizationType
  options: VisualizationOptions
  columns: QueryResultColumnDto[]
  rows: Record<string, unknown>[]
  truncated: boolean
  rowLimit: number
  keyColumns: string[]
}

const NONE: BreakdownEntry[] = []

interface Pending {
  mark: ClickedMark
  steps: TrailStep[]
}

/**
 * Everything a chart needs to be explored: clickable marks, the menu they open, the keyboard list of values, the
 * in-place breakdown (a stack of levels, never saved) and the trail that steps back up. The caller draws `view`
 * instead of the chart's own data and places `trail`, `explore` and `menu`.
 */
export function useChartDrill({
  queryId,
  visualization,
  base,
  baseUpdatedAt,
  parameterValues,
  context,
  crossFilters,
  onFilter,
  canFilter,
}: {
  queryId: string
  visualization: { id: string; name: string; type: VisualizationType; options: VisualizationOptions } | null | undefined
  base: VisualizationDataDto | null | undefined
  /** When the chart's own data was last fetched: a refresh draws the chart as saved again, so levels are dropped. */
  baseUpdatedAt: number
  parameterValues: Record<string, string | null>
  context?: DashboardContext
  crossFilters?: CrossFilterValue[]
  /**
   * "Filter this dashboard": the item shows only where a dashboard passes this. It receives the trail of the clicked mark.
   */
  onFilter?: (steps: TrailStep[]) => void
  /** Hides "Filter this dashboard" for a mark that has no value to filter on. */
  canFilter?: (steps: TrailStep[]) => boolean
}) {
  const navigate = useNavigate()
  const chartEl = useRef<HTMLDivElement | null>(null)
  // A callback, so the caller attaches it without handing a ref object through the hook's result.
  const chartRef = useCallback((el: HTMLDivElement | null) => {
    chartEl.current = el
  }, [])
  const exploreRef = useRef<HTMLButtonElement>(null)
  const focusRef = useRef<HTMLElement | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)

  // Dropped by comparing keys during render rather than in an effect, so no frame draws a stale level.
  const resetKey = JSON.stringify([queryId, visualization?.id, parameterValues, context ?? null, crossFilters ?? []]) + `:${baseUpdatedAt}`
  const [state, setState] = useState<{ key: string; stack: BreakdownEntry[] }>({ key: resetKey, stack: NONE })
  const stack = state.key === resetKey ? state.stack : NONE
  const setStack = useCallback((next: BreakdownEntry[]) => setState({ key: resetKey, stack: next }), [resetKey])
  const top = stack.length > 0 ? stack[stack.length - 1] : undefined

  const level = useDrill({
    queryId,
    vizId: visualization?.id,
    action: 'breakdown',
    steps: top?.steps ?? [],
    breakdownColumn: top?.breakdownColumn,
    filters: crossFilters,
    dashboardId: context?.dashboardId,
    tileId: context?.tileId,
    parameterValues,
    enabled: !!top && !!visualization,
  })

  const view = useMemo<DrillView | null>(() => {
    if (!visualization) return null
    if (top) {
      const d = level.data
      return d
        ? {
            type: d.type,
            options: d.options ?? visualization.options,
            columns: d.columns,
            rows: d.rows,
            truncated: d.truncated,
            rowLimit: d.rowLimit,
            keyColumns: [],
          }
        : null
    }
    return base
      ? {
          type: visualization.type,
          options: visualization.options,
          columns: base.columns,
          rows: base.rows,
          truncated: base.truncated ?? false,
          rowLimit: base.rowLimit,
          keyColumns: base.keyColumns ?? [],
        }
      : null
  }, [visualization, top, level.data, base])

  const allowed = useMemo(
    () => (base ? allowedBreakdownColumns(base.columns, base.keyColumns, visualization?.options.drill?.columns) : []),
    [base, visualization],
  )

  const handleMark = useCallback(
    (mark: ClickedMark, fromExplore = false) => {
      if (!view) return
      const own = stepsForMark(view.type, view.options, view.columns, view.keyColumns, mark)
      if (own === null) return
      const steps = mergeSteps(top?.steps ?? [], own)
      if (exceedsTrail(steps)) {
        toast.error('This is as far down as a chart can go. Go back up the trail first.')
        return
      }
      focusRef.current = fromExplore ? exploreRef.current : chartEl.current
      setPending({ mark, steps })
    },
    [view, top],
  )
  const onMarkClick = useCallback((mark: ClickedMark) => handleMark(mark), [handleMark])

  const explore = useMemo(
    () => (view ? exploreItems(view.type, view.options, view.rows, view.columns, view.keyColumns) : { items: [], more: 0 }),
    [view],
  )

  let menu: ReactNode = null
  if (pending && view && visualization) {
    const items = menuItems({ type: view.type, aggregated: isAggregated(view.columns), onDashboard: !!context, clickToFilter: !!onFilter && (canFilter ? canFilter(pending.steps) : true) })
    const n = clickedCount(view.columns, pending.mark.row)
    // Portalled: a dashboard tile sits in a transformed grid cell, which would make this menu's fixed position tile-relative.
    menu = createPortal(
      <DrillMenu
        anchor={pending.mark.anchor}
        items={items}
        breakdownChoices={breakdownChoicesFor(allowed, pending.steps, top)}
        rowCountHint={n}
        returnFocusRef={focusRef as RefObject<HTMLElement | null>}
        onClose={() => setPending(null)}
        onRows={() =>
          navigate(
            drillUrl({
              queryId,
              vizId: visualization.id,
              steps: pending.steps,
              filters: crossFilters,
              dashboardId: context?.dashboardId,
              tileId: context?.tileId,
              params: parameterValues,
              n,
            }),
          )
        }
        onBreakdown={(column) => setStack(pushBreakdown(stack, { steps: pending.steps, breakdownColumn: column }))}
        onFilter={() => onFilter?.(pending.steps)}
      />,
      document.body,
    )
  }

  const trail =
    stack.length > 0 && visualization ? (
      <DrillTrail
        label="Breakdown trail"
        crumbs={breakdownCrumbs(visualization.name, stack).map((c) => ({
          label: c.label,
          onSelect: () => setStack(popBreakdown(stack, c.keep)),
        }))}
      />
    ) : null

  const exploreButton =
    explore.items.length > 0 ? (
      <ExploreValuesButton items={explore.items} more={explore.more} buttonRef={exploreRef} onSelect={(mark) => handleMark(mark, true)} />
    ) : null

  return {
    /** True while a breakdown level replaces the chart's own data. */
    active: stack.length > 0,
    /** What to draw: the chart as saved, or the current breakdown level; null while a level loads or failed. */
    view,
    loading: !!top && level.isPending,
    error: top && level.isError ? (level.error instanceof ApiError ? level.error.message : 'The chart could not be broken down.') : null,
    onMarkClick,
    trail,
    explore: exploreButton,
    menu,
    chartRef,
  }
}
