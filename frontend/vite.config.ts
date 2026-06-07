/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Single-origin transport (ADR-0011): in dev, the Vite server proxies the API routes to the
// backend so the browser only ever talks to one origin — no CORS. This proxy target is the
// ONLY place the absolute backend URL appears; the typed client always uses relative paths.
// In prod the same single-origin shape is realized by an Azure Static Web Apps linked backend.
const API_TARGET = 'http://localhost:5084'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/documents': { target: API_TARGET, changeOrigin: true },
      '/ask': { target: API_TARGET, changeOrigin: true },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
    css: false,
  },
})
