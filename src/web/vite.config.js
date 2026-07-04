import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Dev server proxies API + SignalR to the .NET backend on :5000.
export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5000', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5000', ws: true, changeOrigin: true },
    },
  },
  build: {
    // Emit straight into the API's wwwroot so a single binary serves the SPA.
    outDir: '../Liquidcast.Api/wwwroot',
    emptyOutDir: true,
  },
})
