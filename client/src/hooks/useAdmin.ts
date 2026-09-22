import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { adminApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import type { UpdateCatalogPolicyRequest } from '@/types'

export const adminDataSourcesKey = ['admin-data-sources'] as const
const adminDataSourceDetailKey = (id: string) => ['admin-data-source', id] as const

export function useAdminAccess() {
  return useQuery({
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
      toast.success('Catalog policy updated.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not update the catalog policy.'),
  })
}
