import { defineConfig } from 'vite';

const core = process.env.CORE_API_URL || 'http://127.0.0.1:5105';
const lms = process.env.LMS_API_URL || 'http://127.0.0.1:5093';

export default defineConfig({
  server: {
    proxy: {
      '/api/lms': { target: lms, changeOrigin: true },
      '/api': { target: core, changeOrigin: true },
      '/auth': { target: core, changeOrigin: true },
      '/health': { target: core, changeOrigin: true }
    }
  }
});
