import type { Density } from '@/state/density-store'

export const MIN_COL_WIDTH = 60
export const MAX_COL_WIDTH = 480
export const DEFAULT_COL_CAP = 320
export const FIT_SAMPLE_ROWS = 200
// 10px each side, matching the grid's cell padding; header affordances travel in `extra` instead.
export const CELL_PADDING_X = 20

export const HEADER_FONT = '600 12px "Public Sans Variable"'
// Cells render at --text-body, which differs per density (index.css); measuring at the wrong size truncates
// default widths and under-fits "Fit to content".
export const CELL_FONT_PX = { comfortable: 14, compact: 13 } as const
export const cellFont = (density: Density) => `400 ${CELL_FONT_PX[density]}px "Public Sans Variable"`

export type MeasureText = (text: string, kind: 'header' | 'cell') => number

export function fitWidth(
  header: string,
  values: string[],
  measure: MeasureText,
  extra: number,
  cap?: number,
): number {
  let widest = 0
  const count = Math.min(values.length, FIT_SAMPLE_ROWS)
  for (let i = 0; i < count; i++) {
    const w = measure(values[i], 'cell')
    if (w > widest) widest = w
  }
  const wanted = Math.max(measure(header, 'header') + extra, widest + CELL_PADDING_X)
  const upper = cap ?? MAX_COL_WIDTH
  return Math.ceil(Math.min(Math.max(wanted, MIN_COL_WIDTH), upper))
}

export function createCanvasMeasure(font: { header: string; cell: string }): MeasureText {
  let ctx: CanvasRenderingContext2D | null | undefined
  return (text, kind) => {
    // Created on first use so importing this module under Node never touches `document`.
    if (ctx === undefined) {
      ctx = typeof document === 'undefined' ? null : document.createElement('canvas').getContext('2d')
    }
    if (!ctx) return text.length * 8
    ctx.font = kind === 'header' ? font.header : font.cell
    return ctx.measureText(text).width
  }
}
