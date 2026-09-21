import { useQuery } from '@tanstack/react-query'
import { formatDistanceToNow } from 'date-fns'
import { Download, FilePlus, History, Loader2, Pencil, Play, Trash2 } from 'lucide-react'
import type { ComponentType } from 'react'
import { Badge } from '@/components/ui/badge'
import { auditApi } from '@/lib/api'
import { cn } from '@/lib/utils'
import type { AuditAction, AuditLogEntryDto } from '@/types'

const ACTION_META: Record<AuditAction, { icon: ComponentType<{ className?: string }>; className: string }> = {
  queryCreated: { icon: FilePlus, className: 'text-success' },
  queryUpdated: { icon: Pencil, className: 'text-primary' },
  queryDeleted: { icon: Trash2, className: 'text-destructive' },
  queryRun: { icon: Play, className: 'text-query' },
  queryExported: { icon: Download, className: 'text-warning' },
}

export function HistoryPanel({ queryId }: { queryId: string }) {
  const { data, isLoading } = useQuery({
    queryKey: ['audit', queryId],
    queryFn: () => auditApi.forQuery(queryId),
  })

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading history…
      </div>
    )
  }

  if (!data || data.length === 0) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-2 text-center text-sm text-muted-foreground">
        <History className="size-8 text-muted-foreground/50" />
        No history yet. Run, edit, or export this query to start building its audit trail.
      </div>
    )
  }

  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-2 p-(--space-section)">
      {data.map((entry) => (
        <HistoryRow key={entry.id} entry={entry} />
      ))}
    </div>
  )
}

function HistoryRow({ entry }: { entry: AuditLogEntryDto }) {
  const meta = ACTION_META[entry.action] ?? ACTION_META.queryRun
  const Icon = meta.icon

  return (
    <div className="flex items-start gap-3 rounded-md border border-border bg-card px-3 py-(--space-row-y)">
      <div className={cn('mt-0.5 flex size-6 shrink-0 items-center justify-center rounded-full bg-muted', meta.className)}>
        <Icon className="size-3.5" />
      </div>
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <p className="text-(length:--text-body)">{entry.summary}</p>
        <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
          <Badge variant="secondary" className="h-4 px-1.5 text-[10px] font-normal">
            {entry.actor}
          </Badge>
          <span>{formatDistanceToNow(new Date(entry.timestampUtc), { addSuffix: true })}</span>
        </div>
      </div>
    </div>
  )
}
