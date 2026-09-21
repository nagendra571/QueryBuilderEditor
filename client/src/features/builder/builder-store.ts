import { create } from 'zustand'
import type {
  ColumnDataType,
  FilterCondition,
  FilterOperator,
  QueryColumn,
  QueryDefinition,
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

interface BuilderState {
  queryId: string | null
  name: string
  description: string
  dataSourceId: string | null
  definition: QueryDefinition
  isDirty: boolean

  startNew: (dataSourceId: string) => void
  loadExisting: (queryId: string, dataSourceId: string, name: string, description: string, definition: QueryDefinition) => void

  setSource: (source: Omit<QuerySource, 'alias'>) => void
  addColumn: (tableAlias: string, columnName: string, dataType: ColumnDataType) => void
  removeColumn: (key: string) => void
  reorderColumns: (fromIndex: number, toIndex: number) => void
  renameColumnAlias: (key: string, alias: string) => void
  toggleColumnVisible: (key: string) => void

  addFilterCondition: () => void
  updateFilterCondition: (index: number, patch: Partial<FilterCondition>) => void
  removeFilterCondition: (index: number) => void
  setFilterLogicalOperator: (operator: 'and' | 'or') => void
  toggleFilterParameterized: (index: number) => void

  addSort: (tableAlias: string, columnName: string) => void
  updateSortDirection: (index: number, direction: SortDirection) => void
  removeSort: (index: number) => void

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
    set((state) => ({
      definition: {
        ...state.definition,
        columns: state.definition.columns
          .filter((c) => columnKey(c.tableAlias, c.columnName, c.aggregate) !== key)
          .map((c, i) => ({ ...c, orderIndex: i })),
      },
      isDirty: true,
    })),

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
      definition: {
        ...state.definition,
        filters: {
          ...state.definition.filters,
          conditions: state.definition.filters.conditions.map((c, i) => (i === index ? { ...c, ...patch } : c)),
        },
      },
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
            {
              name: paramName,
              label: condition.columnName,
              dataType: condition.dataType,
              defaultValue: condition.value,
              isRequired: true,
            },
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

  setName: (name) => set({ name, isDirty: true }),
  setDescription: (description) => set({ description, isDirty: true }),
}))
