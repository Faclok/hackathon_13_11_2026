import { defineConfig } from "vite";

export default defineConfig({
  server: {
    proxy: {
      "/api/lms": "http://localhost:8081",
      "/auth": "http://localhost:8080",
    },
  },
});
