import { ChevronsLeft, ChevronsRight, LayoutDashboard, MapPin, Moon, Rows3, Settings2, Squircle, Sun, Table2 } from 'lucide-react'
import { useTheme } from 'next-themes'
import { useSyncExternalStore, type ReactNode } from 'react'
import { NavLink } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { useAdminAccess } from '@/hooks/useAdmin'
import { useMe, zoneLabel } from '@/hooks/useMe'
import { cn } from '@/lib/utils'
import { useDensityStore } from '@/state/density-store'
import { useSidebarStore } from '@/state/sidebar-store'

function NavItem({ to, label, icon, collapsed }: { to: string; label: string; icon: ReactNode; collapsed: boolean }) {
  const link = (
    <NavLink
      to={to}
      end={to === '/'}
      className={({ isActive }) =>
        cn(
          'group flex items-center gap-3 rounded-sm text-sm font-medium transition-colors duration-150',
          collapsed ? 'justify-center p-1.5' : 'px-2 py-1.5',
          isActive
            ? 'bg-sidebar-accent text-sidebar-accent-foreground'
            : 'text-sidebar-foreground/75 hover:bg-sidebar-accent/60 hover:text-sidebar-foreground',
        )
      }
    >
      {({ isActive }) => (
        <>
          {/* Each destination is a legend key; the one you're on is filled in zone yellow. */}
          <span
            className={cn(
              'flex size-7 shrink-0 items-center justify-center rounded-[3px] border transition-colors duration-150',
              isActive
                ? 'border-sidebar-primary bg-sidebar-primary text-sidebar-primary-foreground'
                : 'border-sidebar-foreground/25 group-hover:border-sidebar-foreground/50',
            )}
          >
            {icon}
          </span>
          {!collapsed && <span className="truncate">{label}</span>}
        </>
      )}
    </NavLink>
  )

  if (!collapsed) return link

  return (
    <Tooltip>
      <TooltipTrigger asChild>{link}</TooltipTrigger>
      <TooltipContent side="right">{label}</TooltipContent>
    </Tooltip>
  )
}

/** Who you are and which zone of the data you see — the one fact every screen is filtered by. */
function ZoneChip({ collapsed }: { collapsed: boolean }) {
  const { data: me } = useMe()
  if (!me) return null
  const zone = zoneLabel(me)
  const restricted = !!me.zone && !me.zone.unrestricted
  const chip = (
    <div
      className={cn(
        'flex items-center gap-2.5 rounded-sm border border-sidebar-border',
        collapsed ? 'justify-center p-1.5' : 'px-2 py-2',
      )}
    >
      <span
        aria-hidden
        className={cn(
          'size-4 shrink-0 rounded-[2px] border border-sidebar-primary',
          restricted ? 'bg-sidebar-primary' : 'bg-sidebar-primary ring-2 ring-sidebar-primary/40 ring-offset-1 ring-offset-sidebar',
        )}
      />
      {!collapsed && (
        <div className="flex min-w-0 flex-col leading-tight">
          <span className="truncate text-xs font-semibold text-sidebar-foreground">{me.displayName}</span>
          {zone && <span className="truncate font-numeric text-sm font-semibold tracking-wide text-sidebar-primary">ZONE · {zone}</span>}
        </div>
      )}
    </div>
  )
  if (!collapsed) return chip
  return (
    <Tooltip>
      <TooltipTrigger asChild>{chip}</TooltipTrigger>
      <TooltipContent side="right">
        {me.displayName}
        {zone ? ` — zone: ${zone}` : ''}
      </TooltipContent>
    </Tooltip>
  )
}

function DensityToggle() {
  const { density, toggle } = useDensityStore()
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button variant="ghost" size="icon" onClick={toggle} aria-label="Toggle density">
          {density === 'comfortable' ? <Rows3 className="size-4" /> : <Squircle className="size-4" />}
        </Button>
      </TooltipTrigger>
      <TooltipContent>{density === 'comfortable' ? 'Switch to compact view' : 'Switch to comfortable view'}</TooltipContent>
    </Tooltip>
  )
}

function ThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme()
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          onClick={() => setTheme(resolvedTheme === 'dark' ? 'light' : 'dark')}
          aria-label="Toggle theme"
        >
          {resolvedTheme === 'dark' ? <Sun className="size-4" /> : <Moon className="size-4" />}
        </Button>
      </TooltipTrigger>
      <TooltipContent>Toggle theme</TooltipContent>
    </Tooltip>
  )
}

const NARROW = '(max-width: 767px)'

function useIsNarrow() {
  return useSyncExternalStore(
    (onChange) => {
      const mq = window.matchMedia(NARROW)
      mq.addEventListener('change', onChange)
      return () => mq.removeEventListener('change', onChange)
    },
    () => window.matchMedia(NARROW).matches,
    () => false,
  )
}

export function AppShell({ children }: { children: ReactNode }) {
  const { collapsed: userCollapsed, toggle } = useSidebarStore()
  // Phones always get the icon rail; the user's own preference applies from tablet width up.
  const narrow = useIsNarrow()
  const collapsed = userCollapsed || narrow
  const adminAccess = useAdminAccess()

  return (
    <div className="flex h-dvh w-full overflow-hidden bg-background text-foreground">
      <aside
        className={cn(
          'flex shrink-0 flex-col bg-sidebar text-sidebar-foreground transition-[width] duration-200 ease-out',
          collapsed ? 'w-14' : 'w-60',
        )}
      >
        <div className={cn('flex h-14 items-center gap-2.5 border-b border-sidebar-border', collapsed ? 'justify-center px-0' : 'px-4')}>
          <MapPin className="size-5 shrink-0 text-sidebar-primary" aria-hidden />
          {!collapsed && <span className="truncate text-[15px] font-bold tracking-tight">QueryBuilder</span>}
        </div>

        <nav aria-label="Main" className={cn('flex flex-1 flex-col gap-1', collapsed ? 'p-2' : 'p-3')}>
          <NavItem to="/" label="Queries" icon={<Table2 className="size-4" />} collapsed={collapsed} />
          <NavItem to="/dashboards" label="Dashboards" icon={<LayoutDashboard className="size-4" />} collapsed={collapsed} />
          {adminAccess.isSuccess && (
            <NavItem to="/admin" label="Admin" icon={<Settings2 className="size-4" />} collapsed={collapsed} />
          )}
        </nav>

        <div className={cn('flex flex-col gap-2 border-t border-sidebar-border', collapsed ? 'p-2' : 'p-3')}>
          <ZoneChip collapsed={collapsed} />
          {!narrow && (
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                onClick={toggle}
                aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
                className={cn(
                  'text-sidebar-foreground/70 hover:bg-sidebar-accent hover:text-sidebar-foreground',
                  collapsed ? 'mx-auto' : 'ml-auto',
                )}
              >
                {collapsed ? <ChevronsRight className="size-4" /> : <ChevronsLeft className="size-4" />}
              </Button>
            </TooltipTrigger>
            <TooltipContent side="right">{collapsed ? 'Expand sidebar' : 'Collapse sidebar'}</TooltipContent>
          </Tooltip>
          )}
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-12 shrink-0 items-center justify-end gap-1 border-b border-border px-4">
          <DensityToggle />
          <ThemeToggle />
        </header>
        <main className="min-h-0 flex-1 overflow-auto">{children}</main>
      </div>
    </div>
  )
}
