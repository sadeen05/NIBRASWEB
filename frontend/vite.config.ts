import { defineConfig, loadEnv, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';
import { resolveDevApiTarget } from './devApiTarget.ts';
import { fileURLToPath, URL } from 'node:url';

// SEC-16: the production bundle must carry the strict Content-Security-Policy
// required by the security specification. It is injected at build time only
// (`apply: 'build'`), because Vite's dev server injects inline scripts and
// HMR code that a `script-src 'self'` policy would block; `npm run dev` must
// keep working. `frame-ancestors` is kept for parity with the specification
// even though browsers honour it only as a response header.
const PRODUCTION_CSP = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self' 'unsafe-inline'",
  "connect-src 'self'",
  "img-src 'self' data:",
  "frame-ancestors 'none'",
].join('; ');

function productionCspPlugin(): Plugin {
  return {
    name: 'nibras-production-csp',
    apply: 'build',
    transformIndexHtml: {
      order: 'post',
      handler: (html) => ({
        html,
        tags: [
          {
            tag: 'meta',
            attrs: { 'http-equiv': 'Content-Security-Policy', content: PRODUCTION_CSP },
            injectTo: 'head-prepend',
          },
        ],
      }),
    },
  };
}

// Normal development uses http://localhost:5181. Test routing additionally requires
// --mode f1-acceptance and NIBRAS_TEST_API_TARGET=http://127.0.0.1:5199.
// The NIBRAS backend exposes its normal HTTP endpoint at http://localhost:5181
// (Properties/launchSettings.json, `http` profile). Development requests are
// proxied through Vite so the browser stays same-origin (the backend has no CORS
// policy configured). There are no SignalR hubs, so no `/hubs` proxy is added.
export default defineConfig(({ command, mode, isPreview }) => ({
  plugins: [react(), productionCspPlugin()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    ...(mode === 'f1-acceptance' ? { host: '127.0.0.1' } : {}),
    proxy: {
      '/api': {
        target: resolveDevApiTarget(command, mode, process.env.NIBRAS_TEST_API_TARGET, isPreview,
          mode === 'f1-acceptance'
            ? loadEnv(mode, fileURLToPath(new URL('.', import.meta.url)), 'VITE_API_BASE_URL').VITE_API_BASE_URL
            : undefined,
        ),
        changeOrigin: true,
        secure: false,
      },
    },
  },
}));
