import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// In dev the API runs on :5090 (compose) and Vite proxies to it, so the browser sees one origin.
const api = process.env.JOB_RADAR_API ?? 'http://localhost:5090'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': api,
      '/hubs': { target: api, ws: true },
    },
  },
  test: {
    environment: 'node',
  },
})
