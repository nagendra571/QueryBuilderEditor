import { ChevronsLeft, ChevronsRight, LayoutGrid, Moon, Rows3, Squircle, Sun } from 'lucide-react'
import { useTheme } from 'next-themes'
import type { ReactNode } from 'react'
import { NavLink } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import { useDensityStore } from '@/state/density-store'
import { useSidebarStore } from '@/state/sidebar-store'

function NavItem({ to, label, icon, collapsed }: { to: string; label: string; icon: ReactNode; collapsed: boolean }) {
  const link = (
    <NavLink
      to={to}
      end
      className={({ isActive }) =>
        cn(
          'flex items-center gap-2.5 rounded-md text-sm font-medium transition-colors',
          collapsed ? 'justify-center px-0 py-2' : 'px-3 py-2',
          isActive
            ? 'bg-sidebar-accent text-sidebar-accent-foreground'
            : 'text-sidebar-foreground/70 hover:bg-sidebar-accent/60 hover:text-sidebar-foreground',
        )
      }
    >
      {icon}
      {!collapsed && label}
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

export function AppShell({ children }: { children: ReactNode }) {
  const { collapsed, toggle } = useSidebarStore()

  return (
    <div className="flex h-dvh w-full overflow-hidden bg-background text-foreground">
      <aside
        className={cn(
          'flex shrink-0 flex-col border-r border-sidebar-border bg-sidebar transition-[width] duration-200 ease-out',
          collapsed ? 'w-14' : 'w-56',
        )}
      >
        <div className={cn('flex h-14 items-center gap-2 border-b border-sidebar-border', collapsed ? 'justify-center px-0' : 'px-4')}>
          <div className="flex size-7 shrink-0 items-center justify-center rounded-md bg-primary text-primary-foreground">
            <LayoutGrid className="size-4" />
          </div>
          {!collapsed && <span className="font-heading truncate text-sm font-semibold tracking-tight">QueryBuilder</span>}
        </div>

        <nav className={cn('flex flex-1 flex-col gap-1', collapsed ? 'p-2' : 'p-3')}>
          <NavItem to="/" label="Queries" icon={<LayoutGrid className="size-4 shrink-0" />} collapsed={collapsed} />
        </nav>

        {!collapsed && (
          <div className="border-t border-sidebar-border p-3 text-xs text-sidebar-foreground/50">
            Dashboards &amp; sharing — coming soon
          </div>
        )}

        <div className={cn('border-t border-sidebar-border p-2', collapsed && 'flex justify-center')}>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                onClick={toggle}
                aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
                className={collapsed ? '' : 'ml-auto flex'}
              >
                {collapsed ? <ChevronsRight className="size-4" /> : <ChevronsLeft className="size-4" />}
              </Button>
            </TooltipTrigger>
            <TooltipContent side="right">{collapsed ? 'Expand sidebar' : 'Collapse sidebar'}</TooltipContent>
          </Tooltip>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 shrink-0 items-center justify-end gap-1 border-b border-border px-4">
          <DensityToggle />
          <ThemeToggle />
        </header>
        <main className="min-h-0 flex-1 overflow-auto">{children}</main>
      </div>
    </div>
  )
}
