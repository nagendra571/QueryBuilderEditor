import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { ParameterControl } from '@/features/parameters/controls/ParameterControl'
import type { DashboardContext, QueryParameter } from '@/types'

export function ParameterPromptDialog({
  open,
  onOpenChange,
  parameters,
  queryId,
  context,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  parameters: QueryParameter[]
  /** The saved query the parameters belong to; lets query-based dropdowns load their choices. */
  queryId?: string
  /** Set when the query is a dashboard tile's, so choices load through the dashboard. */
  context?: DashboardContext
  onConfirm: (values: Record<string, string | null>) => void
}) {
  const [values, setValues] = useState<Record<string, string>>({})

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Enter parameter values</DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-3">
          {parameters.map((param) => (
            <div key={param.name} className="flex flex-col gap-1.5">
              <Label htmlFor={`prompt-${param.name}`}>
                {param.label || param.name}
                {param.isRequired && <span className="text-destructive"> *</span>}
              </Label>
              <ParameterControl
                parameter={param}
                id={`prompt-${param.name}`}
                value={values[param.name] ?? param.defaultValue ?? null}
                onChange={(v) => setValues((prev) => ({ ...prev, [param.name]: v ?? '' }))}
                optionsSource={queryId ? { kind: 'saved', queryId, name: param.name, context } : null}
              />
            </div>
          ))}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            onClick={() => {
              const resolved: Record<string, string | null> = {}
              for (const p of parameters) {
                resolved[p.name] = values[p.name] ?? p.defaultValue ?? null
              }
              onConfirm(resolved)
            }}
          >
            Run with these values
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
