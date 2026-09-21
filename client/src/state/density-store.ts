import { create } from 'zustand'
import { persist } from 'zustand/middleware'

export type Density = 'comfortable' | 'compact'

interface DensityState {
  density: Density
  setDensity: (density: Density) => void
  toggle: () => void
}

export const useDensityStore = create<DensityState>()(
  persist(
    (set, get) => ({
      density: 'comfortable',
      setDensity: (density) => {
        set({ density })
        document.documentElement.setAttribute('data-density', density)
      },
      toggle: () => {
        const next: Density = get().density === 'comfortable' ? 'compact' : 'comfortable'
        get().setDensity(next)
      },
    }),
    {
      name: 'querybuilder-density',
      onRehydrateStorage: () => (state) => {
        if (state) document.documentElement.setAttribute('data-density', state.density)
      },
    },
  ),
)
