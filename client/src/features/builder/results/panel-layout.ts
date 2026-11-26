export const PANEL_DEFAULT = 340
export const PANEL_MIN = 280
export const PANEL_MAX = 560
export const PANEL_STEP = 16
export const OVERLAY_BELOW = 900

export type PanelTab = 'row' | 'column'
export interface PanelState {
  open: boolean
  tab: PanelTab
  width: number
}

export const initialPanel: PanelState = { open: false, tab: 'row', width: PANEL_DEFAULT }

// PANEL_MIN wins in a tiny area: the panel stays usable rather than shrinking with the grid.
export function clampPanelWidth(width: number, areaWidth: number): number {
  return Math.max(PANEL_MIN, Math.min(width, PANEL_MAX, Math.floor(areaWidth / 2)))
}

export function panelMode(areaWidth: number): 'dock' | 'overlay' {
  return areaWidth < OVERLAY_BELOW ? 'overlay' : 'dock'
}
