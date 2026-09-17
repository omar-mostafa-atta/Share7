import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The SPA is served by the API itself, straight out of wwwroot — so every call is same-origin in
// production and there is no CORS anywhere in the solution. The dev proxy below preserves that
// property locally: the browser talks only to :5173, and /api is forwarded to Kestrel.
export default defineConfig({
  plugins: [react()],

  // This console is the site root now that the hand-written one it replaced is gone. Must stay in
  // step with the BrowserRouter basename in main.tsx (none) and the fallback in Program.cs.
  base: '/',

  build: {
    outDir: '../Share7/wwwroot',

    // Never true. Vite treats outDir as a directory it owns and wipes it on build, and outDir is
    // now wwwroot itself — one build with this on would take everything else in there with it.
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
