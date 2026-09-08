import { Routes, Route } from 'react-router-dom'
import { Layout } from '@/components/Layout'
import { ChatPage } from '@/pages/ChatPage'
import { StatusPage } from '@/pages/StatusPage'
import { RequisitionsPage } from '@/pages/RequisitionsPage'

function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<ChatPage />} />
        <Route path="/requisitions" element={<RequisitionsPage />} />
        <Route path="/status" element={<StatusPage />} />
      </Route>
    </Routes>
  )
}

export default App
