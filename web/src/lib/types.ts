export type Channel = 'GoogleAds' | 'Meta' | 'Amazon' | 'LinkedIn' | 'DV360' | (string & {})
export type CampaignStatus = 'Draft' | 'Active' | 'Paused' | 'Completed'
export type PacingStatus = 'not-started' | 'under' | 'on-track' | 'over'

export interface Tenant {
  id?: string
  slug: string
  name: string
  email?: string
  currency?: string
  createdUtc?: string
}

export interface TenantCreated extends Tenant {
  apiKey: string
}

export interface Campaign {
  id: string
  name: string
  channel: Channel
  status: CampaignStatus
  dailyBudget: number
  startDate: string
  endDate: string
  createdUtc?: string
  updatedUtc?: string
}

export interface CampaignSummary extends Campaign {
  impressions: number
  clicks: number
  conversions: number
  spend: number
  revenue: number
  ctr: number
  cpc: number
  cpa: number
  roas: number
  pacingPercent: number
  pacingStatus: PacingStatus
  suggestedDailyBudget: number
  expectedSpendToDate?: number
  actualSpendToDate?: number
}

export interface KpiRow {
  channel: string
  campaigns: number
  impressions: number
  clicks: number
  conversions: number
  spend: number
  revenue: number
  ctr: number
  cpc: number
  cpa: number
  roas: number
}

export interface ByChannelReport {
  from: string
  to: string
  channels: KpiRow[]
  total: KpiRow
}

export interface TrendRow {
  date: string
  spend: number
  revenue: number
  impressions?: number
  clicks: number
  conversions: number
  budget?: number
}

export interface Pacing {
  campaignId: string
  asOf: string
  daysElapsed: number
  daysTotal: number
  dailyBudget: number
  expectedSpendToDate: number
  actualSpendToDate: number
  pacingPercent: number
  status: PacingStatus
  deviation: number
  remainingBudget: number
  suggestedDailyBudget: number
  campaignName?: string
  channel?: Channel
}

export interface MetricRow {
  date: string
  impressions: number
  clicks: number
  conversions: number
  spend: number
  revenue: number
}

export interface ImportPreview {
  format: string
  mapping: Record<string, string | null>
  rows: Record<string, unknown>[]
  warnings: string[]
  rowCount?: number
}

export interface ImportSummary {
  importId?: string
  format?: string
  rowsRead?: number
  inserted?: number
  updated?: number
  skipped?: number
  campaignsCreated?: number
  warnings?: string[]
  [k: string]: unknown
}

export type StreamEventType = 'metrics.upserted' | 'campaign.changed' | 'import.completed' | 'pacing.alert' | 'heartbeat' | (string & {})

export interface StreamEvent<T = Record<string, unknown>> {
  id?: string
  type: StreamEventType
  data: T
  receivedAt: number
}

export interface PacingAlert {
  id: string
  at: string
  campaignId?: string
  campaignName?: string
  channel?: string
  status: PacingStatus
  pacingPercent?: number
  expectedSpendToDate?: number
  actualSpendToDate?: number
  message?: string
}

export interface ApiProblem {
  type?: string
  title?: string
  status: number
  detail?: string
  traceId?: string
  errors?: Record<string, string[]>
}
