import { apiBase } from './config'
import type {
  ApiProblem,
  ByChannelReport,
  Campaign,
  CampaignSummary,
  ImportPreview,
  ImportSummary,
  KpiRow,
  MetricRow,
  Pacing,
  PacingStatus,
  Tenant,
  TenantCreated,
  TrendRow,
} from './types'

export const API_PREFIX = '/api/v1'
export const NGROK_HEADER = { 'ngrok-skip-browser-warning': '1' } as const
const TIMEOUT_MS = 12_000

export type ApiErrorKind = 'network' | 'timeout' | 'unauthorized' | 'not-found' | 'conflict' | 'validation' | 'server' | 'http'

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number
  readonly code: string
  readonly problem?: ApiProblem
  readonly url: string
  constructor(kind: ApiErrorKind, message: string, opts: { status?: number; code?: string; problem?: ApiProblem; url: string }) {
    super(message)
    this.name = 'ApiError'
    this.kind = kind
    this.status = opts.status ?? 0
    this.code = opts.code ?? kind
    this.problem = opts.problem
    this.url = opts.url
  }
  /** True when the key itself is the problem (401) or the tenant/resource does not exist for this key (404). */
  get isAuthProblem() {
    return this.kind === 'unauthorized'
  }
  get isUnreachable() {
    return this.kind === 'network' || this.kind === 'timeout'
  }
}

export function describeError(e: unknown): string {
  if (e instanceof ApiError) return e.message
  if (e instanceof Error) return e.message
  return String(e)
}

function codeFromProblem(p?: ApiProblem): string | undefined {
  if (!p?.type) return undefined
  const i = p.type.lastIndexOf('/')
  return i >= 0 ? p.type.slice(i + 1) : p.type
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  key?: string | null
  body?: unknown
  form?: FormData
  query?: Record<string, string | number | boolean | undefined | null>
  headers?: Record<string, string>
  signal?: AbortSignal
  /** return raw Response text instead of JSON */
  text?: boolean
}

export function buildUrl(path: string, query?: RequestOptions['query']): string {
  const url = new URL(apiBase() + (path.startsWith('/api') ? path : API_PREFIX + path))
  if (query) {
    for (const [k, v] of Object.entries(query)) {
      if (v === undefined || v === null || v === '') continue
      url.searchParams.set(k, String(v))
    }
  }
  return url.toString()
}

export function baseHeaders(key?: string | null, extra?: Record<string, string>): Record<string, string> {
  const h: Record<string, string> = { Accept: 'application/json, application/problem+json;q=0.9, text/csv;q=0.8, */*;q=0.5', ...NGROK_HEADER }
  if (key) h['X-Api-Key'] = key
  return { ...h, ...(extra ?? {}) }
}

