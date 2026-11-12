import { useQuery } from '@tanstack/react-query'
import { dashboardsApi, parametersApi, savedQueriesApi } from '@/lib/api'
import type { DashboardContext, ParameterOption, QueryParameter } from '@/types'

/** Where a query-list control gets its choices: a parameter of a saved query, or one still being edited. */
export type OptionsSource =
  | { kind: 'saved'; queryId: string; name: string; context?: DashboardContext }
  | { kind: 'draft'; parameter: QueryParameter }
  /** Hub mode: a dashboard's own parameter, answered as the caller whether or not they can open its source query. */
  | { kind: 'dashboard'; dashboardId: string; name: string }

// Never cached for long: the server runs the source query as the signed-in user, so what is offered
// depends on who is asking and on data that changes. A short staleTime only de-duplicates remounts.
export function useParameterOptions(source: OptionsSource | null, enabled = true) {
  const key =
    source?.kind === 'saved'
      ? (['parameter-options', 'saved', source.queryId, source.name, source.context?.dashboardId, source.context?.tileId] as const)
      : source?.kind === 'dashboard'
        ? (['parameter-options', 'dashboard', source.dashboardId, source.name] as const)
        : (['parameter-options', 'draft', source?.parameter.optionsQueryId, source?.parameter.optionsLabelColumn, source?.parameter.optionsValueColumn] as const)

  return useQuery<ParameterOption[]>({
    queryKey: key,
    queryFn: async () =>
      source!.kind === 'saved'
        ? savedQueriesApi.parameterOptions(source!.queryId, source!.name, source!.context)
        : source!.kind === 'dashboard'
          ? dashboardsApi.parameterOptions(source!.dashboardId, source!.name)
          : parametersApi.previewOptions(source!.parameter),
    enabled: enabled && !!source && (source.kind !== 'draft' || !!source.parameter.optionsQueryId),
    staleTime: 15_000,
    retry: false,
  })
}
