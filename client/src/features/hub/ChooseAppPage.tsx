import { Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useHubApps, useViewApp } from '@/hooks/useHub'

/** Where a super admin lands until they pick an app: every app API answers 401 before that. */
export function ChooseAppPage() {
  const apps = useHubApps(true)
  const view = useViewApp()
  const enabled = (apps.data ?? []).filter((a) => a.isEnabled)

  return (
    <div className="mx-auto flex max-w-xl flex-col gap-(--space-section) p-(--space-section)">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight">Choose an app</h1>
        <p className="text-sm text-muted-foreground">Pick the app you want to look at. You can switch from the top bar at any time.</p>
      </div>
      {apps.isLoading && (
        <p className="flex items-center text-sm text-muted-foreground">
          <Loader2 className="mr-2 size-4 animate-spin" /> Loading apps…
        </p>
      )}
      {apps.isError && <p className="text-sm text-destructive">Could not load the apps.</p>}
      {apps.data && enabled.length === 0 && <p className="text-sm text-muted-foreground">No app is turned on yet.</p>}
      <ul className="flex flex-col gap-2">
        {enabled.map((a) => (
          <li key={a.id}>
            <Button
              variant="outline"
              className="h-auto w-full justify-start px-3 py-2.5 text-left"
              disabled={view.isPending}
              onClick={() => view.mutate(a.id)}
            >
              <span className="flex min-w-0 flex-col">
                <span className="truncate text-sm font-semibold">{a.name}</span>
                <span className="truncate font-numeric text-xs text-muted-foreground">{a.key}</span>
              </span>
            </Button>
          </li>
        ))}
      </ul>
    </div>
  )
}