export async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const url = buildUrl(path, opts.query)
  const ctrl = new AbortController()
  const timer = setTimeout(() => ctrl.abort(new DOMException('timeout', 'TimeoutError')), TIMEOUT_MS)
  if (opts.signal) opts.signal.addEventListener('abort', () => ctrl.abort(opts.signal?.reason), { once: true })
  const headers = baseHeaders(opts.key, opts.headers)
  let body: BodyInit | undefined
  if (opts.form) body = opts.form
  else if (opts.body !== undefined) {
    headers['Content-Type'] = 'application/json'
    body = JSON.stringify(opts.body)
  }
  let res: Response
  try {
    res = await fetch(url, { method: opts.method ?? 'GET', headers, body, signal: ctrl.signal, mode: 'cors' })
  } catch (e) {
    clearTimeout(timer)
    const isTimeout = e instanceof DOMException && (e.name === 'TimeoutError' || e.message === 'timeout')
    if (opts.signal?.aborted) throw e
    throw new ApiError(isTimeout ? 'timeout' : 'network', isTimeout ? `The API did not answer within ${TIMEOUT_MS / 1000}s.` : 'Could not reach the API (network error or CORS).', { url })
  }
  clearTimeout(timer)

  if (!res.ok) {
    let problem: ApiProblem | undefined
    let fallbackText = ''
    const ct = res.headers.get('content-type') ?? ''
    try {
      if (ct.includes('json')) problem = (await res.json()) as ApiProblem
      else fallbackText = (await res.text()).slice(0, 200)
    } catch {
      /* ignore body */
    }
    const code = codeFromProblem(problem)
    const kind: ApiErrorKind =
      res.status === 401 || res.status === 403 ? 'unauthorized'
      : res.status === 404 ? 'not-found'
      : res.status === 409 ? 'conflict'
      : res.status === 422 || res.status === 400 ? 'validation'
      : res.status >= 500 ? 'server'
      : 'http'
    let message = problem?.detail || problem?.title || fallbackText || `${res.status} ${res.statusText}`
    if (problem?.errors) {
      const parts = Object.entries(problem.errors).map(([f, msgs]) => `${f}: ${msgs.join(', ')}`)
      if (parts.length) message = parts.join(' · ')
    }
    // An HTML page from a wrong host is not an API answer.
    if (!problem && ct.includes('text/html')) message = `Got an HTML page instead of JSON from ${new URL(url).host} (is the API online?)`
    throw new ApiError(kind, message, { status: res.status, code, problem, url })
  }
  if (res.status === 204) return undefined as T
  if (opts.text) return (await res.text()) as T
  const ct = res.headers.get('content-type') ?? ''
  if (!ct.includes('json')) {
    const t = await res.text()
    try {
      return JSON.parse(t) as T
    } catch {
      throw new ApiError('http', `Expected JSON from ${new URL(url).host} but got ${ct || 'an unknown content type'}.`, { status: res.status, url })
    }
  }
  return (await res.json()) as T
}

/* ----------------------------- normalisers ----------------------------- */
/** Tolerant property access: camelCase first, then PascalCase, then snake_case. */
export function pick<T = unknown>(o: Record<string, unknown> | undefined | null, ...names: string[]): T | undefined {
  if (!o) return undefined
  for (const n of names) {
    if (n in o && o[n] !== undefined) return o[n] as T
    const pascal = n[0].toUpperCase() + n.slice(1)
    if (pascal in o && o[pascal] !== undefined) return o[pascal] as T
    const snake = n.replace(/[A-Z]/g, (m) => '_' + m.toLowerCase())
    if (snake in o && o[snake] !== undefined) return o[snake] as T
  }
  return undefined
}
const num = (v: unknown, d = 0): number => {
  const n = typeof v === 'string' ? parseFloat(v) : (v as number)
  return Number.isFinite(n) ? n : d
}
const str = (v: unknown, d = ''): string => (v === undefined || v === null ? d : String(v))

export function normalizePacingStatus(v: unknown): PacingStatus {
  const s = str(v).toLowerCase().replace(/[_\s]/g, '-')
  if (s.startsWith('under')) return 'under'
  if (s.startsWith('over')) return 'over'
  if (s === 'on-track' || s === 'ontrack' || s === 'on-pace' || s === 'onpace' || s === 'ok' || s === 'healthy') return 'on-track'
  return 'not-started'
}

export function normalizeCampaign(o: Record<string, unknown>): Campaign {
  return {
    id: str(pick(o, 'id', 'campaignId')),
    name: str(pick(o, 'name', 'campaignName')),
    channel: str(pick(o, 'channel'), 'Other'),
    status: (str(pick(o, 'status'), 'Draft') as Campaign['status']),
    dailyBudget: num(pick(o, 'dailyBudget', 'budget')),
    startDate: str(pick(o, 'startDate')),
    endDate: str(pick(o, 'endDate')),
    createdUtc: pick<string>(o, 'createdUtc', 'createdAt'),
    updatedUtc: pick<string>(o, 'updatedUtc', 'updatedAt'),
  }
}

