import { useQuery } from '@tanstack/react-query'
import { dataSourcesApi } from '@/lib/api'

export function useDataSources() {
  return useQuery({ queryKey: ['data-sources'], queryFn: dataSourcesApi.list })
}

export function useDataSourceCatalog(dataSourceId: string | undefined) {
  return useQuery({
    queryKey: ['data-source-catalog', dataSourceId],
    queryFn: () => dataSourcesApi.catalog(dataSourceId!),
    enabled: !!dataSourceId,
    staleTime: 5 * 60_000,
  })
}

export function useAppUsers(dataSourceId: string | undefined) {
  return useQuery({
    queryKey: ['app-users', dataSourceId],
    queryFn: () => dataSourcesApi.appUsers(dataSourceId!),
    enabled: !!dataSourceId,
    staleTime: 60_000,
  })
}

/** Hub mode: dashboards have no single data source, so their share dialog lists the people launched into the app. */
export function usePeople(enabled: boolean) {
  return useQuery({
    queryKey: ['people'],
    queryFn: dataSourcesApi.people,
    enabled,
    staleTime: 60_000,
  })
}
