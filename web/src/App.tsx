import { Routes, Route } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { ChatPage } from '@/pages/ChatPage'
import { StatusPage } from '@/pages/StatusPage'

function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<ChatPage />} />
        <Route path="/status" element={<StatusPage />} />
      </Route>
    </Routes>
  )
}

export default App
