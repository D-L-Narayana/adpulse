import type { PacingStatus } from './types'

/** Mirrors the API's Pacing engine: linear expectation, ±10 % healthy band. */
export const BAND_PERCENT = 10
export const UNDER_THRESHOLD = 100 - BAND_PERCENT
export const OVER_THRESHOLD = 100 + BAND_PERCENT

export interface PacingInput {
  /** percent of expected spend-to-date actually spent (100 = exactly on pace) */
  pacingPercent: number
  expectedSpendToDate?: number
  actualSpendToDate?: number
  status?: PacingStatus
  campaignStatus?: string
}

export type PacingTone = 'ok' | 'warn' | 'bad' | 'neutral'

export interface PacingView {
  status: PacingStatus
  tone: PacingTone
  /** short label, e.g. "On pace", "Over", "Under", "Not started" */
  label: string
  /** signed deviation from 100 %, e.g. "+12%" / "−7%" (empty for not-started) */
  deltaLabel: string
  /** full text for title/aria, e.g. "Over pace: 112% of expected spend (expected 1,000 · actual 1,120)" */
  description: string
  /** 0..1 position for a bar scaled 0..200 % */
  barFraction: number
  deviationPercent: number
}

export function classifyPacing(percent: number, daysElapsed?: number): PacingStatus {
  if (daysElapsed === 0) return 'not-started'
  if (!Number.isFinite(percent)) return 'not-started'
  if (percent < UNDER_THRESHOLD) return 'under'
  if (percent > OVER_THRESHOLD) return 'over'
  return 'on-track'
}

export function pacingTone(status: PacingStatus): PacingTone {
  switch (status) {
    case 'on-track':
      return 'ok'
    case 'under':
      return 'warn'
    case 'over':
      return 'bad'
    default:
      return 'neutral'
  }
}

export function pacingLabel(status: PacingStatus): string {
  switch (status) {
    case 'on-track':
      return 'On pace'
    case 'under':
      return 'Under'
    case 'over':
      return 'Over'
    default:
      return 'Not started'
  }
}

function signed(n: number, digits = 0): string {
  const r = Math.abs(n).toFixed(digits)
  return n > 0 ? `+${r}%` : n < 0 ? `−${r}%` : `±0%`
}

export function pacingView(input: PacingInput, fmtMoney: (v: number) => string = (v) => v.toFixed(0)): PacingView {
  const percent = Number.isFinite(input.pacingPercent) ? input.pacingPercent : 0
  let status = input.status ?? classifyPacing(percent)
  if (input.campaignStatus === 'Draft') status = 'not-started'
  const tone = pacingTone(status)
  const label = pacingLabel(status)
  const deviation = percent - 100
  const deltaLabel = status === 'not-started' ? '' : signed(deviation)
  const hasAmounts = input.expectedSpendToDate !== undefined && input.actualSpendToDate !== undefined
  const amounts = hasAmounts ? ` (expected ${fmtMoney(input.expectedSpendToDate!)} · actual ${fmtMoney(input.actualSpendToDate!)})` : ''
  const description =
    status === 'not-started'
      ? 'Not started: no spend expected yet'
      : `${label} pace: ${percent.toFixed(0)}% of expected spend to date${amounts}`
  return {
    status,
    tone,
    label,
    deltaLabel,
    description,
    barFraction: Math.max(0, Math.min(1, percent / 200)),
    deviationPercent: deviation,
  }
}

/**
 * Linear projection of end-of-flight spend based on the current run-rate.
 * Returns projected total spend and the planned total budget.
 */
export function projectFlight(p: { dailyBudget: number; daysElapsed: number; daysTotal: number; actualSpendToDate: number }) {
  const totalBudget = p.dailyBudget * p.daysTotal
  const daysLeft = Math.max(0, p.daysTotal - p.daysElapsed)
  const runRate = p.daysElapsed > 0 ? p.actualSpendToDate / p.daysElapsed : 0
  const projected = p.actualSpendToDate + runRate * daysLeft
  const remaining = Math.max(0, totalBudget - p.actualSpendToDate)
  const suggested = daysLeft > 0 ? remaining / daysLeft : 0
  return { totalBudget, daysLeft, runRate, projected, remaining, suggested, projectedPercent: totalBudget ? (projected / totalBudget) * 100 : 0 }
}
