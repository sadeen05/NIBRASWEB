import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';

// The NIBRAS backend exposes its HTTP endpoint at http://localhost:5181
// (Properties/launchSettings.json, `http` profile). Development requests are
// proxied through Vite so the browser stays same-origin (the backend has no CORS
// policy configured). There are no SignalR hubs, so no `/hubs` proxy is added.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5181',
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
