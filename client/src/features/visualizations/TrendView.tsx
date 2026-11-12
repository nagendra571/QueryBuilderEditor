import { Line, LineChart, YAxis } from 'recharts'
import { ChartContainer, type ChartConfig } from '@/components/ui/chart'
import { formatValue } from '@/features/visualizations/format'
import { fitFontSize, trendChangeText } from '@/features/visualizations/single-number'
import type { TrendModel } from '@/features/visualizations/story-models'
import type { ValueFormat } from '@/types'

const CONFIG: ChartConfig = { y: { label: 'Value', color: 'var(--chart-1)' } }

export function TrendView({ model, animate, format }: { model: TrendModel; animate: boolean; format?: ValueFormat | null }) {
  const text = formatValue(model.latest, format)
  const change = trendChangeText(model, format)
  return (
    <div className="@container flex h-full w-full min-w-0 flex-col items-center justify-center gap-1 py-2">
      <span
        className="font-numeric block max-w-full truncate leading-none font-semibold"
        style={{ fontSize: fitFontSize(text) }}
      >
        {text}
      </span>
      <span className="map-label text-muted-foreground">{model.latestLabel}</span>
      {change && <span className="text-sm text-foreground">{change}</span>}
      <div aria-hidden="true" className="mt-1 h-16 min-h-10 w-full flex-1">
        <ChartContainer config={CONFIG} className="aspect-auto h-full w-full">
          <LineChart data={model.points} margin={{ top: 4, right: 4, bottom: 4, left: 4 }}>
            <YAxis hide domain={['auto', 'auto']} />
            <Line
              dataKey="y"
              type="linear"
              stroke="var(--chart-1)"
              strokeWidth={2}
              dot={false}
              connectNulls={false}
              isAnimationActive={animate}
            />
          </LineChart>
        </ChartContainer>
      </div>
    </div>
  )
}
