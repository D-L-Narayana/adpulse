export type Channel = 'GoogleAds' | 'Meta' | 'Amazon' | 'LinkedIn' | 'DV360'
export type CampaignStatus = 'Draft' | 'Active' | 'Paused' | 'Completed'
export type PacingStatus = 'not-started' | 'under' | 'on-track' | 'over'

export interface CampaignSummary {
  id: string
  name: string
  channel: Channel
  status: CampaignStatus
  dailyBudget: number
  startDate: string
  endDate: string
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

export interface CrossChannelReport {
  from: string
  to: string
  channels: KpiRow[]
  total: KpiRow
}

export interface DailyTrendRow {
  date: string
  spend: number
  revenue: number
  clicks: number
  conversions: number
}

export interface Me {
  id: string
  name: string
}

export interface Dashboard {
  me: Me
  summary: CampaignSummary[]
  report: CrossChannelReport
  daily: DailyTrendRow[]
}

export type DataSource = { kind: 'live'; baseUrl: string } | { kind: 'demo'; reason: string }
