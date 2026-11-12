import type { MeDto } from '@/lib/api'

export type HubRole = 'Admin' | 'Author' | 'Viewer'

export interface NavItemSpec {
  id: 'queries' | 'dashboards' | 'admin' | 'console'
  to: string
  label: string
}

const QUERIES: NavItemSpec = { id: 'queries', to: '/', label: 'Queries' }
const DASHBOARDS: NavItemSpec = { id: 'dashboards', to: '/dashboards', label: 'Dashboards' }
const ADMIN: NavItemSpec = { id: 'admin', to: '/admin', label: 'Admin' }
const CONSOLE: NavItemSpec = { id: 'console', to: '/hub/apps', label: 'Console' }

/** The server adds `permissions` only in hub mode, so its presence is the one hub test. */
export function isHub(me: Pick<MeDto, 'permissions'> | undefined | null): boolean {
  return !!me?.permissions
}

/** A super admin who has not picked an app yet: every app API answers 401 until they do. */
export function needsAppChoice(me: MeDto | undefined | null): boolean {
  return !!me && isHub(me) && !!me.isSuperAdmin && !me.viewingApp
}

/**
 * Embedded mode keeps today's shell: Queries and Dashboards always, Admin once the admin probe succeeds.
 * `adminProbe` is only consulted there; in hub mode the server's `canAdmin` decides.
 */
export function navItemsFor(me: MeDto | undefined | null, adminProbe = false): NavItemSpec[] {
  if (!isHub(me)) return adminProbe ? [QUERIES, DASHBOARDS, ADMIN] : [QUERIES, DASHBOARDS]
  const p = me!.permissions!
  if (needsAppChoice(me)) return []
  const items: NavItemSpec[] = []
  if (p.canBuildQueries) items.push(QUERIES)
  items.push(DASHBOARDS)
  if (p.canAdmin) items.push(ADMIN)
  return items
}

/**
 * The sidebar: the per-app items, then the super admin console. The console is about the hub, not an app, so it stays
 * even before an app is chosen and never shows to anyone else or in embedded mode.
 */
export function shellNavFor(me: MeDto | undefined | null, adminProbe = false): NavItemSpec[] {
  const items = navItemsFor(me, adminProbe)
  return isHub(me) && me!.isSuperAdmin ? [...items, CONSOLE] : items
}

function section(route: string): string {
  const first = route.split(/[?#]/)[0].split('/').filter(Boolean)[0]
  return first ?? ''
}

/** Whether a route is open to this person. Unknown routes are not gated here (the router shows nothing for them). */
export function canSee(route: string, me: MeDto | undefined | null): boolean {
  if (!isHub(me)) return section(route) !== 'hub'
  const p = me!.permissions!
  switch (section(route)) {
    case 'hub':
    case 'choose-app':
      return !!me!.isSuperAdmin
    case 'admin':
      return p.canAdmin && !needsAppChoice(me)
    case '':
    case 'queries':
      return p.canBuildQueries && !needsAppChoice(me)
    case 'dashboards':
      return !needsAppChoice(me)
    default:
      return true
  }
}

/** Where someone lands when they open `/`. */
export function homeRouteFor(me: MeDto | undefined | null): string {
  if (needsAppChoice(me)) return '/choose-app'
  return isHub(me) && !me!.permissions!.canBuildQueries ? '/dashboards' : '/'
}

/** Why a data source's controls are off, or null when they are on. */
export function lockReason(source: { locked?: boolean; canEdit?: boolean }): string | null {
  if (source.locked && source.canEdit === false) return 'Locked by a super admin'
  if (source.canEdit === false) return 'You can view this data source but not change it'
  return null
}

export function connectionReason(source: { canManageConnection?: boolean }): string | null {
  return source.canManageConnection === false ? 'Connections are managed by a super admin for this app.' : null
}

export function dashboardPublishLabel(published: boolean): string {
  return published ? 'Unpublish' : 'Publish'
}
