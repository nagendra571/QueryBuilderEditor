import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { FilterValueInput } from '@/features/builder/FilterValueInput'
import type { QueryParameter } from '@/types'

export function ParameterPromptDialog({
  open,
  onOpenChange,
  parameters,
  onConfirm,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  parameters: QueryParameter[]
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
              <Label>
                {param.label || param.name}
                {param.isRequired && <span className="text-destructive"> *</span>}
              </Label>
              <FilterValueInput
                dataType={param.dataType}
                operator="equals"
                value={values[param.name] ?? param.defaultValue ?? ''}
                onChange={(v) => setValues((prev) => ({ ...prev, [param.name]: v }))}
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
