import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The SPA is served by the API itself, from wwwroot/app — so every call is same-origin in
// production and there is no CORS anywhere in the solution. The dev proxy below preserves that
// property locally: the browser talks only to :5173, and /api is forwarded to Kestrel.
export default defineConfig({
  plugins: [react()],

  // Assets are requested from /app/, not /. Must match the outDir below and the
  // MapFallbackToFile route in Program.cs.
  base: '/app/',

  build: {
    outDir: '../Share7/wwwroot/app',

    // Never true. Vite treats outDir as a directory it owns and wipes it on build; one typo in
    // the path above then deletes the hand-written console next door, which is not in version
    // control and has no backup.
    emptyOutDir: false,

    sourcemap: true,
  },

  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'https://localhost:7147',
        changeOrigin: true,
        secure: false, // the ASP.NET dev certificate is self-signed
      },
    },
  },
})
