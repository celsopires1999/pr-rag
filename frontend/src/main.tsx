import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import './index.css'
import App from './App.tsx'
import { RagSettingsProvider } from './context/RagSettingsContext.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <RagSettingsProvider>
        <App />
      </RagSettingsProvider>
    </BrowserRouter>
  </StrictMode>,
)
