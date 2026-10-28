import type { CampaignSummary, CrossChannelReport, DailyTrendRow, Dashboard, DataSource, Me } from './types'
import acmeSummary from './mock/acme-summary.json'
import acmeReport from './mock/acme-cross-channel.json'
import acmeDaily from './mock/acme-daily.json'
import acmeMe from './mock/acme-me.json'
import globexSummary from './mock/globex-summary.json'
import globexReport from './mock/globex-cross-channel.json'
import globexDaily from './mock/globex-daily.json'
import globexMe from './mock/globex-me.json'

/**
 * Data access for the dashboard.
 *  - If VITE_API_URL is set and reachable, every call goes to the real ASP.NET Core API with the tenant's X-Api-Key.
 *  - Otherwise (no URL, network error, 401, timeout) we fall back to bundled JSON captured from the seeded API,
 *    so the Vercel demo works standalone. The UI surfaces which source is in use.
 */
const BASE = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '')
const DEFAULT_KEY = import.meta.env.VITE_API_KEY ?? 'acme-demo-key'
const TIMEOUT_MS = 4000

export const DEMO_TENANTS: { label: string; key: string }[] = [
  { label: 'Acme Retail', key: 'acme-demo-key' },
  { label: 'Globex Travel', key: 'globex-demo-key' },
]

export function defaultApiKey(): string {
  return localStorage.getItem('adpulse.apiKey') ?? DEFAULT_KEY
}

export function rememberApiKey(key: string) {
  localStorage.setItem('adpulse.apiKey', key)
}

async function get<T>(path: string, apiKey: string): Promise<T> {
  const ctrl = new AbortController()
  const timer = setTimeout(() => ctrl.abort(), TIMEOUT_MS)
  try {
    const res = await fetch(`${BASE}${path}`, { headers: { 'X-Api-Key': apiKey }, signal: ctrl.signal })
    if (!res.ok) {
      let detail = `${res.status} ${res.statusText}`
      try {
        const problem = await res.json()
        if (problem?.detail) detail = problem.detail
      } catch {
        /* body was not JSON */
      }
      throw new Error(detail)
    }
    return (await res.json()) as T
  } finally {
    clearTimeout(timer)
  }
}

/** Snapshots captured from the seeded API (one per demo tenant) so tenant switching also works offline. */
const SNAPSHOTS: Record<string, Dashboard> = {
  'acme-demo-key': { me: acmeMe as Me, summary: acmeSummary as CampaignSummary[], report: acmeReport as CrossChannelReport, daily: acmeDaily as DailyTrendRow[] },
  'globex-demo-key': { me: globexMe as Me, summary: globexSummary as CampaignSummary[], report: globexReport as CrossChannelReport, daily: globexDaily as DailyTrendRow[] },
}

function demoDashboard(apiKey: string): Dashboard {
  return SNAPSHOTS[apiKey] ?? SNAPSHOTS['acme-demo-key']
}

export async function loadDashboard(apiKey: string): Promise<{ data: Dashboard; source: DataSource }> {
  if (!BASE) {
    return { data: demoDashboard(apiKey), source: { kind: 'demo', reason: 'VITE_API_URL not configured' } }
  }
  try {
    const [me, summary, report, daily] = await Promise.all([
      get<Me>('/api/me', apiKey),
      get<CampaignSummary[]>('/api/campaigns/summary', apiKey),
      get<CrossChannelReport>('/api/reports/cross-channel', apiKey),
      get<DailyTrendRow[]>('/api/reports/daily', apiKey),
    ])
    return { data: { me, summary, report, daily }, source: { kind: 'live', baseUrl: BASE } }
  } catch (e) {
    const reason = e instanceof Error ? (e.name === 'AbortError' ? 'API timed out' : e.message) : 'API unreachable'
    return { data: demoDashboard(apiKey), source: { kind: 'demo', reason } }
  }
}

/** Lifecycle action against the live API (no-op in demo mode). */
export async function transition(id: string, action: 'activate' | 'pause' | 'complete', apiKey: string): Promise<void> {
  const res = await fetch(`${BASE}/api/campaigns/${id}/${action}`, { method: 'POST', headers: { 'X-Api-Key': apiKey } })
  if (!res.ok) {
    const problem = await res.json().catch(() => ({}))
    throw new Error(problem?.detail ?? `${res.status} ${res.statusText}`)
  }
}

export const isLiveConfigured = Boolean(BASE)
