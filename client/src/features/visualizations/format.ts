import type { ValueFormat } from '@/types'

const MISSING = '—'

// Intl throws RangeError on a malformed currency code; a bad saved option must still render a number.
function currencyCode(format: ValueFormat): string | null {
  const code = format.currency?.trim().toUpperCase()
  return code && /^[A-Z]{3}$/.test(code) ? code : null
}

function build(value: number, format: ValueFormat | null | undefined, axis: boolean): string {
  const decimals = format?.decimals ?? undefined
  const pinned = decimals === undefined ? {} : { minimumFractionDigits: decimals, maximumFractionDigits: decimals }

  switch (format?.style) {
    case 'compact':
      return new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: decimals ?? 1 }).format(value)
    case 'currency': {
      const currency = currencyCode(format)
      if (currency) {
        try {
          return new Intl.NumberFormat(undefined, {
            style: 'currency',
            currency,
            ...(axis ? { notation: 'compact', maximumFractionDigits: decimals ?? 1 } : pinned),
          }).format(value)
        } catch {
          // fall through to the plain number
        }
      }
      return build(value, { style: 'number', decimals: format.decimals }, axis)
    }
    case 'percent':
      // The value is already a percentage (42 means 42%); Intl would multiply by 100.
      return new Intl.NumberFormat(undefined, {
        style: 'percent',
        minimumFractionDigits: decimals ?? 0,
        maximumFractionDigits: decimals ?? 2,
      }).format(value / 100)
    default:
      if (axis) return new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(value)
      // Fractions pin to two places, exactly what charts showed before formats existed.
      return new Intl.NumberFormat(undefined, {
        minimumFractionDigits: decimals ?? (Number.isInteger(value) ? 0 : 2),
        maximumFractionDigits: decimals ?? (Number.isInteger(value) ? 0 : 2),
      }).format(value)
  }
}

export function formatValue(value: number | null, format?: ValueFormat | null): string {
  if (value === null) return MISSING
  return build(value, format, false)
}

export function formatAxis(value: number, format?: ValueFormat | null): string {
  return build(value, format, true)
}

/** Plain numbers for an X axis, where compact notation would turn the year 2024 into "2K". */
export function formatPlainNumber(value: number): string {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 2, useGrouping: 'min2' }).format(value)
}

/** Rounded so a floating-point sum like 100.00000000000001 never reaches the axis. */
export function percentTick(value: number): string {
  return `${Number(value.toFixed(2))}%`
}

const MAX_LABEL = 24

export function truncateLabel(value: unknown): string {
  const text = String(value)
  return text.length > MAX_LABEL ? `${text.slice(0, MAX_LABEL - 1)}…` : text
}