export function normalizeSummary(o: Record<string, unknown>): CampaignSummary {
  const base = normalizeCampaign(o)
  const spend = num(pick(o, 'spend', 'cost'))
  const clicks = num(pick(o, 'clicks'))
  const impressions = num(pick(o, 'impressions'))
  const conversions = num(pick(o, 'conversions'))
  const pacing = pick<Record<string, unknown>>(o, 'pacing')
  return {
    ...base,
    impressions,
    clicks,
    conversions,
    spend,
    revenue: num(pick(o, 'revenue')),
    ctr: num(pick(o, 'ctr'), impressions ? (clicks / impressions) * 100 : 0),
    cpc: num(pick(o, 'cpc'), clicks ? spend / clicks : 0),
    cpa: num(pick(o, 'cpa'), conversions ? spend / conversions : 0),
    roas: num(pick(o, 'roas')),
    pacingPercent: num(pick(o, 'pacingPercent') ?? pick(pacing, 'pacingPercent', 'percent')),
    pacingStatus: normalizePacingStatus(pick(o, 'pacingStatus') ?? pick(pacing, 'status')),
    suggestedDailyBudget: num(pick(o, 'suggestedDailyBudget') ?? pick(pacing, 'suggestedDailyBudget')),
    expectedSpendToDate: pick<number>(o, 'expectedSpendToDate') ?? pick<number>(pacing, 'expectedSpendToDate'),
    actualSpendToDate: pick<number>(o, 'actualSpendToDate') ?? pick<number>(pacing, 'actualSpendToDate'),
  }
}

export function normalizeKpiRow(o: Record<string, unknown>): KpiRow {
  const spend = num(pick(o, 'spend', 'cost'))
  const clicks = num(pick(o, 'clicks'))
  const impressions = num(pick(o, 'impressions'))
  const conversions = num(pick(o, 'conversions'))
  return {
    channel: str(pick(o, 'channel'), 'Total'),
    campaigns: num(pick(o, 'campaigns', 'campaignCount')),
    impressions,
    clicks,
    conversions,
    spend,
    revenue: num(pick(o, 'revenue')),
    ctr: num(pick(o, 'ctr'), impressions ? (clicks / impressions) * 100 : 0),
    cpc: num(pick(o, 'cpc'), clicks ? spend / clicks : 0),
    cpa: num(pick(o, 'cpa'), conversions ? spend / conversions : 0),
    roas: num(pick(o, 'roas'), spend ? num(pick(o, 'revenue')) / spend : 0),
  }
}

export function normalizePacing(o: Record<string, unknown>): Pacing {
  return {
    campaignId: str(pick(o, 'campaignId', 'id')),
    campaignName: pick<string>(o, 'campaignName', 'name'),
    channel: pick<string>(o, 'channel'),
    asOf: str(pick(o, 'asOf')),
    daysElapsed: num(pick(o, 'daysElapsed')),
    daysTotal: num(pick(o, 'daysTotal')),
    dailyBudget: num(pick(o, 'dailyBudget')),
    expectedSpendToDate: num(pick(o, 'expectedSpendToDate', 'expected')),
    actualSpendToDate: num(pick(o, 'actualSpendToDate', 'actual')),
    pacingPercent: num(pick(o, 'pacingPercent', 'percent')),
    status: normalizePacingStatus(pick(o, 'status', 'pacingStatus')),
    deviation: num(pick(o, 'deviation')),
    remainingBudget: num(pick(o, 'remainingBudget')),
    suggestedDailyBudget: num(pick(o, 'suggestedDailyBudget')),
  }
}

export function normalizeTrendRow(o: Record<string, unknown>): TrendRow {
  return {
    date: str(pick(o, 'date', 'day')),
    spend: num(pick(o, 'spend', 'cost')),
    revenue: num(pick(o, 'revenue')),
    impressions: pick<number>(o, 'impressions'),
    clicks: num(pick(o, 'clicks')),
    conversions: num(pick(o, 'conversions')),
    budget: pick<number>(o, 'budget', 'expectedSpend', 'plannedSpend'),
  }
}

function asList(v: unknown): Record<string, unknown>[] {
  if (Array.isArray(v)) return v as Record<string, unknown>[]
  if (v && typeof v === 'object') {
    const o = v as Record<string, unknown>
    const inner = pick(o, 'items', 'rows', 'campaigns', 'data', 'results')
    if (Array.isArray(inner)) return inner as Record<string, unknown>[]
  }
  return []
}

