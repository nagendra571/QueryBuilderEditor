import { NavLink } from 'react-router-dom'
import { cn } from '@/lib/utils'

const TABS = [
  { to: '/hub/apps', label: 'Apps' },
  { to: '/hub/audit', label: 'Audit log' },
]

/** The console's own sections; the sidebar has one Console entry, so these say where you are inside it. */
export function ConsoleTabs() {
  return (
    <nav aria-label="Console" className="flex gap-1 border-b border-border">
      {TABS.map((tab) => (
        <NavLink
          key={tab.to}
          to={tab.to}
          className={({ isActive }) =>
            cn(
              '-mb-px border-b-2 px-3 py-1.5 text-sm font-medium transition-colors',
              isActive ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground hover:text-foreground',
            )
          }
        >
          {tab.label}
        </NavLink>
      ))}
    </nav>
  )
}
