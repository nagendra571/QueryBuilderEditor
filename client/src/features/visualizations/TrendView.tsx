import { useRef } from 'react'
import type { ActiveDotProps } from 'recharts/types/util/types'
import { Line, LineChart, Tooltip, XAxis, YAxis } from 'recharts'
import { ChartContainer, type ChartConfig } from '@/components/ui/chart'
import { formatValue } from '@/features/visualizations/format'
import { fitFontSize, trendChangeText } from '@/features/visualizations/single-number'
import { fireMark, type MarkDrill } from '@/features/drill/mark-click'
import { PlotGeometryReporter, pointIndexAtClick, type PlotGeometry } from '@/features/visualizations/PlotClick'
import { pickRow, rowsOf } from '@/features/visualizations/mark-sources'
import type { TrendModel } from '@/features/visualizations/story-models'
import type { ValueFormat } from '@/types'

const CONFIG: ChartConfig = { y: { label: 'Value', color: 'var(--chart-1)' } }

export function TrendView({
  model,
  animate,
  format,
  drill,
}: {
  model: TrendModel
  animate: boolean
  format?: ValueFormat | null
  drill?: MarkDrill
}) {
  const geometry = useRef<PlotGeometry | null>(null)
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
          <LineChart
            data={model.points}
            margin={{ top: 4, right: 4, bottom: 4, left: 4 }}
            {...(drill
              ? {
                  style: { cursor: 'pointer' },
                  onClick: (_state: unknown, e: React.MouseEvent) => {
                    const index = pointIndexAtClick(e, geometry.current)
                    const row = index === undefined ? undefined : pickRow(rowsOf(model.points[index]), drill.keyColumns)
                    fireMark(drill, row ? { row } : null, e)
                  },
                }
              : {})}
          >
            {drill && <PlotGeometryReporter labels={model.points.map((_, i) => i)} into={geometry} />}
            {drill && <XAxis hide />}
            <YAxis hide domain={['auto', 'auto']} />
            {drill && <Tooltip content={() => null} cursor={false} />}
            <Line
              dataKey="y"
              type="linear"
              stroke="var(--chart-1)"
              strokeWidth={2}
              dot={false}
              connectNulls={false}
              isAnimationActive={animate}
              activeDot={
                drill
                  ? (dot: ActiveDotProps) => (
                      <circle
                        cx={dot.cx}
                        cy={dot.cy}
                        r={5}
                        fill="var(--chart-1)"
                        stroke="var(--background)"
                        strokeWidth={2}
                        style={{ cursor: 'pointer' }}
                        onClick={(e) => {
                          e.stopPropagation()
                          const row = pickRow(rowsOf(model.points[dot.index]), drill.keyColumns)
                          fireMark(drill, row ? { row } : null, e)
                        }}
                      />
                    )
                  : undefined
              }
            />
          </LineChart>
        </ChartContainer>
      </div>
    </div>
  )
}