/* ------------------------------- endpoints ------------------------------ */
export const api = {
  health: () => request<unknown>('/health'),

  createTenant: async (name: string, email: string): Promise<TenantCreated> => {
    const o = await request<Record<string, unknown>>('/tenants', { method: 'POST', body: { name, email } })
    const t = pick<Record<string, unknown>>(o, 'tenant') ?? o
    return {
      id: pick<string>(t, 'id'),
      slug: str(pick(t, 'slug') ?? pick(o, 'slug')),
      name: str(pick(t, 'name'), name),
      email: pick<string>(t, 'email') ?? email,
      currency: pick<string>(t, 'currency'),
      createdUtc: pick<string>(t, 'createdUtc', 'createdAt'),
      apiKey: str(pick(o, 'apiKey', 'key') ?? pick(t, 'apiKey', 'key')),
    }
  },

  me: async (key: string): Promise<Tenant> => {
    const o = await request<Record<string, unknown>>('/tenants/me', { key })
    return {
      id: pick<string>(o, 'id'),
      slug: str(pick(o, 'slug')),
      name: str(pick(o, 'name')),
      email: pick<string>(o, 'email'),
      currency: pick<string>(o, 'currency'),
      createdUtc: pick<string>(o, 'createdUtc', 'createdAt'),
    }
  },

  campaigns: async (key: string, q?: { status?: string; channel?: string }) =>
    asList(await request('/campaigns', { key, query: q })).map(normalizeCampaign),

  campaignSummary: async (key: string, asOf?: string) =>
    asList(await request('/campaigns/summary', { key, query: { asOf } })).map(normalizeSummary),

  campaign: async (key: string, id: string) => normalizeCampaign(await request('/campaigns/' + id, { key })),

  campaignMetrics: async (key: string, id: string, q?: { from?: string; to?: string }): Promise<MetricRow[]> =>
    asList(await request('/campaigns/' + id + '/metrics', { key, query: q })).map((o) => ({
      date: str(pick(o, 'date')),
      impressions: num(pick(o, 'impressions')),
      clicks: num(pick(o, 'clicks')),
      conversions: num(pick(o, 'conversions')),
      spend: num(pick(o, 'spend', 'cost')),
      revenue: num(pick(o, 'revenue')),
    })),

  campaignPacing: async (key: string, id: string) => normalizePacing(await request('/campaigns/' + id + '/pacing', { key })),

  transition: async (key: string, id: string, action: 'activate' | 'pause' | 'complete') =>
    normalizeCampaign(await request('/campaigns/' + id + '/' + action, { key, method: 'POST' })),

  deleteCampaign: (key: string, id: string) => request<void>('/campaigns/' + id, { key, method: 'DELETE' }),

  reportSummary: async (key: string, q: { from?: string; to?: string }): Promise<KpiRow> => {
    const o = await request<Record<string, unknown>>('/reports/summary', { key, query: q })
    const total = pick<Record<string, unknown>>(o, 'total', 'current', 'totals', 'window') ?? o
    return normalizeKpiRow(total)
  },

  reportByChannel: async (key: string, q: { from?: string; to?: string }): Promise<ByChannelReport> => {
    const o = await request<unknown>('/reports/cross-channel', { key, query: q })
    const obj = (Array.isArray(o) ? { channels: o } : (o as Record<string, unknown>)) ?? {}
    const channels = asList(pick(obj, 'channels', 'rows', 'items') ?? obj).map(normalizeKpiRow)
    const totalRaw = pick<Record<string, unknown>>(obj, 'total')
    const total = totalRaw
      ? normalizeKpiRow(totalRaw)
      : normalizeKpiRow({
          channel: 'Total',
          campaigns: channels.reduce((a, c) => a + c.campaigns, 0),
          impressions: channels.reduce((a, c) => a + c.impressions, 0),
          clicks: channels.reduce((a, c) => a + c.clicks, 0),
          conversions: channels.reduce((a, c) => a + c.conversions, 0),
          spend: channels.reduce((a, c) => a + c.spend, 0),
          revenue: channels.reduce((a, c) => a + c.revenue, 0),
        })
    return { from: str(pick(obj, 'from'), q.from ?? ''), to: str(pick(obj, 'to'), q.to ?? ''), channels, total }
  },

  reportTrend: async (key: string, q: { from?: string; to?: string }): Promise<TrendRow[]> =>
    asList(await request('/reports/daily', { key, query: q })).map(normalizeTrendRow),

  /**
   * Pacing for every non-completed campaign. The API exposes pacing per campaign (`/campaigns/{id}/pacing`), so this
   * fans out from `/campaigns/summary`; a campaign whose pacing call fails is simply left out.
   */
  reportPacing: async (key: string, asOf?: string): Promise<Pacing[]> => {
    const campaigns = asList(await request('/campaigns/summary', { key, query: { asOf } })).map(normalizeSummary)
    const live = campaigns.filter((c) => c.status !== 'Completed')
    const rows = await Promise.all(
      live.map(async (c): Promise<Pacing | null> => {
        try {
          const p = normalizePacing(await request<Record<string, unknown>>('/campaigns/' + c.id + '/pacing', { key, query: { asOf } }))
          return { ...p, campaignName: p.campaignName ?? c.name, channel: p.channel ?? c.channel }
        } catch {
          return null
        }
      }),
    )
    return rows.filter((r): r is Pacing => r !== null)
  },

  /** Dry-run a CSV import: detected format, column mapping, first rows and warnings. Nothing is written. */
  previewCsv: async (key: string, file: File | Blob, opts: { fileName?: string; signal?: AbortSignal } = {}): Promise<ImportPreview> => {
    const form = new FormData()
    form.append('file', file, opts.fileName ?? (file instanceof File ? file.name : 'import.csv'))
    const o = await request<Record<string, unknown>>('/imports/csv', { key, method: 'POST', form, query: { dryRun: 'true' }, signal: opts.signal })
    return {
      format: str(pick(o, 'format', 'detectedFormat'), 'unknown'),
      mapping: pick<Record<string, string | null>>(o, 'mapping', 'columnMapping') ?? {},
      rows: asList(pick(o, 'rows', 'preview', 'sample')),
      warnings: (pick<unknown[]>(o, 'warnings') ?? []).map(String),
      rowCount: pick<number>(o, 'rowCount', 'totalRows'),
    }
  },

  /** Commit a CSV import. */
  commitCsv: async (key: string, file: File | Blob, opts: { fileName?: string; signal?: AbortSignal } = {}): Promise<ImportSummary> => {
    const form = new FormData()
    form.append('file', file, opts.fileName ?? (file instanceof File ? file.name : 'import.csv'))
    const o = await request<Record<string, unknown>>('/imports/csv', { key, method: 'POST', form, signal: opts.signal })
    return {
      ...o,
      importId: pick<string>(o, 'importId', 'id'),
      format: pick<string>(o, 'format'),
      rowsRead: pick<number>(o, 'rowsRead', 'rows', 'totalRows'),
      inserted: pick<number>(o, 'inserted', 'rowsInserted', 'created'),
      updated: pick<number>(o, 'updated', 'rowsUpdated'),
      skipped: pick<number>(o, 'skipped', 'rowsSkipped'),
      campaignsCreated: pick<number>(o, 'campaignsCreated', 'newCampaigns'),
      warnings: (pick<unknown[]>(o, 'warnings') ?? []).map(String),
    }
  },

  /** Returns the CSV body of the API-provided real public dataset plus any attribution headers. */
  sampleDataset: async (key: string): Promise<{ csv: string; source?: string; license?: string; fileName: string }> => {
    const url = buildUrl('/datasets/sample')
    const res = await fetch(url, { headers: baseHeaders(key, { Accept: 'text/csv, */*;q=0.5' }) })
    if (!res.ok) {
      let problem: ApiProblem | undefined
      try { problem = await res.json() } catch { /* noop */ }
      throw new ApiError(res.status === 401 ? 'unauthorized' : res.status === 404 ? 'not-found' : 'http', problem?.detail ?? `${res.status} ${res.statusText}`, { status: res.status, problem, url })
    }
    const csv = await res.text()
    const cd = res.headers.get('content-disposition') ?? ''
    const m = /filename\*?="?([^";]+)"?/i.exec(cd)
    return {
      csv,
      source: res.headers.get('x-dataset-source') ?? res.headers.get('x-source') ?? undefined,
      license: res.headers.get('x-dataset-license') ?? undefined,
      fileName: m?.[1] ?? 'sample-dataset.csv',
    }
  },
}

export type Api = typeof api
