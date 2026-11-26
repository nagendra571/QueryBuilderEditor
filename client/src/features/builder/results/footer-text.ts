/** "1–40 of 1,200 visible", or with a narrowing search "1–40 of 12 (filtered from 1,200)". Empty with no rows. */
export function footerRange(first: number, last: number, filtered: number, raw: number): string {
  if (filtered === 0 || last === 0) return ''
  const n = (x: number) => x.toLocaleString()
  const range = `${n(first)}–${n(last)} of ${n(filtered)}`
  return filtered < raw ? `${range} (filtered from ${n(raw)})` : `${range} visible`
}
