import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'

// Imported for its module-level side effect: it stamps the persisted theme and
// density onto <html> before React's first paint. An effect inside a component
// would run after the first frame and flash the default palette.
import './store/prefs'

import './styles/tokens.css'
import './styles/global.css'
import './styles/console.css'

const root = document.getElementById('root')
if (!root) throw new Error('#root is missing from index.html')

createRoot(root).render(
  <StrictMode>
    {/* No basename: this console is the site root, matching `base: '/'` in vite.config.ts and the
        bare fallback route in Program.cs. It was mounted under /app/ only while the console it
        replaced still owned /. */}
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
)
