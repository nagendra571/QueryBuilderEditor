import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { adminApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import type {
  CreateDataSourceRequest,
  TestConnectionRequest,
  UpdateCatalogPolicyRequest,
  UpdateDataScopeRequest,
  UpdateDataSourceRequest,
  UpdateScopeKeysRequest,
  UpdateRecordLimitRequest,
} from '@/types'

export const adminDataSourcesKey = ['admin-data-sources'] as const
const adminDataSourceDetailKey = (id: string) => ['admin-data-source', id] as const
const adminDataScopeKey = (id: string) => ['admin-data-scope', id] as const

export function useAdminAccess(enabled = true) {
  return useQuery({
    enabled,
    queryKey: ['admin-access'],
    // queryFn must resolve to a defined value — TanStack Query v5 throws
    // "<queryHash> data is undefined" and forces the query into an error
    // state (never `isSuccess`) if it resolves to `undefined`, which
    // `adminApi.access()` does (it's a Promise<void> 204 probe).
    queryFn: async () => {
      await adminApi.access()
      return true
    },
    retry: false,
  })
}

export function useAdminDataSources() {
  return useQuery({ queryKey: adminDataSourcesKey, queryFn: adminApi.listDataSources })
}

export function useAdminDataSourceDetail(id: string | undefined) {
  return useQuery({
    queryKey: adminDataSourceDetailKey(id ?? ''),
    queryFn: () => adminApi.getDataSource(id!),
    enabled: !!id,
  })
}

export function useUpdateCatalogPolicy(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateCatalogPolicyRequest) => adminApi.updateCatalogPolicy(id, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminDataSourceDetailKey(id) })
      // The business-facing catalog is cached client-side for 5 minutes, and `catalogScope`
      // also rides on the data-sources list DTO — without these the SPA keeps serving the
      // pre-save catalog on in-app navigation, undoing the server-side cache eviction.
      queryClient.invalidateQueries({ queryKey: ['data-source-catalog', id] })
      queryClient.invalidateQueries({ queryKey: ['data-sources'] })
      // The data-scope section lists exactly the catalog's objects, so it changes too.
      queryClient.invalidateQueries({ queryKey: adminDataScopeKey(id) })
      toast.success('Catalog policy updated.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not update the catalog policy.'),
  })
}

export function useAdminDataScope(id: string | undefined) {
  return useQuery({
    queryKey: adminDataScopeKey(id ?? ''),
    queryFn: () => adminApi.getDataScope(id!),
    enabled: !!id,
  })
}

export function useUpdateDataScope(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateDataScopeRequest) => adminApi.updateDataScope(id, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminDataScopeKey(id) })
      // Which objects a scoped user sees comes from the business-facing catalog query —
      // without this, in-SPA navigation keeps showing the pre-save catalog for up to staleTime.
      queryClient.invalidateQueries({ queryKey: ['data-source-catalog', id] })
      queryClient.invalidateQueries({ queryKey: ['data-sources'] })
      toast.success('Data scope updated.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not update the data scope.'),
  })
}

// Errors are shown inline by the section, so there is no toast here.
export function useUpdateScopeKeys(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateScopeKeysRequest) => adminApi.updateScopeKeys(id, request),
    onSuccess: (data) => {
      queryClient.setQueryData(adminDataScopeKey(id), data)
      // Keys decide which views a scoped user sees, so the business-facing catalog changes with them;
      // its client-side staleTime would otherwise keep serving the old one on in-SPA navigation.
      queryClient.invalidateQueries({ queryKey: adminDataScopeKey(id) })
      queryClient.invalidateQueries({ queryKey: ['data-source-catalog', id] })
      queryClient.invalidateQueries({ queryKey: ['data-sources'] })
    },
  })
}

export function useUpdateRecordLimit(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateRecordLimitRequest) => adminApi.updateRecordLimit(id, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminDataSourceDetailKey(id) })
      toast.success('Record limit updated.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not update the record limit.'),
  })
}

export function useDataSourceManagement() {
  return useQuery({ queryKey: ['admin-data-source-management'], queryFn: adminApi.dataSourceManagement, retry: false })
}

// Everything that reads a data source's name, status or connection, so no screen keeps serving the old one
// (the business-facing picker and catalog are cached client-side for minutes and would otherwise outlive a change).
function invalidateDataSource(queryClient: ReturnType<typeof useQueryClient>, id: string) {
  queryClient.invalidateQueries({ queryKey: adminDataSourcesKey })
  queryClient.invalidateQueries({ queryKey: adminDataSourceDetailKey(id) })
  queryClient.invalidateQueries({ queryKey: adminDataScopeKey(id) })
  queryClient.invalidateQueries({ queryKey: ['data-source-catalog', id] })
  queryClient.invalidateQueries({ queryKey: ['data-sources'] })
  // Turning a source off or changing its connection changes what these show or can still load.
  queryClient.invalidateQueries({ queryKey: ['app-users', id] })
  queryClient.invalidateQueries({ queryKey: ['visualization-data'] })
  queryClient.invalidateQueries({ queryKey: ['pickable-visualizations'] })
  queryClient.invalidateQueries({ queryKey: ['parameter-options'] })
}

export const encryptionKeyStatusKey = ['admin-encryption-key'] as const

export function useEncryptionKeyStatus() {
  return useQuery({ queryKey: encryptionKeyStatusKey, queryFn: adminApi.encryptionKeyStatus, retry: false })
}

export function useReencrypt() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => adminApi.reencrypt(),
    onSuccess: () => {
      // The counts and the list of sources on an old key change; so can what a source's own page says about its
      // connection being readable.
      queryClient.invalidateQueries({ queryKey: encryptionKeyStatusKey })
      queryClient.invalidateQueries({ queryKey: adminDataSourcesKey })
      queryClient.invalidateQueries({ queryKey: ['admin-data-source'] })
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not re-encrypt the data sources.'),
  })
}

export function useCreateDataSource() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateDataSourceRequest) => adminApi.createDataSource(request),
    // The request holds the password; don't let the mutation cache keep it after the call settles.
    gcTime: 0,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminDataSourcesKey })
      queryClient.invalidateQueries({ queryKey: ['data-sources'] })
    },
  })
}

export function useUpdateDataSource(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: UpdateDataSourceRequest) => adminApi.updateDataSource(id, request),
    gcTime: 0,
    onSuccess: () => {
      invalidateDataSource(queryClient, id)
      toast.success('Data source saved.')
    },
  })
}

export function useSetDataSourceActive(id: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (isActive: boolean) => adminApi.setDataSourceActive(id, isActive),
    onSuccess: (_data, isActive) => {
      invalidateDataSource(queryClient, id)
      toast.success(isActive ? 'Data source turned on.' : 'Data source turned off.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not change the data source.'),
  })
}

export function useTestConnection() {
  return useMutation({ mutationFn: (request: TestConnectionRequest) => adminApi.testConnection(request), gcTime: 0 })
}

export function useTestStoredConnection(id: string) {
  return useMutation({ mutationFn: () => adminApi.testStoredConnection(id) })
}
