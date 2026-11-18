export const MAX_TRAIL_STEPS = 8
export const MAX_CROSS_FILTERS = 5

export interface TrailStep {
  column: string
  value: string | null
}

export interface CrossFilterValue {
  sourceTileId: string
  column: string
  value: string | null
}

// TextEncoder rather than btoa on the raw string: btoa throws on anything beyond Latin-1.
function toBase64Url(text: string): string {
  const bytes = new TextEncoder().encode(text)
  let binary = ''
  for (const b of bytes) binary += String.fromCharCode(b)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function fromBase64Url(text: string): string | null {
  if (!/^[A-Za-z0-9_-]*$/.test(text) || text.length % 4 === 1) return null
  try {
    const binary = atob(text.replace(/-/g, '+').replace(/_/g, '/'))
    const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0))
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes)
  } catch {
    return null
  }
}

function decodeList<T>(text: string, max: number, valid: (item: unknown) => item is T): T[] | null {
  const json = fromBase64Url(text)
  if (json === null) return null
  let parsed: unknown
  try {
    parsed = JSON.parse(json)
  } catch {
    return null
  }
  if (!Array.isArray(parsed) || parsed.length > max || !parsed.every(valid)) return null
  return parsed
}

const isValue = (v: unknown) => v === null || typeof v === 'string'

function isStep(item: unknown): item is TrailStep {
  if (typeof item !== 'object' || item === null) return false
  const o = item as Record<string, unknown>
  return Object.keys(o).length === 2 && typeof o.column === 'string' && isValue(o.value)
}

function isFilter(item: unknown): item is CrossFilterValue {
  if (typeof item !== 'object' || item === null) return false
  const o = item as Record<string, unknown>
  return Object.keys(o).length === 3 && typeof o.sourceTileId === 'string' && typeof o.column === 'string' && isValue(o.value)
}

export function encodeTrail(steps: TrailStep[]): string {
  return toBase64Url(JSON.stringify(steps.map((s) => ({ column: s.column, value: s.value }))))
}

export function decodeTrail(text: string): TrailStep[] | null {
  return decodeList(text, MAX_TRAIL_STEPS, isStep)
}

export function encodeFilters(filters: CrossFilterValue[]): string {
  return toBase64Url(
    JSON.stringify(filters.map((f) => ({ sourceTileId: f.sourceTileId, column: f.column, value: f.value }))),
  )
}

export function decodeFilters(text: string): CrossFilterValue[] | null {
  return decodeList(text, MAX_CROSS_FILTERS, isFilter)
}
