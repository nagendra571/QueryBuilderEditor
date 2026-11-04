import { create } from 'zustand'
import { aggregateResultDataType, describeAggregate } from '@/features/builder/aggregate-functions'
import { allowedTypesFor, defaultTypeFor, effectiveType, initialDefault, type ParameterValues } from '@/features/parameters/param-values'
import type {
  AggregateFunction,
  ColumnDataType,
  FilterCondition,
  FilterGroup,
  FilterOperator,
  QueryColumn,
  QueryDefinition,
  QueryParameter,
  QuerySort,
  QuerySource,
  SortDirection,
} from '@/types'

function emptyDefinition(): QueryDefinition {
  return {
    source: { schemaName: '', objectName: '', kind: 'view', alias: '' },
    joins: [],
    columns: [],
    filters: { operator: 'and', conditions: [], groups: [] },
    groupBy: [],
    sorts: [],
    parameters: [],
    rowLimit: 1000,
  }
}

export function columnKey(tableAlias: string, columnName: string, aggregate: string) {
  return `${tableAlias}.${columnName}.${aggregate}`
}

function emptyFilterGroup(): FilterGroup {
  return { operator: 'and', conditions: [], groups: [] }
}

/** Drops any HAVING condition whose measure (column + aggregate) is no longer selected. */
function pruneHaving(having: FilterGroup | null | undefined, columns: QueryColumn[]): FilterGroup | null | undefined {
  if (!having) return having
  const stillExists = (c: FilterCondition) =>
    columns.some((col) => col.tableAlias === c.tableAlias && col.columnName === c.columnName && col.aggregate === c.aggregate)
  const conditions = having.conditions.filter(stillExists)
  if (conditions.length === having.conditions.length) return having
  return { ...having, conditions }
}


function newParameter(name: string, label: string, condition: FilterCondition): QueryParameter {
  const type = defaultTypeFor(condition.dataType, condition.operator)
  return {
    name,
    label,
    dataType: condition.dataType,
    type,
    defaultValue: initialDefault(condition, type),
    isRequired: true,
    options: [],
    allowMultiple: false,
  }
}

/**
 * A parameter's type has to stay valid for the filter it is bound to. When the filter's column or
 * condition changes, keep the type if it still fits, otherwise fall back to the filter's natural
 * type (or drop the prompt entirely when the new condition can't be asked at run time).
 */
function reconcileParameters(definition: QueryDefinition): QueryDefinition {
  let parameters = definition.parameters
  let changed = false

  const fix = (group: FilterGroup | null | undefined): FilterGroup | null | undefined => {
    if (!group) return group
    return {
      ...group,
      conditions: group.conditions.map((c) => {
        if (!c.isParameterized) return c
        const parameter = parameters.find((p) => p.name === c.parameterName)
        const allowed = allowedTypesFor(c.dataType, c.operator)
        if (!parameter) return c
        const type = effectiveType(parameter)
        const multipleOk = c.operator === 'in' || c.operator === 'notIn'
        if (allowed.includes(type) && (multipleOk || !parameter.allowMultiple)) return c
        changed = true
        if (allowed.length === 0) {
          parameters = parameters.filter((p) => p.name !== parameter.name)
          return { ...c, isParameterized: false, parameterName: undefined }
        }
        const next = allowed.includes(type) ? type : defaultTypeFor(c.dataType, c.operator)
        parameters = parameters.map((p) =>
          p.name === parameter.name
            ? { ...p, dataType: c.dataType, type: next, defaultValue: null, options: [], optionsQueryId: null, allowMultiple: multipleOk && p.allowMultiple }
            : p,
        )
        return c
      }),
      groups: group.groups.map((g) => fix(g)!),
    }
  }

  const filters = fix(definition.filters)!
  const having = fix(definition.having)
  return changed ? { ...definition, filters, having, parameters } : definition
}

interface BuilderState {
  queryId: string | null
  name: string
  description: string
  dataSourceId: string | null
  definition: QueryDefinition
  isDirty: boolean
  /** Name of the parameter whose settings dialog is open, if any. */
  parameterSettingsFor: string | null
  openParameterSettings: (name: string | null) => void

  startNew: (dataSourceId: string) => void
  loadExisting: (queryId: string, dataSourceId: string, name: string, description: string, definition: QueryDefinition) => void

