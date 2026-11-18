import { useMemo } from 'react'
import type { ActiveDotProps } from 'recharts/types/util/types'
import { Area, AreaChart, Bar, BarChart, CartesianGrid, ComposedChart, Line, LineChart, XAxis, YAxis } from 'recharts'
import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
  type ChartConfig,
  type TooltipPayloadItem,
} from '@/components/ui/chart'
import { toPercentStack, type CartesianModel } from '@/features/visualizations/chart-data'
import { paletteColor } from '@/features/visualizations/chart-types'
import { formatAxis, formatValue, percentTick, truncateLabel } from '@/features/visualizations/format'
import { fireMark, type MarkDrill } from '@/features/drill/mark-click'
import { pickRow, pointIndexOf, rowAtX, seriesRowsOf } from '@/features/visualizations/mark-sources'
import type { ValueFormat, VisualizationOptions, VisualizationType } from '@/types'

const DASHES = [undefined, '6 3', '2 3', '10 3 2 3']
const PERCENT_FORMAT: ValueFormat = { style: 'percent', decimals: 1 }

export function CartesianView({
  type,
  options,
  model,
  animate,
  heightClass,
  format,
  drill,
}: {
  type: VisualizationType
  options: VisualizationOptions
  model: CartesianModel
  animate: boolean
  heightClass: string
  format?: ValueFormat | null
  drill?: MarkDrill
}) {
  const stackable = type === 'bar' || type === 'horizontalBar' || type === 'area'
  const stacked = stackable && !!options.stacked
  const percent = stacked && !!options.percent

  // The raw value rides along under its own key so the tooltip can show it beside the percent.
  const data = useMemo(() => {
    if (!percent) return model.points
    return toPercentStack(model.points, model.series).map((p, i) => {
      const next: Record<string, string | number | null> = { ...p }
      for (const s of model.series) next[`raw_${s.key}`] = model.points[i][s.key] ?? null
      return next
    })
  }, [percent, model])

  const config: ChartConfig = {}
  model.series.forEach((s, i) => {
    config[s.key] = { label: s.label, color: paletteColor(i) }
  })
  const showLegend = model.series.length > 1

  const formatTooltip = (value: number, item: TooltipPayloadItem) => {
    if (!percent) return formatValue(value, format)
    const raw = item.payload?.[`raw_${String(item.dataKey)}`]
    const shown = formatValue(value, PERCENT_FORMAT)
    return typeof raw === 'number' ? `${shown} (${formatValue(raw, format)})` : shown
  }
  const tooltip = <ChartTooltip content={<ChartTooltipContent valueFormatter={formatTooltip} />} />
  const legend = showLegend && <ChartLegend content={<ChartLegendContent />} />
  const tick = { fill: 'var(--muted-foreground)' }
  const valueTick = (v: number) => (percent ? percentTick(v) : formatAxis(v, format))
  const domain = percent ? ([0, 100] as [number, number]) : undefined
  const stackId = stacked ? 'stack' : undefined

  const category = (
    <XAxis dataKey="x" tickLine={false} axisLine={false} tickMargin={8} minTickGap={16} tick={tick} />
  )
  const values = (
    <YAxis tickLine={false} axisLine={false} width={52} tick={tick} domain={domain} tickFormatter={valueTick} />
  )

  // A mark whose row can't be told apart (a split with two rows under one X) gets no click rather than a wrong one.
  const markAt = (seriesKey: string, index: number) => {
    const row = pickRow(seriesRowsOf(model.points[index], seriesKey), drill?.keyColumns)
    return row ? { row } : null
  }
  const pointer = drill ? { cursor: 'pointer' } : undefined

  const bar = (s: CartesianModel['series'][number]) => (
    <Bar
      key={s.key}
      dataKey={s.key}
      fill={`var(--color-${s.key})`}
      stroke="var(--mark-outline)"
      strokeWidth={1}
      stackId={stackId}
      maxBarSize={72}
      radius={stacked ? 0 : type === 'horizontalBar' ? [0, 1, 1, 0] : [1, 1, 0, 0]}
      isAnimationActive={animate}
      {...(drill
        ? {
            style: pointer,
            onClick: (_d: unknown, index: number, e: React.MouseEvent) => {
              // In a combo the chart-level click would open a second menu for the line at the same X.
              e.stopPropagation()
              fireMark(drill, markAt(s.key, index), e)
            },
          }
        : {})}
    />
  )

  // Only the point under the pointer is drawn as a dot, so that is the one that takes the click.
  const activeDot = (seriesKey: string) =>
    drill
      ? (dot: ActiveDotProps) => (
          <circle
            cx={dot.cx}
            cy={dot.cy}
            r={dot.r}
            fill={dot.fill}
            stroke={dot.stroke}
            strokeWidth={dot.strokeWidth}
            style={pointer}
            onClick={(e) => fireMark(drill, markAt(seriesKey, dot.index), e)}
          />
        )
      : undefined

  // A drawn dot takes its own click so a chart with several lines opens the menu of the line that was hit.
  const dotOf = (seriesKey: string) =>
    drill
      ? (dot: { cx?: number; cy?: number; r?: number | string; fill?: string; stroke?: string; strokeWidth?: number | string; index?: number }) => (
          <circle
            key={dot.index}
            cx={dot.cx}
            cy={dot.cy}
            r={dot.r}
            fill={dot.fill}
            stroke={dot.stroke}
            strokeWidth={dot.strokeWidth}
            style={pointer}
            onClick={(e) => {
              e.stopPropagation()
              fireMark(drill, markAt(seriesKey, dot.index ?? -1), e)
            }}
          />
        )
      : true

  // A click or tap anywhere on a point's X (a touch never hovers first). With several lines it only resolves when
  // every line has the same row there; the dot under the pointer covers the rest.
  const lineKeys = (type === 'combo' ? model.series.filter((s) => (options.lineMeasures ?? []).includes(s.label)) : model.series).map((s) => s.key)
  const chartClick = drill && (type === 'line' || type === 'area' || type === 'combo')
    ? {
        style: pointer,
        onClick: (state: { activeTooltipIndex?: unknown }, e: React.MouseEvent) => {
          const index = pointIndexOf(state)
          const row = index === undefined ? undefined : rowAtX(model.points[index], lineKeys, drill.keyColumns)
          fireMark(drill, row ? { row } : null, e)
        },
      }
    : {}

  const line = (s: CartesianModel['series'][number], dashIndex: number) => (
    <Line
      key={s.key}
      dataKey={s.key}
      type="monotone"
      stroke={`var(--color-${s.key})`}
      strokeWidth={2}
      // A line style per series as well as a hue, so series never differ by colour alone.
      strokeDasharray={DASHES[dashIndex % DASHES.length]}
      dot={model.points.length <= 40 ? dotOf(s.key) : false}
      connectNulls={false}
      isAnimationActive={animate}
      activeDot={activeDot(s.key)}
    />
  )

  let chart: React.ReactElement
  if (type === 'line') {
    chart = (
      <LineChart data={data} accessibilityLayer {...chartClick}>
        <CartesianGrid vertical={false} />
        {category}
        {values}
        {tooltip}
        {legend}
        {model.series.map((s, i) => line(s, i))}
      </LineChart>
    )
  } else if (type === 'area') {
    chart = (
      <AreaChart data={data} accessibilityLayer {...chartClick}>
        <CartesianGrid vertical={false} />
        {category}
        {values}
        {tooltip}
        {legend}
        {model.series.map((s, i) => (
          <Area
            key={s.key}
            dataKey={s.key}
            type="monotone"
            stackId={stackId}
            fill={`var(--color-${s.key})`}
            fillOpacity={0.25}
            stroke={`var(--color-${s.key})`}
            strokeWidth={2}
            strokeDasharray={DASHES[i % DASHES.length]}
            connectNulls={false}
            isAnimationActive={animate}
            activeDot={activeDot(s.key)}
          />
        ))}
      </AreaChart>
    )
  } else if (type === 'horizontalBar') {
    chart = (
      <BarChart data={data} layout="vertical" accessibilityLayer>
        <CartesianGrid horizontal={false} />
        <XAxis type="number" tickLine={false} axisLine={false} tickMargin={8} tick={tick} domain={domain} tickFormatter={valueTick} />
        <YAxis
          type="category"
          dataKey="x"
          width={150}
          tickLine={false}
          axisLine={false}
          tick={tick}
          interval={0}
          tickFormatter={truncateLabel}
        />
        {tooltip}
        {legend}
        {model.series.map(bar)}
      </BarChart>
    )
  } else if (type === 'combo') {
    const lines = new Set(options.lineMeasures ?? [])
    const isLine = (s: CartesianModel['series'][number]) => lines.has(s.label)
    let lineIndex = 0
    chart = (
      <ComposedChart data={data} accessibilityLayer {...chartClick}>
        <CartesianGrid vertical={false} />
        {category}
        {values}
        {tooltip}
        {legend}
        {model.series.map((s) => (isLine(s) ? null : bar(s)))}
        {model.series.map((s) => (isLine(s) ? line(s, lineIndex++) : null))}
      </ComposedChart>
    )
  } else {
    chart = (
      <BarChart data={data} accessibilityLayer>
        <CartesianGrid vertical={false} />
        {category}
        {values}
        {tooltip}
        {legend}
        {model.series.map(bar)}
      </BarChart>
    )
  }

  return (
    <ChartContainer config={config} className={`aspect-auto w-full font-numeric text-[13px] ${heightClass}`}>
      {chart}
    </ChartContainer>
  )
}
