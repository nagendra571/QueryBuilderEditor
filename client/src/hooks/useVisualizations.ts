import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { visualizationsApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import type { SaveVisualizationRequest, VisualizationDto } from '@/types'

export const visualizationsKey = (queryId: string) => ['visualizations', queryId] as const
// Prefix shared by every rendered chart of a query, so one invalidation covers them all.
export const visualizationDataPrefix = (queryId: string) => ['visualization-data', queryId] as const

export function useVisualizations(queryId: string | null | undefined) {
  return useQuery({
    queryKey: visualizationsKey(queryId ?? ''),
    queryFn: () => visualizationsApi.list(queryId!),
    enabled: !!queryId,
  })
}

export function useSaveVisualization(queryId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, request }: { id?: string; request: SaveVisualizationRequest }): Promise<VisualizationDto> =>
      id ? visualizationsApi.update(queryId, id, request) : visualizationsApi.create(queryId, request),
    onSuccess: (saved, { id }) => {
      queryClient.invalidateQueries({ queryKey: visualizationsKey(queryId) })
      queryClient.invalidateQueries({ queryKey: visualizationDataPrefix(queryId) })
      queryClient.invalidateQueries({ queryKey: ['audit', queryId] })
      toast.success(id ? 'Chart updated.' : `Chart “${saved.name}” added.`)
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not save the chart.'),
  })
}

export function useDeleteVisualization(queryId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => visualizationsApi.remove(queryId, id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: visualizationsKey(queryId) })
      queryClient.removeQueries({ queryKey: visualizationDataPrefix(queryId) })
      queryClient.invalidateQueries({ queryKey: ['audit', queryId] })
      toast.success('Chart deleted.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not delete the chart.'),
  })
}

export function useVisualizationData(
  queryId: string,
  visualizationId: string,
  parameterValues: Record<string, string | null>,
  enabled: boolean,
) {
  return useQuery({
    queryKey: [...visualizationDataPrefix(queryId), visualizationId, parameterValues],
    queryFn: () => visualizationsApi.render(queryId, visualizationId, { parameterValues }),
    enabled,
    // Errors here are meaningful states (stale chart, no access, too many rows), not transient
    // failures, so retrying would only delay showing them.
    retry: false,
    staleTime: 30_000,
  })
}
