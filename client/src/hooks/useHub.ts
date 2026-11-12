import { useInfiniteQuery, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { hubApi, type HubAuditParams, type UpdateHubAppRequest } from '@/lib/api'
import { ApiError } from '@/lib/api-client'

export const hubAppsKey = ['hub-apps'] as const
export const hubAuditKey = ['hub-audit'] as const
export const HUB_AUDIT_PAGE = 50

const message = (error: unknown, fallback: string) => (error instanceof ApiError ? error.message : fallback)

/** Every list that can show an app, its keys or a change to it, so nothing stale survives a registry edit. */
function invalidateRegistry(queryClient: QueryClient) {
  queryClient.invalidateQueries({ queryKey: hubAppsKey })
  queryClient.invalidateQueries({ queryKey: hubAuditKey })
}

/** A data source's lock or app changes what the admin lists, the picker and the catalog show. */
function invalidateDataSources(queryClient: QueryClient, id: string) {
  queryClient.invalidateQueries({ queryKey: ['admin-data-sources'] })
  queryClient.invalidateQueries({ queryKey: ['admin-data-source', id] })
  queryClient.invalidateQueries({ queryKey: ['data-sources'] })
  queryClient.invalidateQueries({ queryKey: ['data-source-catalog'] })
  queryClient.invalidateQueries({ queryKey: hubAuditKey })
}

/** Super admins only; anyone else gets 403, so callers pass `enabled`. */
export function useHubApps(enabled: boolean) {
  return useQuery({ queryKey: hubAppsKey, queryFn: hubApi.listApps, enabled, retry: false, staleTime: 30_000 })
}

/** Switching app changes what every query would answer, so nothing cached may survive it. */
export function useViewApp() {
  return useMutation({
    mutationFn: (appId: string) => hubApi.viewApp(appId),
    // A full reload is the only way to be sure no cached answer, open dialog or store survives the other app.
    onSuccess: () => window.location.assign('/querybuilder/'),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not switch app.'),
  })
}

export function useLogout() {
  return useMutation({
    mutationFn: hubApi.logout,
    onSuccess: ({ redirectUrl }) => window.location.assign(redirectUrl),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not sign out.'),
  })
}

export function useCreateApp() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: { key: string; name: string }) => hubApi.createApp(request),
    onSuccess: () => invalidateRegistry(queryClient),
  })
}

export function useUpdateApp(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateHubAppRequest) => hubApi.updateApp(id, request),
    onSuccess: () => {
      invalidateRegistry(queryClient)
      // Enabling an app, its ceiling and its scope keys change what the data source and scope pages say.
      queryClient.invalidateQueries({ queryKey: ['admin-data-sources'] })
      queryClient.invalidateQueries({ queryKey: ['me'] })
    },
  })
}

export function useAddSigningKey(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: { kid: string; algorithm: string; publicKeyPem: string }) => hubApi.addKey(id, request),
    onSuccess: () => invalidateRegistry(queryClient),
  })
}

export function useRevokeSigningKey(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (kid: string) => hubApi.revokeKey(id, kid),
    onSuccess: () => {
      invalidateRegistry(queryClient)
      toast.success('Key revoked.')
    },
    onError: (error) => toast.error(message(error, 'Could not revoke the key.')),
  })
}

export function useSetReturnUrls(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (urls: string[]) => hubApi.setReturnUrls(id, urls),
    onSuccess: () => invalidateRegistry(queryClient),
  })
}

export function useHubAudit(params: Omit<HubAuditParams, 'before' | 'take'>) {
  return useInfiniteQuery({
    queryKey: [...hubAuditKey, params],
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam }) => hubApi.audit({ ...params, before: pageParam, take: HUB_AUDIT_PAGE }),
    getNextPageParam: (last) => (last.length === HUB_AUDIT_PAGE ? last[last.length - 1].timestampUtc : undefined),
    retry: false,
  })
}

export function useSetDataSourceLock(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (locked: boolean) => hubApi.setDataSourceLock(id, locked),
    onSuccess: () => invalidateDataSources(queryClient, id),
    onError: (error) => toast.error(message(error, 'Could not change the lock.')),
  })
}

export function useMoveDataSource(appId: string, id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (targetAppId: string) => hubApi.moveDataSource(appId, id, targetAppId),
    onSuccess: () => {
      invalidateDataSources(queryClient, id)
      invalidateRegistry(queryClient)
    },
  })
}