  setSource: (source: Omit<QuerySource, 'alias'>) => void
  addColumn: (tableAlias: string, columnName: string, dataType: ColumnDataType) => void
  removeColumn: (key: string) => void
  reorderColumns: (fromIndex: number, toIndex: number) => void
  renameColumnAlias: (key: string, alias: string) => void
  toggleColumnVisible: (key: string) => void
  setColumnAggregate: (key: string, aggregate: AggregateFunction) => void

  addFilterCondition: () => void
  updateFilterCondition: (index: number, patch: Partial<FilterCondition>) => void
  removeFilterCondition: (index: number) => void
  setFilterLogicalOperator: (operator: 'and' | 'or') => void
  toggleFilterParameterized: (index: number) => void

  addHavingCondition: () => void
  updateHavingCondition: (index: number, patch: Partial<FilterCondition>) => void
  removeHavingCondition: (index: number) => void
  setHavingLogicalOperator: (operator: 'and' | 'or') => void
  toggleHavingParameterized: (index: number) => void

  addSort: (tableAlias: string, columnName: string) => void
  updateSortDirection: (index: number, direction: SortDirection) => void
  removeSort: (index: number) => void

  updateParameter: (name: string, parameter: QueryParameter) => void
  /** Makes the given bar values this query's defaults; saved with the next Save. */
  setParameterDefaults: (values: ParameterValues) => void

  setName: (name: string) => void
  setDescription: (description: string) => void
}

