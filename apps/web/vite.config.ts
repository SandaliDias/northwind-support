import { loadEnv } from 'vite';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

// The dev server proxies /api to the backend so the browser only ever talks to one origin.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: env.VITE_API_PROXY_TARGET ?? 'https://localhost:7043',
          changeOrigin: true,
          // The backend uses the ASP.NET development certificate, which is self-signed.
          secure: false,
        },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: './src/test/setup.ts',
      css: false,
      fileParallelism: false,
      // A single thread worker is reliable on constrained Windows assessment machines.
      pool: 'threads',
      maxWorkers: 1,
    },
  };
});
