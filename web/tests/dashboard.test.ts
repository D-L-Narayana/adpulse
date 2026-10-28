import { describe, expect, it } from 'vitest'
import { computeUtilisation, fillTrend, periodFor, plannedBudgetByDay } from '../src/lib/dashboard'
import type { Campaign, Pacing } from '../src/lib/types'

const c = (o: Partial<Campaign>): Campaign => ({ id: 'x', name: 'x', channel: 'Meta', status: 'Active', dailyBudget: 100, startDate: '2025-01-01', endDate: '2025-01-31', ...o })

describe('dashboard helpers', () => {
  it('builds contiguous current + previous periods', () => {
    const p = periodFor(7, '2025-03-10')
    expect(p).toEqual({ from: '2025-03-04', to: '2025-03-10', prevFrom: '2025-02-25', prevTo: '2025-03-03', days: 7 })
  })
  it('sums daily budgets only for non-draft campaigns in flight', () => {
    const dates = ['2024-12-31', '2025-01-01', '2025-02-01']
    const planned = plannedBudgetByDay([c({}), c({ status: 'Draft', dailyBudget: 999 }), c({ dailyBudget: 50, startDate: '2025-01-01', endDate: '2025-01-01' })], dates)
    expect(planned).toEqual([0, 150, 0])
  })
  it('fills gaps with zero rows so the axis is continuous', () => {
    const t = fillTrend([{ date: '2025-01-02', spend: 5, revenue: 0, clicks: 1, conversions: 0 }], '2025-01-01', '2025-01-03')
    expect(t.map((r) => r.date)).toEqual(['2025-01-01', '2025-01-02', '2025-01-03'])
    expect(t[1].spend).toBe(5)
    expect(t[0].spend).toBe(0)
  })
  it('computes utilisation from pacing rows, null when nothing is expected yet', () => {
    const p = (a: number, e: number): Pacing => ({ campaignId: 'x', asOf: '', daysElapsed: 1, daysTotal: 2, dailyBudget: 1, expectedSpendToDate: e, actualSpendToDate: a, pacingPercent: 0, status: 'on-track', deviation: 0, remainingBudget: 0, suggestedDailyBudget: 0 })
    expect(computeUtilisation([p(50, 100), p(70, 100)], []).percent).toBe(60)
    expect(computeUtilisation([], []).percent).toBeNull()
  })
})
