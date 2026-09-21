import { apiClient } from '@/lib/api-client'
import type {
  AuditLogEntryDto,
  DataSourceCatalog,
  DataSourceDto,
  ExportQueryRequest,
  RunQueryRequest,
  SaveQueryRequest,
  SavedQueryDetailDto,
  SavedQuerySummaryDto,
  QueryResultDto,
  QuerySqlPreviewDto,
} from '@/types'

export const dataSourcesApi = {
  list: async (): Promise<DataSourceDto[]> => (await apiClient.get('/data-sources')).data,
  catalog: async (dataSourceId: string): Promise<DataSourceCatalog> =>
    (await apiClient.get(`/data-sources/${dataSourceId}/catalog`)).data,
}

export const savedQueriesApi = {
  list: async (): Promise<SavedQuerySummaryDto[]> => (await apiClient.get('/queries')).data,
  getById: async (id: string): Promise<SavedQueryDetailDto> => (await apiClient.get(`/queries/${id}`)).data,
  save: async (request: SaveQueryRequest): Promise<{ id: string }> =>
    (await apiClient.post('/queries', request)).data,
  remove: async (id: string): Promise<void> => {
    await apiClient.delete(`/queries/${id}`)
  },
  toggleFavorite: async (id: string): Promise<{ isFavorite: boolean }> =>
    (await apiClient.post(`/queries/${id}/favorite`)).data,
  previewSql: async (request: RunQueryRequest): Promise<QuerySqlPreviewDto> =>
    (await apiClient.post('/queries/preview-sql', request)).data,
  run: async (request: RunQueryRequest): Promise<QueryResultDto> =>
    (await apiClient.post('/queries/run', request)).data,
  export: async (request: ExportQueryRequest): Promise<Blob> =>
    (await apiClient.post('/queries/export', request, { responseType: 'blob' })).data,
}

export const auditApi = {
  forQuery: async (queryId: string): Promise<AuditLogEntryDto[]> =>
    (await apiClient.get('/audit', { params: { entityType: 'SavedQuery', entityId: queryId } })).data,
}
