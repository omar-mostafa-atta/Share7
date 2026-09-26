import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import type { Plugin } from 'vite'

/**
 * The dev server answers the bare address too: http://localhost:5174 and /studio (no slash) go to
 * /studio/, so nobody is shown Vite's "did you mean" page for the address they were given.
 */
function studioAtItsAddress(): Plugin {
  return {
    name: 'studio-at-its-address',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const path = (req.url ?? '').split('?')[0].toLowerCase()
        if (path === '/' || path === '/studio') {
          res.statusCode = 302
          res.setHeader('Location', '/studio/')
          res.end()
          return
        }
        next()
      })
    },
  }
}

// The Content Studio, at one fixed address for every member: /studio. In production the API serves
// this build there (Share7/Hosting/StudioHosting.cs) on the same site as the Admin Console, and
// answers /api too; locally this dev server serves it at http://localhost:5174/studio and the proxy
// below forwards /api to Kestrel. Every call is same-origin. No CORS.
//
// `base` must stay in step with StudioHosting.PathBase and the router's basename (main.tsx).
export default defineConfig({
  plugins: [react(), studioAtItsAddress()],
  base: '/studio/',

  build: {
    // Served by the API from here at /studio. The Studio owns this folder outright, unlike the
    // console's wwwroot, so emptying it on build is safe.
    outDir: '../Share7/wwwroot-studio',
    emptyOutDir: true,
    sourcemap: true,
  },

  server: {
    // Fixed, and never moved: the address handed to the content team is this port.
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
