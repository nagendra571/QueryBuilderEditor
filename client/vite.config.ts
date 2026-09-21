import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// QueryBuilder always mounts under /querybuilder in a host app (see QueryBuilder.Editor's
// QueryBuilderRoutes.BasePath) — base and the dev proxy target mirror that so dev-server behavior
// matches what the packaged app actually serves.
const basePath = '/querybuilder/'

// https://vite.dev/config/
export default defineConfig({
  base: basePath,
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
  server: {
    proxy: {
      [`${basePath}api`]: {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
})
