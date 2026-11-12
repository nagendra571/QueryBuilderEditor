import type { ValueFormat } from '@/types'
import type { TrendModel } from './story-models.ts'
import { formatValue } from './format.ts'

// The counter's own decimals only stand in for a style whose default is a whole number or a fixed
// currency scale; compact and percent keep their own default, or 1,230,974 would read "1M".
export function counterText(value: number | null, decimals: number, format?: ValueFormat | null): string {
  if (!format) return formatValue(value, { style: 'number', decimals })
  const usesCounterDecimals = format.style === 'number' || format.style === 'currency'
  return formatValue(value, { ...format, decimals: format.decimals ?? (usesCounterDecimals ? decimals : undefined) })
}

/** Sized from the container's width (cqw) and the text length so a long number shrinks to fit instead of clipping. */
export function fitFontSize(text: string): string {
  const perCharacter = 0.62
  const width = 92 / (Math.max(text.length, 1) * perCharacter)
  return `clamp(1rem, ${width.toFixed(2)}cqw, 4.5rem)`
}

export function targetText(pctOfTarget: number | null): string | null {
  if (pctOfTarget === null) return null
  return `${formatValue(pctOfTarget, { style: 'percent', decimals: 0 })} of target`
}

function magnitude(model: TrendModel, format?: ValueFormat | null): string | null {
  if (model.change === null) return null
  if (model.change === 0) return '0%'
  const size = Math.abs(model.change)
  return model.changePct === null
    ? formatValue(size, format)
    : formatValue(Math.abs(model.changePct), { style: 'percent', decimals: null })
}

/** Direction is carried by the arrow and the words, never by colour. */
export function trendChangeText(model: TrendModel, format?: ValueFormat | null): string | null {
  const size = magnitude(model, format)
  if (size === null || model.previousLabel === null || model.change === null) return null
  const arrow = model.change === 0 ? '→' : model.change > 0 ? '▲' : '▼'
  return `${arrow} ${size} vs ${model.previousLabel}`
}

export function trendSummary(model: TrendModel, measure: string, format?: ValueFormat | null): string {
  if (model.latest === null) return `Trend: ${measure}, no value`
  const head = `Trend: ${measure}, latest ${formatValue(model.latest, format)} on ${model.latestLabel}`
  const size = magnitude(model, format)
  if (size === null || model.previousLabel === null || model.change === null) return head
  const word = model.change === 0 ? 'no change' : `${model.change > 0 ? 'up' : 'down'} ${size}`
  return `${head}, ${word} compared with ${model.previousLabel}`
}
