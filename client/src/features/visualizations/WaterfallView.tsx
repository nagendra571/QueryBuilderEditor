import { useMemo } from 'react'
import { Bar, BarChart, CartesianGrid, Cell, LabelList, ReferenceLine, XAxis, YAxis } from 'recharts'
import { ChartContainer, ChartTooltip, type ChartConfig } from '@/components/ui/chart'
import { formatAxis, formatValue, truncateLabel } from '@/features/visualizations/format'
import { KIND_LABEL, type WaterfallBar, type WaterfallModel } from '@/features/visualizations/story-models'
import type { ValueFormat } from '@/types'

const KIND_COLOR: Record<WaterfallBar['kind'], string> = {
  up: 'var(--chart-2)',
  down: 'var(--chart-4)',
  total: 'var(--chart-1)',
}

// Up and down differ by more than hue: each bar carries its own mark. "Total" needs room, so a bar too
// narrow for the word goes without it (the axis and tooltip still name it); the arrows always fit.
const KIND_MARK: Record<WaterfallBar['kind'], string> = { up: '▲', down: '▼', total: 'Total' }
const TOTAL_LABEL_WIDTH = 36

interface Datum {
  key: number
  name: string
  kind: WaterfallBar['kind']
  value: number
  range: [number, number]
}

function WaterfallTooltip({ active, payload, format }: { active?: boolean; payload?: { payload?: Datum }[]; format?: ValueFormat | null }) {
  const datum = payload?.[0]?.payload
  if (!active || !datum) return null
  return (
    <div className="grid min-w-32 gap-1 rounded-md border border-border bg-popover px-2.5 py-1.5 text-xs text-popover-foreground shadow-md">
      <span className="font-medium">{datum.name}</span>
      <span className="flex items-center justify-between gap-4">
        <span className="flex items-center gap-1.5">
          <span className="size-2.5 shrink-0 rounded-[2px]" style={{ background: KIND_COLOR[datum.kind] }} aria-hidden="true" />
          {KIND_LABEL[datum.kind]}
        </span>
        <span className="font-numeric tabular-nums">{formatValue(datum.value, format)}</span>
      </span>
    </div>
  )
}

/** One range bar per row. A stacked transparent base cannot draw a bar that crosses zero honestly, a [low, high] range can. */
export function WaterfallView({
  model,
  animate,
  heightClass,
  format,
  fill,
}: {
  model: WaterfallModel
  animate: boolean
  heightClass: string
  format?: ValueFormat | null
  fill: boolean
}) {
  const data: Datum[] = useMemo(
    () =>
      model.bars.map((b, i) => ({
        key: i,
        name: b.name,
        kind: b.kind,
        value: b.value,
        range: [b.base, b.base + b.size],
      })),
    [model],
  )
  const kinds = (['up', 'down', 'total'] as const).filter((k) => model.bars.some((b) => b.kind === k))
  const config: ChartConfig = {}
  const tick = { fill: 'var(--muted-foreground)' }

  const renderMark = (props: unknown) => {
    const { x, y, width, index } = props as { x: number; y: number; width: number; index: number }
    const kind = data[index]?.kind
    if (!kind || (kind === 'total' && width < TOTAL_LABEL_WIDTH)) return null
    return (
      <text x={x + width / 2} y={y - 5} textAnchor="middle" fontSize={11} fill="var(--foreground)" aria-hidden="true">
        {KIND_MARK[kind]}
      </text>
    )
  }

  return (
    <div className={`flex min-w-0 flex-col gap-2 ${fill ? 'h-full' : ''}`}>
      <ChartContainer
        config={config}
        className={`aspect-auto w-full font-numeric text-[13px] ${fill ? 'min-h-[96px] flex-1' : heightClass}`}
      >
        <BarChart data={data} accessibilityLayer margin={{ top: 18 }}>
          <CartesianGrid vertical={false} />
          <XAxis dataKey="key" tickLine={false} axisLine={false} tickMargin={8} minTickGap={16} tick={tick} tickFormatter={(k: number) => truncateLabel(data[k]?.name ?? '')} />
          <YAxis tickLine={false} axisLine={false} width={52} tick={tick} tickFormatter={(v: number) => formatAxis(v, format)} />
          <ReferenceLine y={0} stroke="var(--muted-foreground)" />
          <ChartTooltip content={<WaterfallTooltip format={format} />} />
          <Bar dataKey="range" maxBarSize={72} stroke="var(--mark-outline)" strokeWidth={1} isAnimationActive={animate}>
            {data.map((d) => (
              <Cell key={d.key} fill={KIND_COLOR[d.kind]} />
            ))}
            <LabelList dataKey="range" content={renderMark} />
          </Bar>
        </BarChart>
      </ChartContainer>
      <ul className="flex shrink-0 flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground" aria-label="Legend">
        {kinds.map((k) => (
          <li key={k} className="flex items-center gap-1.5">
            <span className="size-2.5 shrink-0 rounded-[2px]" style={{ background: KIND_COLOR[k] }} aria-hidden="true" />
            {KIND_LABEL[k]}
          </li>
        ))}
      </ul>
    </div>
  )
}
