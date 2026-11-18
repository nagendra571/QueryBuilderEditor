import { useQuery } from '@tanstack/react-query'
import { savedQueriesApi, visualizationsApi } from '@/lib/api'
import { encodeFilters, encodeTrail, type CrossFilterValue, type TrailStep } from '@/features/drill/drill-url'
import type { DrillAction, DrillResultDto } from '@/types'

export interface DrillArgs {
  queryId: string
  /** Absent for the results-grid drill, which has no chart. */
  vizId?: string | null
  action?: DrillAction
  steps: TrailStep[]
  breakdownColumn?: string | null
  filters?: CrossFilterValue[]
  dashboardId?: string | null
  tileId?: string | null
  parameterValues: Record<string, string | null>
  enabled?: boolean
}

export const drillKey = (a: Omit<DrillArgs, 'enabled'>) =>
  [
    'drill',
    a.queryId,
    a.vizId ?? null,
    encodeTrail(a.steps),
    encodeFilters(a.filters ?? []),
    a.dashboardId ?? null,
    a.tileId ?? null,
    a.parameterValues,
    a.action ?? 'rows',
    a.breakdownColumn ?? null,
  ] as const

/** Rows behind a mark or a regrouped chart. The server rebuilds everything from the saved query; only the trail goes up. */
export function useDrill(args: DrillArgs) {
  return useQuery<DrillResultDto>({
    queryKey: drillKey(args),
    queryFn: async () => {
      const filters = args.filters ?? []
      if (!args.vizId) return savedQueriesApi.drillQuery(args.queryId, { steps: args.steps, parameterValues: args.parameterValues })
      return visualizationsApi.drillVisualization(args.queryId, args.vizId, {
        action: args.action ?? 'rows',
        steps: args.steps,
        breakdownColumn: args.breakdownColumn ?? null,
        crossFilters: filters.length > 0 ? filters : undefined,
        parameterValues: args.parameterValues,
        dashboardId: args.dashboardId ?? null,
        tileId: args.tileId ?? null,
      })
    },
    enabled: args.enabled ?? true,
    // A refusal (no access, too many rows, a stale chart) is an answer, not a blip.
    retry: false,
    staleTime: 30_000,
  })
}
