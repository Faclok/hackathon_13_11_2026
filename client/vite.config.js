import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';

const core = process.env.CORE_API_URL || 'http://127.0.0.1:5105';
const lms = process.env.LMS_API_URL || 'http://127.0.0.1:5093';

export default defineConfig({
  build: {
    rollupOptions: {
      input: {
        main: fileURLToPath(new URL('./index.html', import.meta.url)),
        test: fileURLToPath(new URL('./test/index.html', import.meta.url))
      }
    }
  },
  server: {
    proxy: {
      '/api/lms': { target: lms, changeOrigin: true },
      '/api': { target: core, changeOrigin: true },
      '/auth': { target: core, changeOrigin: true },
      '/health': { target: core, changeOrigin: true }
    }
  }
});