export const useBuilderStore = create<BuilderState>((set) => ({
  queryId: null,
  name: '',
  description: '',
  dataSourceId: null,
  definition: emptyDefinition(),
  isDirty: false,
  parameterSettingsFor: null,
  openParameterSettings: (name) => set({ parameterSettingsFor: name }),

  startNew: (dataSourceId) =>
    set({
      queryId: null,
      name: '',
      description: '',
      dataSourceId,
      definition: emptyDefinition(),
      isDirty: false,
    }),

  loadExisting: (queryId, dataSourceId, name, description, definition) =>
    set({ queryId, dataSourceId, name, description, definition, isDirty: false }),

  setSource: (source) =>
    set((state) => ({
      definition: {
        ...emptyDefinition(),
        source: { ...source, alias: source.objectName.slice(0, 1).toLowerCase() || 't' },
      },
      isDirty: state.definition.source.objectName !== source.objectName,
    })),

  addColumn: (tableAlias, columnName, dataType) =>
    set((state) => {
      const exists = state.definition.columns.some(
        (c) => c.tableAlias === tableAlias && c.columnName === columnName && c.aggregate === 'none',
      )
      if (exists) return state
      const column: QueryColumn = {
        tableAlias,
        columnName,
        dataType,
        aggregate: 'none',
        orderIndex: state.definition.columns.length,
        isVisible: true,
      }
      return { definition: { ...state.definition, columns: [...state.definition.columns, column] }, isDirty: true }
    }),

  removeColumn: (key) =>
    set((state) => {
      const columns = state.definition.columns
        .filter((c) => columnKey(c.tableAlias, c.columnName, c.aggregate) !== key)
        .map((c, i) => ({ ...c, orderIndex: i }))
      return {
        definition: { ...state.definition, columns, having: pruneHaving(state.definition.having, columns) },
        isDirty: true,
      }
    }),

  reorderColumns: (fromIndex, toIndex) =>
    set((state) => {
      const columns = [...state.definition.columns]
      const [moved] = columns.splice(fromIndex, 1)
      columns.splice(toIndex, 0, moved)
      return { definition: { ...state.definition, columns: columns.map((c, i) => ({ ...c, orderIndex: i })) }, isDirty: true }
    }),

  renameColumnAlias: (key, alias) =>
    set((state) => ({
      definition: {
        ...state.definition,
        columns: state.definition.columns.map((c) =>
          columnKey(c.tableAlias, c.columnName, c.aggregate) === key ? { ...c, alias: alias || undefined } : c,
        ),
      },
      isDirty: true,
    })),

  toggleColumnVisible: (key) =>
    set((state) => ({
      definition: {
        ...state.definition,
        columns: state.definition.columns.map((c) =>
          columnKey(c.tableAlias, c.columnName, c.aggregate) === key ? { ...c, isVisible: !c.isVisible } : c,
        ),
      },
      isDirty: true,
    })),

  setColumnAggregate: (key, aggregate) =>
    set((state) => {
      const target = state.definition.columns.find((c) => columnKey(c.tableAlias, c.columnName, c.aggregate) === key)
      if (!target || target.aggregate === aggregate) return state

      // Auto-name it (e.g. "Sum of Amount") so exported headers are readable and two aggregates
      // on the same raw column never collide — but never clobber an alias the user typed themselves.
      const hadAutoOrNoAlias = !target.alias || target.alias === describeAggregate(target.aggregate, target.columnName)
      const alias = hadAutoOrNoAlias
        ? aggregate === 'none'
          ? undefined
          : describeAggregate(aggregate, target.columnName)
        : target.alias

      const columns = state.definition.columns.map((c) =>
        columnKey(c.tableAlias, c.columnName, c.aggregate) === key ? { ...c, aggregate, alias } : c,
      )
      return {
        definition: { ...state.definition, columns, having: pruneHaving(state.definition.having, columns) },
        isDirty: true,
      }
    }),

  addFilterCondition: () =>
    set((state) => {
      const firstColumn = state.definition.columns[0]
      const condition: FilterCondition = {
        tableAlias: firstColumn?.tableAlias ?? state.definition.source.alias,
        columnName: firstColumn?.columnName ?? '',
        dataType: firstColumn?.dataType ?? 'text',
        aggregate: 'none',
        operator: 'equals' as FilterOperator,
        value: '',
        isParameterized: false,
      }
      return {
        definition: {
          ...state.definition,
          filters: { ...state.definition.filters, conditions: [...state.definition.filters.conditions, condition] },
        },
        isDirty: true,
      }
    }),

  updateFilterCondition: (index, patch) =>
    set((state) => ({
      definition: reconcileParameters({
        ...state.definition,
        filters: {
          ...state.definition.filters,
          conditions: state.definition.filters.conditions.map((c, i) => (i === index ? { ...c, ...patch } : c)),
        },
      }),
      isDirty: true,
    })),

  removeFilterCondition: (index) =>
    set((state) => ({
      definition: {
        ...state.definition,
        filters: {
          ...state.definition.filters,
          conditions: state.definition.filters.conditions.filter((_, i) => i !== index),
        },
      },
      isDirty: true,
    })),

  setFilterLogicalOperator: (operator) =>
    set((state) => ({
      definition: { ...state.definition, filters: { ...state.definition.filters, operator } },
      isDirty: true,
    })),

  toggleFilterParameterized: (index) =>
    set((state) => {
      const condition = state.definition.filters.conditions[index]
      if (!condition) return state

      if (condition.isParameterized) {
        return {
          definition: {
            ...state.definition,
            parameters: state.definition.parameters.filter((p) => p.name !== condition.parameterName),
            filters: {
              ...state.definition.filters,
              conditions: state.definition.filters.conditions.map((c, i) =>
                i === index ? { ...c, isParameterized: false, parameterName: undefined } : c,
              ),
            },
          },
          isDirty: true,
        }
      }

      // Some conditions (is empty, between on a number...) have nothing to ask at run time.
      if (allowedTypesFor(condition.dataType, condition.operator).length === 0) return state

      const baseName = condition.columnName.replace(/[^a-zA-Z0-9_]/g, '_')
      let paramName = baseName
      let suffix = 1
      const existingNames = new Set(state.definition.parameters.map((p) => p.name))
      while (existingNames.has(paramName)) {
        paramName = `${baseName}_${suffix++}`
      }

      return {
        definition: {
          ...state.definition,
          parameters: [
            ...state.definition.parameters,
            newParameter(paramName, condition.columnName, condition),
          ],
          filters: {
            ...state.definition.filters,
            conditions: state.definition.filters.conditions.map((c, i) =>
              i === index ? { ...c, isParameterized: true, parameterName: paramName } : c,
            ),
          },
        },
        isDirty: true,
      }
    }),

  addHavingCondition: () =>
    set((state) => {
      const firstMeasure = state.definition.columns.find((c) => c.aggregate !== 'none')
      if (!firstMeasure) return state
      const condition: FilterCondition = {
        tableAlias: firstMeasure.tableAlias,
        columnName: firstMeasure.columnName,
        dataType: aggregateResultDataType(firstMeasure.dataType, firstMeasure.aggregate),
        aggregate: firstMeasure.aggregate,
        operator: 'greaterThan' as FilterOperator,
        value: '',
        isParameterized: false,
      }
      const having = state.definition.having ?? emptyFilterGroup()
      return {
        definition: { ...state.definition, having: { ...having, conditions: [...having.conditions, condition] } },
        isDirty: true,
      }
    }),

  updateHavingCondition: (index, patch) =>
    set((state) => {
      const having = state.definition.having
      if (!having) return state
      return {
        definition: reconcileParameters({
          ...state.definition,
          having: { ...having, conditions: having.conditions.map((c, i) => (i === index ? { ...c, ...patch } : c)) },
        }),
        isDirty: true,
      }
    }),

  removeHavingCondition: (index) =>
    set((state) => {
      const having = state.definition.having
      if (!having) return state
      return {
        definition: { ...state.definition, having: { ...having, conditions: having.conditions.filter((_, i) => i !== index) } },
        isDirty: true,
      }
    }),

  setHavingLogicalOperator: (operator) =>
    set((state) => {
      const having = state.definition.having
      if (!having) return state
      return { definition: { ...state.definition, having: { ...having, operator } }, isDirty: true }
    }),

  toggleHavingParameterized: (index) =>
    set((state) => {
      const having = state.definition.having
      const condition = having?.conditions[index]
      if (!having || !condition) return state

      if (condition.isParameterized) {
        return {
          definition: {
            ...state.definition,
            parameters: state.definition.parameters.filter((p) => p.name !== condition.parameterName),
            having: {
              ...having,
              conditions: having.conditions.map((c, i) =>
                i === index ? { ...c, isParameterized: false, parameterName: undefined } : c,
              ),
            },
          },
          isDirty: true,
        }
      }

      if (allowedTypesFor(condition.dataType, condition.operator).length === 0) return state

      const baseName = `${condition.columnName}_total`.replace(/[^a-zA-Z0-9_]/g, '_')
      let paramName = baseName
      let suffix = 1
      const existingNames = new Set(state.definition.parameters.map((p) => p.name))
      while (existingNames.has(paramName)) {
        paramName = `${baseName}_${suffix++}`
      }

      return {
        definition: {
          ...state.definition,
          parameters: [
            ...state.definition.parameters,
            newParameter(paramName, describeAggregate(condition.aggregate, condition.columnName), condition),
          ],
          having: {
            ...having,
            conditions: having.conditions.map((c, i) => (i === index ? { ...c, isParameterized: true, parameterName: paramName } : c)),
          },
        },
        isDirty: true,
      }
    }),

  addSort: (tableAlias, columnName) =>
    set((state) => {
      if (state.definition.sorts.some((s) => s.tableAlias === tableAlias && s.columnName === columnName)) return state
      const sort: QuerySort = { tableAlias, columnName, direction: 'asc', orderIndex: state.definition.sorts.length }
      return { definition: { ...state.definition, sorts: [...state.definition.sorts, sort] }, isDirty: true }
    }),

  updateSortDirection: (index, direction) =>
    set((state) => ({
      definition: {
        ...state.definition,
        sorts: state.definition.sorts.map((s, i) => (i === index ? { ...s, direction } : s)),
      },
      isDirty: true,
    })),

  removeSort: (index) =>
    set((state) => ({
      definition: {
        ...state.definition,
        sorts: state.definition.sorts.filter((_, i) => i !== index).map((s, i) => ({ ...s, orderIndex: i })),
      },
      isDirty: true,
    })),

  updateParameter: (name, parameter) =>
    set((state) => ({
      definition: {
        ...state.definition,
        parameters: state.definition.parameters.map((p) => (p.name === name ? { ...parameter, name } : p)),
      },
      isDirty: true,
    })),

  setParameterDefaults: (values) =>
    set((state) => ({
      definition: {
        ...state.definition,
        parameters: state.definition.parameters.map((p) =>
          p.name in values ? { ...p, defaultValue: values[p.name] } : p,
        ),
      },
      isDirty: true,
    })),

  setName: (name) => set({ name, isDirty: true }),
  setDescription: (description) => set({ description, isDirty: true }),
}))
