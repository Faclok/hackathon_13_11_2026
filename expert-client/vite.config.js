import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    proxy: Object.fromEntries(['/api', '/auth', '/health'].map(path => [path, {
      target: process.env.EXPERT_API_URL || 'http://127.0.0.1:5106',
      changeOrigin: true
    }]))
  }
});
