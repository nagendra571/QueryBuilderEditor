import { ChevronFirst, ChevronLast, ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { PAGE_SIZE_OPTIONS, type PageSize } from '@/features/builder/results/useResultsView'

export function ResultsPagination({
  page,
  pageCount,
  pageSize,
  totalFiltered,
  onPageChange,
  onPageSizeChange,
}: {
  page: number
  pageCount: number
  pageSize: PageSize
  totalFiltered: number
  onPageChange: (page: number) => void
  onPageSizeChange: (size: PageSize) => void
}) {
  const start = totalFiltered === 0 ? 0 : (page - 1) * (pageSize === 'all' ? totalFiltered : pageSize) + 1
  const end = pageSize === 'all' ? totalFiltered : Math.min(page * pageSize, totalFiltered)

  return (
    <div className="flex flex-wrap items-center gap-3 border-t border-border px-(--space-row-x) py-1.5 text-xs text-muted-foreground">
      <span>
        {totalFiltered === 0 ? 'No rows' : `${start.toLocaleString()}–${end.toLocaleString()} of ${totalFiltered.toLocaleString()}`}
      </span>

      <div className="flex items-center gap-1.5">
        <span>Rows per page</span>
        <Select value={String(pageSize)} onValueChange={(v) => onPageSizeChange(v === 'all' ? 'all' : (Number(v) as PageSize))}>
          <SelectTrigger size="sm" className="h-6 w-[4.5rem] text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {PAGE_SIZE_OPTIONS.map((opt) => (
              <SelectItem key={opt} value={String(opt)} className="text-xs">
                {opt === 'all' ? 'All' : opt}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {pageSize !== 'all' && pageCount > 1 && (
        <div className="ml-auto flex items-center gap-1">
          <Button variant="ghost" size="icon" className="size-6" disabled={page <= 1} onClick={() => onPageChange(1)} aria-label="First page">
            <ChevronFirst className="size-3.5" />
          </Button>
          <Button variant="ghost" size="icon" className="size-6" disabled={page <= 1} onClick={() => onPageChange(page - 1)} aria-label="Previous page">
            <ChevronLeft className="size-3.5" />
          </Button>
          <span className="px-1 tabular-nums">
            Page {page} of {pageCount}
          </span>
          <Button variant="ghost" size="icon" className="size-6" disabled={page >= pageCount} onClick={() => onPageChange(page + 1)} aria-label="Next page">
            <ChevronRight className="size-3.5" />
          </Button>
          <Button variant="ghost" size="icon" className="size-6" disabled={page >= pageCount} onClick={() => onPageChange(pageCount)} aria-label="Last page">
            <ChevronLast className="size-3.5" />
          </Button>
        </div>
      )}
    </div>
  )
}
