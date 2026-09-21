import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { savedQueriesApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'

export const savedQueriesKey = ['saved-queries'] as const

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
