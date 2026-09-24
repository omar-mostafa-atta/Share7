import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { TellingProvider } from './board/pieces'
import './board/board.css'
import './board/screens.css'
import './board/surface'
import { I18nProvider } from './i18n/i18n'
import { start } from './lib/session'

// The refresh cookie is asked about before anything is drawn, so a reload lands
// a signed-in member back on their board rather than on the sign-in page.
void start()

createRoot(document.getElementById('studio')!).render(
  <StrictMode>
    <BrowserRouter>
      <I18nProvider>
        <TellingProvider>
          <App />
        </TellingProvider>
      </I18nProvider>
    </BrowserRouter>
  </StrictMode>,
)
