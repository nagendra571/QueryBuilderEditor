import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { dashboardsApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import type { SaveDashboardRequest, SaveDashboardTilesRequest, ShareDashboardRequest } from '@/types'

export const dashboardsKey = ['dashboards'] as const
export const dashboardKey = (id: string) => ['dashboard', id] as const
const dashboardSharesKey = (id: string) => ['dashboard-shares', id] as const

function message(error: unknown, fallback: string) {
  return error instanceof ApiError ? error.message : fallback
}

/** The server's optimistic-concurrency refusal. The page shows it inline with a Reload button. */
export function isVersionConflict(error: unknown) {
  return error instanceof ApiError && error.status === 409 && error.title === 'Conflict'
}

export function useDashboards() {
  return useQuery({ queryKey: dashboardsKey, queryFn: dashboardsApi.list })
}

export function useDashboard(id: string | undefined) {
  return useQuery({
    queryKey: dashboardKey(id ?? ''),
    queryFn: () => dashboardsApi.getById(id!),
    enabled: !!id,
    retry: false,
  })
}

export function useCreateDashboard() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: SaveDashboardRequest) => dashboardsApi.create(request),
    onSuccess: (created) => {
      queryClient.setQueryData(dashboardKey(created.id), created)
      queryClient.invalidateQueries({ queryKey: dashboardsKey })
    },
    onError: (error) => toast.error(message(error, 'Could not create the dashboard.')),
  })
}

export function useRenameDashboard(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: SaveDashboardRequest) => dashboardsApi.rename(id, request),
    onSuccess: (saved) => {
      queryClient.setQueryData(dashboardKey(id), saved)
      queryClient.invalidateQueries({ queryKey: dashboardsKey })
      toast.success('Dashboard renamed.')
    },
    onError: (error) => {
      if (!isVersionConflict(error)) toast.error(message(error, 'Could not rename the dashboard.'))
    },
  })
}

export function useSaveDashboardTiles(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: SaveDashboardTilesRequest) => dashboardsApi.saveTiles(id, request),
    onSuccess: (saved) => {
      queryClient.setQueryData(dashboardKey(id), saved)
      queryClient.invalidateQueries({ queryKey: dashboardsKey })
      toast.success('Dashboard saved.')
    },
    onError: (error) => {
      if (!isVersionConflict(error)) toast.error(message(error, 'Could not save the dashboard.'))
    },
  })
}

/** From the query page: appends a chart to a dashboard without opening it. */
export function useAddChartToDashboard() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ dashboardId, visualizationId }: { dashboardId: string; visualizationId: string }) =>
      dashboardsApi.addChart(dashboardId, visualizationId),
    onSuccess: (_, { dashboardId }) => {
      // The dashboard's cached detail no longer has the new tile (or the bumped version).
      queryClient.invalidateQueries({ queryKey: dashboardKey(dashboardId) })
      queryClient.invalidateQueries({ queryKey: dashboardsKey })
    },
    onError: (error) => toast.error(message(error, 'Could not add the chart to that dashboard.')),
  })
}

export function useDeleteDashboard() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => dashboardsApi.remove(id),
    onSuccess: (_, id) => {
      queryClient.removeQueries({ queryKey: dashboardKey(id) })
      queryClient.removeQueries({ queryKey: dashboardSharesKey(id) })
      queryClient.invalidateQueries({ queryKey: dashboardsKey })
      toast.success('Dashboard deleted.')
    },
    onError: (error) => toast.error(message(error, 'Could not delete the dashboard.')),
  })
}

export function useDashboardShares(id: string | undefined) {
  return useQuery({
    queryKey: dashboardSharesKey(id ?? ''),
    queryFn: () => dashboardsApi.getShares(id!),
    enabled: !!id,
  })
}

export function useShareDashboard(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ShareDashboardRequest) => dashboardsApi.share(id, request),
    onSuccess: (_, request) => {
      queryClient.invalidateQueries({ queryKey: dashboardSharesKey(id) })
      queryClient.invalidateQueries({ queryKey: ['dashboard-share-preview', id] })
      // Queries shared along with the dashboard change their own share lists too.
      for (const queryId of request.alsoShareQueryIds ?? []) {
        queryClient.invalidateQueries({ queryKey: ['query-shares', queryId] })
      }
      queryClient.invalidateQueries({ queryKey: ['saved-queries'] })
      toast.success('Access granted.')
    },
    onError: (error) => toast.error(message(error, 'Could not share this dashboard.')),
  })
}

export function useRemoveDashboardShare(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (shareId: string) => dashboardsApi.removeShare(id, shareId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: dashboardSharesKey(id) })
      queryClient.invalidateQueries({ queryKey: ['dashboard-share-preview', id] })
      toast.success('Access removed.')
    },
    onError: (error) => toast.error(message(error, 'Could not remove access.')),
  })
}

export function useDashboardSharePreview(id: string, userId: string | undefined) {
  return useQuery({
    queryKey: ['dashboard-share-preview', id, userId ?? ''],
    queryFn: () => dashboardsApi.sharePreview(id, userId!),
    enabled: !!userId,
    retry: false,
  })
}

export function usePickableVisualizations(enabled: boolean) {
  return useQuery({
    queryKey: ['pickable-visualizations'],
    queryFn: dashboardsApi.pickableVisualizations,
    enabled,
    // Charts and queries come and go; always show the current list when the picker opens.
    staleTime: 0,
  })
}

export function useSetDashboardPublished(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (published: boolean) => dashboardsApi.setPublished(id, published).then(() => published),
    onSuccess: (published) => {
      queryClient.invalidateQueries({ queryKey: dashboardKey(id) })
      queryClient.invalidateQueries({ queryKey: dashboardsKey })
      toast.success(published ? 'Published to everyone in the app.' : 'Unpublished.')
    },
    onError: (error) => toast.error(message(error, 'Could not change whether this dashboard is published.')),
  })
}
