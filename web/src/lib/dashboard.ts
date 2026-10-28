import { api, ApiError } from './api'
import { addDays, isoDay, pctChange, todayIso } from './format'
import { optional } from './useAsync'
import type { ByChannelReport, Campaign, CampaignSummary, KpiRow, Pacing, TrendRow } from './types'

export type PeriodDays = 7 | 30 | 90

export interface Period {
  from: string
  to: string
  prevFrom: string
  prevTo: string
  days: PeriodDays
}

export function periodFor(days: PeriodDays, today = todayIso()): Period {
  const to = today
  const from = addDays(to, -(days - 1))
  const prevTo = addDays(from, -1)
  const prevFrom = addDays(prevTo, -(days - 1))
  return { from, to, prevFrom, prevTo, days }
}

export interface KpiDelta {
  value: number
  delta: number | null
}

export interface DashboardData {
  period: Period
  campaigns: CampaignSummary[]
  current: KpiRow
  previous: KpiRow | null
  byChannel: ByChannelReport
  trend: TrendRow[]
  pacing: Pacing[]
  utilisation: { actual: number; expected: number; percent: number | null }
  kpis: {
    spend: KpiDelta
    impressions: KpiDelta
    clicks: KpiDelta
    ctr: KpiDelta
    cpc: KpiDelta
    conversions: KpiDelta
    cpa: KpiDelta
  }
  sparks: { spend: number[]; clicks: number[]; conversions: number[]; impressions: number[] }
}

const notFound = (e: unknown) => e instanceof ApiError && e.kind === 'not-found'

/** Planned spend per day = sum of daily budgets of non-draft campaigns in flight that day. */
export function plannedBudgetByDay(campaigns: Campaign[], dates: string[]): number[] {
  const live = campaigns.filter((c) => c.status !== 'Draft' && c.startDate && c.endDate)
  return dates.map((d) => live.reduce((sum, c) => (c.startDate <= d && d <= c.endDate ? sum + c.dailyBudget : sum), 0))
}

/** Fill missing dates in a trend series with zero rows so the x-axis is continuous. */
export function fillTrend(rows: TrendRow[], from: string, to: string): TrendRow[] {
  const byDate = new Map(rows.map((r) => [r.date.slice(0, 10), r]))
  const out: TrendRow[] = []
  for (let d = from; d <= to; d = addDays(d, 1)) {
    out.push(byDate.get(d) ?? { date: d, spend: 0, revenue: 0, clicks: 0, conversions: 0, impressions: 0 })
    if (out.length > 400) break
  }
  return out
}

export function computeUtilisation(pacing: Pacing[], campaigns: CampaignSummary[]) {
  let actual = 0
  let expected = 0
  if (pacing.length) {
    for (const p of pacing) {
      actual += p.actualSpendToDate
      expected += p.expectedSpendToDate
    }
  } else {
    for (const c of campaigns) {
      if (c.expectedSpendToDate !== undefined && c.actualSpendToDate !== undefined) {
        actual += c.actualSpendToDate
        expected += c.expectedSpendToDate
      }
    }
  }
  return { actual, expected, percent: expected > 0 ? (actual / expected) * 100 : null }
}

export async function loadDashboard(key: string, days: PeriodDays): Promise<DashboardData> {
  const period = periodFor(days)
  const cur = { from: period.from, to: period.to }
  const prev = { from: period.prevFrom, to: period.prevTo }

  const [campaigns, byChannel, trendRaw, pacing, summaryCur, summaryPrev] = await Promise.all([
    api.campaignSummary(key),
    api.reportByChannel(key, cur),
    optional(api.reportTrend(key, cur), [] as TrendRow[], notFound),
    optional(api.reportPacing(key), [] as Pacing[], notFound),
    optional(api.reportSummary(key, cur), null as KpiRow | null, notFound),
    optional(api.reportSummary(key, prev), null as KpiRow | null, notFound),
  ])

  const current = summaryCur ?? byChannel.total
  let previous = summaryPrev
  if (!previous) previous = await optional(api.reportByChannel(key, prev).then((r) => r.total), null as KpiRow | null, () => true)

  const trend = fillTrend(trendRaw, period.from, period.to)
  if (!trend.some((r) => typeof r.budget === 'number')) {
    const planned = plannedBudgetByDay(campaigns, trend.map((r) => r.date))
    trend.forEach((r, i) => (r.budget = planned[i]))
  }

  const d = (c: number, p: number | undefined) => ({ value: c, delta: previous ? pctChange(c, p) : null })
  const hasPrevData = previous && (previous.spend > 0 || previous.impressions > 0 || previous.clicks > 0)
  const prevOrNull = hasPrevData ? previous : null

  return {
    period,
    campaigns,
    current,
    previous: prevOrNull,
    byChannel,
    trend,
    pacing,
    utilisation: computeUtilisation(pacing, campaigns),
    kpis: {
      spend: d(current.spend, prevOrNull?.spend),
      impressions: d(current.impressions, prevOrNull?.impressions),
      clicks: d(current.clicks, prevOrNull?.clicks),
      ctr: d(current.ctr, prevOrNull?.ctr),
      cpc: d(current.cpc, prevOrNull?.cpc),
      conversions: d(current.conversions, prevOrNull?.conversions),
      cpa: d(current.cpa, prevOrNull?.cpa),
    },
    sparks: {
      spend: trend.map((r) => r.spend),
      clicks: trend.map((r) => r.clicks),
      conversions: trend.map((r) => r.conversions),
      impressions: trend.map((r) => r.impressions ?? 0),
    },
  }
}

export { isoDay }
