import { createContext, useContext, useState, type ReactNode } from 'react'

interface RagSettings {
  topK: number | null
  minSimilarity: number | null
}

interface RagSettingsContextValue extends RagSettings {
  setTopK: (value: number | null) => void
  setMinSimilarity: (value: number | null) => void
}

const RagSettingsContext = createContext<RagSettingsContextValue | null>(null)

export function RagSettingsProvider({ children }: { children: ReactNode }) {
  const [topK, setTopK] = useState<number | null>(null)
  const [minSimilarity, setMinSimilarity] = useState<number | null>(null)

  return (
    <RagSettingsContext.Provider
      value={{ topK, minSimilarity, setTopK, setMinSimilarity }}
    >
      {children}
    </RagSettingsContext.Provider>
  )
}

export function useRagSettings(): RagSettingsContextValue {
  const ctx = useContext(RagSettingsContext)
  if (!ctx) {
    throw new Error('useRagSettings must be used within a RagSettingsProvider')
  }
  return ctx
}
