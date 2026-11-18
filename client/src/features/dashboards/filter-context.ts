import { createContext } from 'react'
import type { CrossFilterValue } from '@/features/drill/drill-url'

export interface DashboardFilters {
  /** In force for tiles: empty while the switch is off. */
  filters: CrossFilterValue[]
  enabled: boolean
  add(filter: CrossFilterValue): void
}

export const DashboardFiltersContext = createContext<DashboardFilters>({ filters: [], enabled: false, add: () => {} })
