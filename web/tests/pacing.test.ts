import { describe, expect, it } from 'vitest'
import { classifyPacing, pacingLabel, pacingTone, pacingView, projectFlight, OVER_THRESHOLD, UNDER_THRESHOLD } from '../src/lib/pacing'

describe('classifyPacing', () => {
  it('uses the ±10% band exactly like the API', () => {
    expect(UNDER_THRESHOLD).toBe(90)
    expect(OVER_THRESHOLD).toBe(110)
    expect(classifyPacing(89.99)).toBe('under')
    expect(classifyPacing(90)).toBe('on-track')
    expect(classifyPacing(100)).toBe('on-track')
    expect(classifyPacing(110)).toBe('on-track')
    expect(classifyPacing(110.01)).toBe('over')
  })
  it('is not-started before day one or for non-finite input', () => {
    expect(classifyPacing(100, 0)).toBe('not-started')
    expect(classifyPacing(NaN)).toBe('not-started')
  })
})

describe('pacingView', () => {
  it('renders an over-pace badge with signed delta and amounts', () => {
    const v = pacingView({ pacingPercent: 123.4, expectedSpendToDate: 1000, actualSpendToDate: 1234 }, (n) => `$${n}`)
    expect(v.status).toBe('over')
    expect(v.tone).toBe('bad')
    expect(v.label).toBe('Over')
    expect(v.deltaLabel).toBe('+23%')
    expect(v.description).toContain('123% of expected spend')
    expect(v.description).toContain('expected $1000 · actual $1234')
    expect(v.barFraction).toBeCloseTo(0.617, 2)
  })
  it('renders under-pace with a minus sign (never colour alone)', () => {
    const v = pacingView({ pacingPercent: 72 })
    expect(v.label).toBe('Under')
    expect(v.deltaLabel).toBe('−28%')
    expect(v.tone).toBe('warn')
  })
  it('shows ±0% when exactly on plan', () => {
    expect(pacingView({ pacingPercent: 100 }).deltaLabel).toBe('±0%')
  })
  it('prefers the API status when provided and forces not-started for drafts', () => {
    expect(pacingView({ pacingPercent: 150, status: 'on-track' }).status).toBe('on-track')
    const draft = pacingView({ pacingPercent: 150, status: 'over', campaignStatus: 'Draft' })
    expect(draft.status).toBe('not-started')
    expect(draft.deltaLabel).toBe('')
    expect(draft.tone).toBe('neutral')
  })
  it('maps tones and labels for every status', () => {
    expect(pacingTone('on-track')).toBe('ok')
    expect(pacingTone('under')).toBe('warn')
    expect(pacingTone('over')).toBe('bad')
    expect(pacingTone('not-started')).toBe('neutral')
    expect(pacingLabel('on-track')).toBe('On pace')
    expect(pacingLabel('not-started')).toBe('Not started')
  })
})

describe('projectFlight', () => {
  it('projects end-of-flight spend from the current run-rate', () => {
    const p = projectFlight({ dailyBudget: 100, daysElapsed: 10, daysTotal: 30, actualSpendToDate: 1200 })
    expect(p.totalBudget).toBe(3000)
    expect(p.daysLeft).toBe(20)
    expect(p.runRate).toBe(120)
    expect(p.projected).toBe(3600)
    expect(p.projectedPercent).toBeCloseTo(120)
    expect(p.remaining).toBe(1800)
    expect(p.suggested).toBe(90)
  })
  it('handles the last day and over-spent flights without dividing by zero', () => {
    const p = projectFlight({ dailyBudget: 100, daysElapsed: 30, daysTotal: 30, actualSpendToDate: 3500 })
    expect(p.daysLeft).toBe(0)
    expect(p.suggested).toBe(0)
    expect(p.remaining).toBe(0)
    expect(p.projected).toBe(3500)
  })
})
