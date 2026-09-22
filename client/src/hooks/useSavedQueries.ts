import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { savedQueriesApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import type { ShareQueryRequest } from '@/types'

export const savedQueriesKey = ['saved-queries'] as const
const sharesKey = (queryId: string) => ['query-shares', queryId] as const

export function useSavedQueries() {
  return useQuery({ queryKey: savedQueriesKey, queryFn: savedQueriesApi.list })
}

export function useToggleFavorite() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => savedQueriesApi.toggleFavorite(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: savedQueriesKey }),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not update favorite.'),
  })
}

export function useToggleDisabled() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => savedQueriesApi.toggleDisabled(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: savedQueriesKey }),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not update the query.'),
  })
}

export function useDeleteSavedQuery() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => savedQueriesApi.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: savedQueriesKey })
      toast.success('Query deleted.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not delete query.'),
  })
}

export function useQueryShares(queryId: string | undefined) {
  return useQuery({
    queryKey: sharesKey(queryId ?? ''),
    queryFn: () => savedQueriesApi.getShares(queryId!),
    enabled: !!queryId,
  })
}

export function useShareQuery(queryId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: ShareQueryRequest) => savedQueriesApi.share(queryId, request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sharesKey(queryId) })
      toast.success('Access granted.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not share this query.'),
  })
}

export function useRemoveShare(queryId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (shareId: string) => savedQueriesApi.removeShare(queryId, shareId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sharesKey(queryId) })
      toast.success('Access removed.')
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not remove access.'),
  })
}
