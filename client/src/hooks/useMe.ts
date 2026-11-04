import { useQuery } from '@tanstack/react-query'
import { meApi, type MeDto } from '@/lib/api'

export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: meApi.get, staleTime: 5 * 60_000, retry: false })
}

/** "ProgramId 10, 30" — the viewer's own zone in plain words, or null when scoping is off. */
export function zoneLabel(me: MeDto | undefined): string | null {
  if (!me?.zone) return null
  if (me.zone.unrestricted) return 'All data'
  const parts = Object.entries(me.zone.values).map(([key, values]) => `${key} ${values.join(', ')}`)
  return parts.length ? parts.join(' · ') : 'No zone assigned'
}
