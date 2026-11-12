import { ExternalLink, LogOut, ShieldCheck } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { AppSwitcher } from '@/features/hub/AppSwitcher'
import { isHub } from '@/features/hub/permissions'
import { useLogout } from '@/hooks/useHub'
import type { MeDto } from '@/lib/api'

/** Top-bar strip for hub sessions. Embedded mode draws nothing. */
export function HubBanner({ me }: { me: MeDto | undefined }) {
  const logout = useLogout()
  if (!me || !isHub(me)) return null
  const appName = me.viewingApp?.name ?? me.app?.name ?? null

  return (
    <div className="mr-auto flex min-w-0 items-center gap-3">
      {me.isSuperAdmin ? (
        <>
          <span
            className="flex min-w-0 items-center gap-1.5 rounded-sm border border-primary bg-primary/15 px-2 py-1 text-xs font-semibold"
            role="status"
          >
            <ShieldCheck className="size-3.5 shrink-0" aria-hidden />
            <span className="min-w-0 truncate">{appName ? `Super admin · viewing ${appName}` : 'Super admin · no app chosen'}</span>
          </span>
          <AppSwitcher currentAppId={me.viewingApp?.id ?? null} />
        </>
      ) : (
        appName && <span className="min-w-0 truncate text-xs font-semibold text-muted-foreground">{appName}</span>
      )}
      {me.returnUrl && !me.isSuperAdmin && (
        <a
          href={me.returnUrl}
          className="flex shrink-0 items-center gap-1 text-xs text-muted-foreground underline-offset-2 hover:text-foreground hover:underline"
        >
          <ExternalLink className="size-3" aria-hidden />
          Back to {appName ?? 'app'}
        </a>
      )}
      <Button
        variant="ghost"
        size="sm"
        className="ml-auto h-8 shrink-0 text-xs"
        disabled={logout.isPending}
        onClick={() => logout.mutate()}
      >
        <LogOut className="size-3.5" />
        Sign out
      </Button>
    </div>
  )
}
