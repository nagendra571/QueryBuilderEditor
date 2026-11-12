import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useHubApps, useViewApp } from '@/hooks/useHub'

/** Super admins only. Lists enabled apps; picking one drops every cached answer and lands on that app's home. */
export function AppSwitcher({ currentAppId }: { currentAppId: string | null }) {
  const apps = useHubApps(true)
  const view = useViewApp()
  const enabled = (apps.data ?? []).filter((a) => a.isEnabled)

  return (
    <Select
      value={currentAppId ?? ''}
      disabled={view.isPending || enabled.length === 0}
      onValueChange={(appId) => {
        // Radix can report '' while its items are not mounted; that is not a choice.
        if (!appId || appId === currentAppId) return
        view.mutate(appId)
      }}
    >
      <SelectTrigger size="sm" className="h-8 w-48 text-xs" aria-label="Switch app">
        <SelectValue placeholder={apps.isError ? 'Apps unavailable' : 'Choose an app'} />
      </SelectTrigger>
      <SelectContent>
        {enabled.map((a) => (
          <SelectItem key={a.id} value={a.id}>
            {a.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
