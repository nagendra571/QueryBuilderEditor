import { ChevronRight } from 'lucide-react'
import { Link } from 'react-router-dom'

export interface TrailCrumb {
  label: string
  /** A link (the drill page) or an action (the in-place breakdown). The last crumb is where you are, so it has neither. */
  to?: string
  onSelect?: () => void
}

const LINK = 'min-w-0 max-w-64 truncate rounded-sm underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-ring'

export function DrillTrail({ crumbs, label = 'Drill trail' }: { crumbs: TrailCrumb[]; label?: string }) {
  return (
    <nav aria-label={label} className="min-w-0">
      <ol className="flex flex-wrap items-center gap-x-1 gap-y-0.5 text-sm">
        {crumbs.map((crumb, i) => {
          const last = i === crumbs.length - 1
          return (
            <li key={`${i}:${crumb.label}`} className="flex min-w-0 items-center gap-1">
              {i > 0 && <ChevronRight className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />}
              {last ? (
                <span aria-current="location" title={crumb.label} className="min-w-0 max-w-64 truncate font-semibold">
                  {crumb.label}
                </span>
              ) : crumb.to ? (
                <Link to={crumb.to} title={crumb.label} className={`${LINK} text-muted-foreground`}>
                  {crumb.label}
                </Link>
              ) : (
                <button type="button" title={crumb.label} className={`${LINK} text-muted-foreground`} onClick={crumb.onSelect}>
                  {crumb.label}
                </button>
              )}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}
