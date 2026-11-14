import { Download, FileSpreadsheet, FileText, Loader2 } from 'lucide-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { downloadFileName } from '@/features/visualizations/download-name'
import { visualizationsApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import { downloadBlob } from '@/lib/download'
import type { DashboardContext, DownloadFormat } from '@/types'

/**
 * Downloads the rows a chart is drawn from, with the same parameter values and dashboard context as the drawing, so the
 * file always matches what is on screen. The server re-checks access, scope and limits.
 */
export function ChartDownloadMenu({
  queryId,
  visualizationId,
  name,
  parameterValues,
  context,
  compact,
}: {
  queryId: string
  visualizationId: string
  /** The title shown for the chart; also the file name. */
  name: string
  parameterValues: Record<string, string | null>
  context?: DashboardContext
  /** An icon-only trigger, for a dashboard tile header. */
  compact?: boolean
}) {
  const queryClient = useQueryClient()
  const download = useMutation({
    mutationFn: async (format: DownloadFormat) => {
      const blob = await visualizationsApi.download(queryId, visualizationId, { format, parameterValues, ...context })
      downloadBlob(blob, downloadFileName(name, format))
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['audit', queryId] }),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'The download failed.'),
  })

  const label = `Download data from ${name}`
  const icon = download.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Download className="size-3.5" />

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        {compact ? (
          <Button variant="ghost" size="icon" className="size-6 shrink-0" aria-label={label} title="Download data" disabled={download.isPending}>
            {icon}
          </Button>
        ) : (
          <Button variant="outline" size="sm" className="h-8 text-xs" aria-label={label} disabled={download.isPending}>
            {icon}
            Download
          </Button>
        )}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-48">
        <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">The data in this chart</DropdownMenuLabel>
        <DropdownMenuItem onSelect={() => download.mutate('csv')}>
          <FileText className="size-3.5" />
          CSV (.csv)
        </DropdownMenuItem>
        <DropdownMenuItem onSelect={() => download.mutate('xlsx')}>
          <FileSpreadsheet className="size-3.5" />
          Excel (.xlsx)
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
