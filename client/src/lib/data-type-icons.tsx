import { Calendar, CaseSensitive, Clock, Fingerprint, Hash, HelpCircle, Link2, ToggleLeft } from 'lucide-react'
import type { ComponentType } from 'react'
import type { ColumnDataType } from '@/types'

interface DataTypeMeta {
  icon: ComponentType<{ className?: string }>
  label: string
  className: string
}

const META: Record<ColumnDataType, DataTypeMeta> = {
  text: { icon: CaseSensitive, label: 'Text', className: 'text-slate-500 dark:text-slate-400' },
  number: { icon: Hash, label: 'Number', className: 'text-primary' },
  date: { icon: Calendar, label: 'Date', className: 'text-warning' },
  dateTime: { icon: Clock, label: 'Date & time', className: 'text-warning' },
  boolean: { icon: ToggleLeft, label: 'Boolean', className: 'text-success' },
  guid: { icon: Fingerprint, label: 'Identifier', className: 'text-query' },
  unknown: { icon: HelpCircle, label: 'Unknown', className: 'text-muted-foreground' },
}

export function dataTypeMeta(dataType: ColumnDataType): DataTypeMeta {
  return META[dataType] ?? META.unknown
}

export function DataTypeIcon({
  dataType,
  isForeignKey,
  className,
}: {
  dataType: ColumnDataType
  isForeignKey?: boolean
  className?: string
}) {
  if (isForeignKey) {
    return <Link2 className={className ?? 'text-query size-3.5'} aria-label="Foreign key" />
  }
  const meta = dataTypeMeta(dataType)
  const Icon = meta.icon
  return <Icon className={className ?? `${meta.className} size-3.5`} aria-label={meta.label} />
}
