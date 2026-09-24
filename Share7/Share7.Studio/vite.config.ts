import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The Content Studio: its own app on its own origin (studio.<domain> in production), so nothing it
// stores can be read by the Admin Console and the reverse. Every call is still same-origin: in
// production the API serves this build on the Studio's host name (Share7/Hosting/StudioHosting.cs)
// and answers /api there too; locally the dev proxy below forwards /api to Kestrel. No CORS.
export default defineConfig({
  plugins: [react()],
  base: '/',

  build: {
    // Served by the API from here when Studio:Host is set. The Studio owns this folder outright,
    // unlike the console's wwwroot, so emptying it on build is safe.
    outDir: '../Share7/wwwroot-studio',
    emptyOutDir: true,
    sourcemap: true,
  },

  server: {
    port: 5174,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'https://localhost:7147',
        changeOrigin: true,
        secure: false, // the ASP.NET dev certificate is self-signed
      },
    },
  },
})
