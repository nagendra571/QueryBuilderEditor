import { useEffect, useRef } from 'react'
import { rowsForContent } from '@/features/dashboards/layout'

/**
 * Measures a tile's content at its natural height and reports the grid rows that would hold it.
 * The element must not be stretched by its parent while enabled, or it would only ever measure itself.
 */
export function useAutoHeight(enabled: boolean, onRows: (rows: number) => void) {
  const ref = useRef<HTMLDivElement>(null)
  const latest = useRef(onRows)
  useEffect(() => {
    latest.current = onRows
  })

  useEffect(() => {
    const el = ref.current
    if (!enabled || !el) return
    const measure = () => latest.current(rowsForContent(el.offsetHeight))
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    measure()
    return () => observer.disconnect()
  }, [enabled])

  return ref
}
