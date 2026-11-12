import { useMemo } from 'react'
import { CartesianGrid, Scatter, ScatterChart, Tooltip, XAxis, YAxis, ZAxis } from 'recharts'
import { ChartContainer, type ChartConfig } from '@/components/ui/chart'
import { formatScatterX, scatterHasTime, type ScatterModel } from '@/features/visualizations/chart-data'
import { paletteColor } from '@/features/visualizations/chart-types'
import { formatAxis, formatValue } from '@/features/visualizations/format'
import type { ValueFormat } from '@/types'

const MARKER_SHAPES = ['circle', 'square', 'triangle', 'diamond'] as const
type MarkerShape = (typeof MARKER_SHAPES)[number]
const MAX_ANIMATED_POINTS = 500

interface Datum {
  x: number
  y: number
  z: number
  size: number | null
  label: string | null
  group: string
}

function ShapeGlyph({ shape, color }: { shape: MarkerShape; color: string }) {
  const props = { fill: color, stroke: 'var(--mark-outline)', strokeWidth: 1 }
  return (
    <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true" className="shrink-0">
      {shape === 'circle' && <circle cx="6" cy="6" r="5" {...props} />}
      {shape === 'square' && <rect x="1" y="1" width="10" height="10" {...props} />}
      {shape === 'triangle' && <polygon points="6,1 11,11 1,11" {...props} />}
      {shape === 'diamond' && <polygon points="6,0.5 11.5,6 6,11.5 0.5,6" {...props} />}
    </svg>
  )
}

interface TooltipProps {
  active?: boolean
  payload?: { payload?: Datum }[]
  model: ScatterModel
  withTime: boolean
  format?: ValueFormat | null
  xName: string
  yName: string
  sizeName?: string | null
  seriesName?: string | null
}

function ScatterTooltip({ active, payload, model, withTime, format, xName, yName, sizeName, seriesName }: TooltipProps) {
  const datum = payload?.[0]?.payload
  if (!active || !datum) return null
  return (
    <div className="grid min-w-32 gap-1 rounded-md border border-border bg-popover px-2.5 py-1.5 text-xs text-popover-foreground shadow-md">
      {datum.label !== null && <span className="font-medium">{datum.label}</span>}
      {seriesName && model.groups.length > 1 && (
        <span className="flex justify-between gap-4">
          <span className="text-muted-foreground">{seriesName}</span>
          <span>{datum.group}</span>
        </span>
      )}
      <span className="flex justify-between gap-4">
        <span className="text-muted-foreground">{xName}</span>
        <span className="font-numeric tabular-nums">{formatScatterX(datum.x, model.xIsDate, withTime)}</span>
      </span>
      <span className="flex justify-between gap-4">
        <span className="text-muted-foreground">{yName}</span>
        <span className="font-numeric tabular-nums">{formatValue(datum.y, format)}</span>
      </span>
      {sizeName && datum.size !== null && (
        <span className="flex justify-between gap-4">
          <span className="text-muted-foreground">{sizeName}</span>
          <span className="font-numeric tabular-nums">{formatValue(datum.size)}</span>
        </span>
      )}
    </div>
  )
}

/** One Scatter per group, each with its own marker shape so groups never differ by colour alone. */
export function ScatterView({
  model,
  animate,
  heightClass,
  format,
  xName,
  yName,
  sizeName,
  seriesName,
  fill,
}: {
  model: ScatterModel
  animate: boolean
  heightClass: string
  format?: ValueFormat | null
  xName: string
  yName: string
  sizeName?: string | null
  seriesName?: string | null
  fill: boolean
}) {
  // Null-size points draw at the smallest bubble; their z must stay inside the domain built from real sizes.
  const sizes = model.groups.flatMap((g) => g.points.flatMap((p) => (p.size === null ? [] : [p.size])))
  const minSize = sizes.length ? Math.min(...sizes) : 0
  const maxSize = sizes.length ? Math.max(...sizes) : 0
  const data = useMemo(
    () =>
      model.groups.map((g) =>
        g.points.map<Datum>((p) => ({ x: p.x, y: p.y, z: p.size ?? minSize, size: p.size, label: p.label, group: g.name })),
      ),
    [model, minSize],
  )
  const total = model.groups.reduce((n, g) => n + g.points.length, 0)
  const hasSize = sizes.length > 0
  const withTime = scatterHasTime(model)
  const showLegend = model.groups.length > 1
  const tick = { fill: 'var(--muted-foreground)' }
  const config: ChartConfig = {}

  return (
    <div className={`flex min-w-0 flex-col gap-2 ${fill ? 'h-full' : ''}`}>
      <ChartContainer
        config={config}
        className={`aspect-auto w-full font-numeric text-[13px] ${fill ? 'min-h-[96px] flex-1' : heightClass}`}
      >
        <ScatterChart accessibilityLayer margin={{ top: 8, right: 12, bottom: 4, left: 0 }}>
          <CartesianGrid />
          <XAxis
            type="number"
            dataKey="x"
            name={xName}
            domain={['auto', 'auto']}
            tickLine={false}
            axisLine={false}
            tickMargin={8}
            minTickGap={16}
            tick={tick}
            tickFormatter={(v: number) => formatScatterX(v, model.xIsDate, withTime)}
          />
          <YAxis
            type="number"
            dataKey="y"
            name={yName}
            tickLine={false}
            axisLine={false}
            width={52}
            tick={tick}
            tickFormatter={(v: number) => formatAxis(v, format)}
          />
          {hasSize && <ZAxis type="number" dataKey="z" domain={[minSize, maxSize > minSize ? maxSize : minSize + 1]} range={[40, 400]} name={sizeName ?? 'Size'} />}
          <Tooltip
            cursor={{ strokeDasharray: '3 3' }}
            content={
              <ScatterTooltip
                model={model}
                withTime={withTime}
                format={format}
                xName={xName}
                yName={yName}
                sizeName={hasSize ? sizeName : null}
                seriesName={seriesName}
              />
            }
          />
          {data.map((points, i) => (
            <Scatter
              key={model.groups[i].name || `group-${i}`}
              name={model.groups[i].name}
              data={points}
              fill={paletteColor(i)}
              fillOpacity={hasSize ? 0.7 : 1}
              stroke="var(--mark-outline)"
              shape={MARKER_SHAPES[i % MARKER_SHAPES.length]}
              isAnimationActive={animate && total <= MAX_ANIMATED_POINTS}
            />
          ))}
        </ScatterChart>
      </ChartContainer>
      {showLegend && (
        <ul className="flex shrink-0 flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground" aria-label="Legend">
          {model.groups.map((g, i) => (
            <li key={i} className="flex min-w-0 items-center gap-1.5">
              <ShapeGlyph shape={MARKER_SHAPES[i % MARKER_SHAPES.length]} color={paletteColor(i)} />
              <span className="min-w-0 truncate">{g.name}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
