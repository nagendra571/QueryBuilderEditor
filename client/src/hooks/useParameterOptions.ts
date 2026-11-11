import { useQuery } from '@tanstack/react-query'
import { parametersApi, savedQueriesApi } from '@/lib/api'
import type { DashboardContext, ParameterOption, QueryParameter } from '@/types'

/** Where a query-list control gets its choices: a parameter of a saved query, or one still being edited. */
export type OptionsSource =
  | { kind: 'saved'; queryId: string; name: string; context?: DashboardContext }
  | { kind: 'draft'; parameter: QueryParameter }

// Never cached for long: the server runs the source query as the signed-in user, so what is offered
// depends on who is asking and on data that changes. A short staleTime only de-duplicates remounts.
export function useParameterOptions(source: OptionsSource | null, enabled = true) {
  const key =
    source?.kind === 'saved'
      ? (['parameter-options', 'saved', source.queryId, source.name, source.context?.dashboardId, source.context?.tileId] as const)
      : (['parameter-options', 'draft', source?.parameter.optionsQueryId, source?.parameter.optionsLabelColumn, source?.parameter.optionsValueColumn] as const)

  return useQuery<ParameterOption[]>({
    queryKey: key,
    queryFn: async () =>
      source!.kind === 'saved'
        ? savedQueriesApi.parameterOptions(source!.queryId, source!.name, source!.context)
        : parametersApi.previewOptions(source!.parameter),
    enabled: enabled && !!source && (source.kind === 'saved' || !!source.parameter.optionsQueryId),
    staleTime: 15_000,
    retry: false,
  })
}
