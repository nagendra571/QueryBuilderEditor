import { Settings2, Sparkles } from 'lucide-react'
import { useBuilderStore } from '@/features/builder/builder-store'
import { effectiveType, parameterTitle, TYPE_LABELS } from '@/features/parameters/param-values'

/** What a run-time filter shows instead of a value: how it will be asked, with a way into its settings. */
export function ParameterChip({ name }: { name: string }) {
  // find() returns the stored object itself, so this selector is snapshot-stable.
  const parameter = useBuilderStore((s) => s.definition.parameters.find((p) => p.name === name))
  const openSettings = useBuilderStore((s) => s.openParameterSettings)

  return (
    <button
      type="button"
      onClick={() => openSettings(name)}
      aria-label={`Settings for parameter ${parameter ? parameterTitle(parameter) : name}`}
      className="inline-flex h-7 min-w-0 items-center gap-1.5 rounded-md bg-query/10 px-2 text-xs font-medium text-query outline-none hover:bg-query/15 focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      <Sparkles className="size-3 shrink-0" />
      <span className="truncate">
        Asked as “{parameter ? parameterTitle(parameter) : name}”
        {parameter && <span className="font-normal opacity-80"> · {TYPE_LABELS[effectiveType(parameter)]}</span>}
      </span>
      <Settings2 className="size-3 shrink-0 opacity-70" />
    </button>
  )
}
