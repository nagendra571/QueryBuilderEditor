import { apiClient } from '@/lib/api-client'
import type {
  AppUsersResult,
  AuditLogEntryDto,
  DataSourceCatalog,
  DataSourceDto,
  ExportQueryRequest,
  QueryShareDto,
  RunQueryRequest,
  SaveQueryRequest,
  SavedQueryDetailDto,
  SavedQuerySummaryDto,
  ShareQueryRequest,
  QueryResultDto,
  QuerySqlPreviewDto,
} from '@/types'

export const dataSourcesApi = {
  list: async (): Promise<DataSourceDto[]> => (await apiClient.get('/data-sources')).data,
  catalog: async (dataSourceId: string): Promise<DataSourceCatalog> =>
    (await apiClient.get(`/data-sources/${dataSourceId}/catalog`)).data,
  appUsers: async (dataSourceId: string): Promise<AppUsersResult> =>
    (await apiClient.get(`/data-sources/${dataSourceId}/app-users`)).data,
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
  getShares: async (id: string): Promise<QueryShareDto[]> => (await apiClient.get(`/queries/${id}/shares`)).data,
  share: async (id: string, request: ShareQueryRequest): Promise<{ id: string }> =>
    (await apiClient.post(`/queries/${id}/shares`, request)).data,
  removeShare: async (id: string, shareId: string): Promise<void> => {
    await apiClient.delete(`/queries/${id}/shares/${shareId}`)
  },
}

export const auditApi = {
  forQuery: async (queryId: string): Promise<AuditLogEntryDto[]> =>
    (await apiClient.get('/audit', { params: { entityType: 'SavedQuery', entityId: queryId } })).data,
}
