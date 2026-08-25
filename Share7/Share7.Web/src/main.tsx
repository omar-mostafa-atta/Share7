import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import './styles/tokens.css'
import './styles/global.css'

const root = document.getElementById('root')
if (!root) throw new Error('#root is missing from index.html')

createRoot(root).render(
  <StrictMode>
    {/*
      basename must match `base` in vite.config.ts and the MapFallbackToFile route in Program.cs.
      Without it every route would resolve against `/` and the app would try to navigate out of
      /app/ and into the old console.
    */}
    <BrowserRouter basename="/app">
      <App />
    </BrowserRouter>
  </StrictMode>,
)
