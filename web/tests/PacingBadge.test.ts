import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PacingBadge from '../src/components/PacingBadge.vue'
import KpiCard from '../src/components/KpiCard.vue'

describe('PacingBadge', () => {
  it('renders label, signed % and expected vs actual', () => {
    const w = mount(PacingBadge, { props: { status: 'over', percent: 118, expected: 1000, actual: 1180, detail: true } })
    expect(w.text()).toContain('Over')
    expect(w.text()).toContain('+18%')
    expect(w.text()).toContain('$1,180')
    expect(w.text()).toContain('of $1,000 expected')
    expect(w.find('.sr-only').text()).toMatch(/Over pace: 118% of expected spend/)
  })
  it('renders not-started for drafts without a percentage', () => {
    const w = mount(PacingBadge, { props: { percent: 140, status: 'over', campaignStatus: 'Draft' } })
    expect(w.text()).toContain('Not started')
    expect(w.text()).not.toContain('%')
  })
})

describe('KpiCard', () => {
  it('shows arrow + signed delta and never relies on colour alone', () => {
    const w = mount(KpiCard, { props: { label: 'CPC', value: '$1.20', delta: -8.4, lowerIsBetter: true } })
    expect(w.text()).toContain('↓')
    expect(w.text()).toContain('−8.4%')
    expect(w.find('.delta').classes()).toContain('good')
    expect(w.find('.sr-only').text()).toBe('improved')
  })
  it('falls back to a sub label when there is no previous period', () => {
    const w = mount(KpiCard, { props: { label: 'Spend', value: '$0', delta: null } })
    expect(w.text()).toContain('no previous period')
  })
  it('renders skeletons while loading', () => {
    const w = mount(KpiCard, { props: { label: 'Spend', value: '', loading: true } })
    expect(w.findAll('.sk').length).toBeGreaterThan(0)
    expect(w.attributes('aria-busy')).toBe('true')
  })
})
