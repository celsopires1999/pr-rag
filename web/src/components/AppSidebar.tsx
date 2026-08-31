import { NavLink } from 'react-router-dom'
import { MessageSquare, Activity, Database } from 'lucide-react'
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from '@/components/ui/sidebar'
import { Label } from '@/components/ui/label'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { useRagSettings } from '@/context/RagSettingsContext'
import { useIngest } from '@/hooks/use-ingest'

export function AppSidebar() {
  const { topK, minSimilarity, setTopK, setMinSimilarity } = useRagSettings()
  const { run, loading, result, error } = useIngest()

  return (
    <Sidebar>
      <SidebarHeader>
        <div className="px-3 py-2 text-sm font-semibold">PrRag</div>
      </SidebarHeader>
      <SidebarContent>
        <SidebarGroup>
          <SidebarGroupLabel>Navigate</SidebarGroupLabel>
          <SidebarGroupContent>
            <SidebarMenu>
              <SidebarMenuItem>
                <SidebarMenuButton asChild isActive>
                  <NavLink to="/">
                    <MessageSquare />
                    <span>Chat</span>
                  </NavLink>
                </SidebarMenuButton>
              </SidebarMenuItem>
              <SidebarMenuItem>
                <SidebarMenuButton asChild>
                  <NavLink to="/status">
                    <Activity />
                    <span>System Status</span>
                  </NavLink>
                </SidebarMenuButton>
              </SidebarMenuItem>
            </SidebarMenu>
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarContent>
      <SidebarFooter>
        <SidebarGroup>
          <SidebarGroupLabel>System options</SidebarGroupLabel>
          <SidebarGroupContent className="space-y-3 px-1">
            <div className="space-y-1.5">
              <Label htmlFor="topk">Top K</Label>
              <Input
                id="topk"
                type="number"
                min={1}
                placeholder="default"
                value={topK ?? ''}
                onChange={(e) =>
                  setTopK(e.target.value === '' ? null : Number(e.target.value))
                }
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="minsim">Min similarity</Label>
              <Input
                id="minsim"
                type="number"
                min={0}
                max={1}
                step={0.05}
                placeholder="default"
                value={minSimilarity ?? ''}
                onChange={(e) =>
                  setMinSimilarity(
                    e.target.value === '' ? null : Number(e.target.value),
                  )
                }
              />
            </div>
            <Button
              variant="outline"
              className="w-full"
              onClick={run}
              disabled={loading}
            >
              <Database className="size-4" />
              {loading ? 'Ingesting…' : 'Run ingestion'}
            </Button>
            {result && (
              <p className="text-xs text-muted-foreground">
                Inserted {result.inserted} · Updated {result.updated} · Embedded{' '}
                {result.embedded}
              </p>
            )}
            {error && <p className="text-xs text-destructive">{error}</p>}
          </SidebarGroupContent>
        </SidebarGroup>
      </SidebarFooter>
    </Sidebar>
  )
}
