import Editor from '@monaco-editor/react'
import { useTheme } from 'next-themes'
import { Badge } from '@/components/ui/badge'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import type { QuerySqlPreviewDto } from '@/types'

export function SqlPreviewDialog({
  open,
  onOpenChange,
  preview,
  isLoading,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  preview: QuerySqlPreviewDto | undefined
  isLoading: boolean
}) {
  const { resolvedTheme } = useTheme()

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[80vh] flex-col sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>Generated SQL</DialogTitle>
        </DialogHeader>

        <div className="min-h-64 overflow-hidden rounded-md border border-border">
          <Editor
            height="320px"
            language="sql"
            theme={resolvedTheme === 'dark' ? 'vs-dark' : 'light'}
            value={isLoading ? '-- generating…' : (preview?.sql ?? '-- select at least one column to preview SQL')}
            options={{
              readOnly: true,
              minimap: { enabled: false },
              fontSize: 13,
              fontFamily: 'JetBrains Mono Variable, ui-monospace, monospace',
              scrollBeyondLastLine: false,
              lineNumbers: 'on',
              wordWrap: 'on',
            }}
          />
        </div>

        {preview && preview.parameters.length > 0 && (
          <div className="flex flex-wrap gap-1.5">
            {preview.parameters.map((p) => (
              <Badge key={p.name} variant={p.isRuntimeParameter ? 'default' : 'secondary'} className="font-mono text-[11px]">
                {p.name}
                {p.isRuntimeParameter ? ' (prompted)' : ` = ${p.defaultValue ?? 'null'}`}
              </Badge>
            ))}
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
