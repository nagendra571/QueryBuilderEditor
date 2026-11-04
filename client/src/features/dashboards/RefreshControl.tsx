import { RefreshCw } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

// Minutes. The smallest is 1 so a dashboard can never hammer the business database.
const INTERVALS = ['off', '1', '5', '15', '30'] as const

export function RefreshControl({
  onRefresh,
  paused,
  lastRefreshed,
}: {
  onRefresh: () => void
  /** True while editing: a refresh mid-edit would only repaint tiles the user is rearranging. */
  paused: boolean
  lastRefreshed: Date | null
}) {
  const [interval, setIntervalValue] = useState<(typeof INTERVALS)[number]>('off')
  const onRefreshRef = useRef(onRefresh)
  useEffect(() => {
    onRefreshRef.current = onRefresh
  })

  useEffect(() => {
    if (interval === 'off' || paused) return
    const ms = Number(interval) * 60_000
    const id = window.setInterval(() => {
      if (!document.hidden) onRefreshRef.current()
    }, ms)
    return () => window.clearInterval(id)
  }, [interval, paused])

  return (
    <div className="flex items-center gap-2">
      <Button variant="outline" size="sm" className="h-8 text-xs" onClick={onRefresh}>
        <RefreshCw className="size-3.5" />
        Refresh all
      </Button>
      <Select value={interval} onValueChange={(v) => setIntervalValue(v as (typeof INTERVALS)[number])}>
        <SelectTrigger size="sm" className="h-8 w-36 text-xs" aria-label="Auto-refresh">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {INTERVALS.map((value) => (
            <SelectItem key={value} value={value} className="text-xs">
              {value === 'off' ? 'Auto-refresh: off' : `Every ${value} min`}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      {/* Polite, and never moves focus, so a background refresh does not interrupt anyone. */}
      <span className="text-xs text-muted-foreground" role="status" aria-live="polite">
        {lastRefreshed ? `Refreshed ${lastRefreshed.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}` : ''}
      </span>
    </div>
  )
}
