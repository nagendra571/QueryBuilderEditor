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
