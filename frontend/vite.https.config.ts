import { defineConfig, mergeConfig, type UserConfig } from 'vite';
import fs from 'node:fs';
import baseConfig from './vite.config.ts';

// Local-only HTTPS wrapper for the existing Vite dev server.
// The certificate is supplied at runtime through environment variables and is
// never written into this file or into a log.
export default defineConfig((env) => {
  const pfxPath = process.env.NIBRAS_HTTPS_PFX_PATH;
  const pfxPassphrase = process.env.NIBRAS_HTTPS_PFX_PASSPHRASE;

  if (!pfxPath || !pfxPassphrase) {
    throw new Error(
      'NIBRAS_HTTPS_PFX_PATH and NIBRAS_HTTPS_PFX_PASSPHRASE are required for the local HTTPS frontend.',
    );
  }

  const base = typeof baseConfig === 'function' ? baseConfig(env) : baseConfig;

  return mergeConfig(base as UserConfig, {
    server: {
      host: '127.0.0.1',
      port: 5173,
      strictPort: true,
      https: {
        pfx: fs.readFileSync(pfxPath),
        passphrase: pfxPassphrase,
      },
    },
  });
});
