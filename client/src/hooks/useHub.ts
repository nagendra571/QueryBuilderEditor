import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { hubApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'

/** Super admins only; anyone else gets 403, so callers pass `enabled`. */
export function useHubApps(enabled: boolean) {
  return useQuery({ queryKey: ['hub-apps'], queryFn: hubApi.listApps, enabled, retry: false, staleTime: 30_000 })
}

/** Switching app changes what every query would answer, so nothing cached may survive it. */
export function useViewApp() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (appId: string) => hubApi.viewApp(appId),
    onSuccess: async () => {
      queryClient.clear()
      await queryClient.refetchQueries({ queryKey: ['me'] })
    },
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
