import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { niceTicks, pickIndices, linePath } from '../src/lib/chart'
import PacingChart from '../src/components/PacingChart.vue'

describe('chart helpers', () => {
  it('produces nice ticks covering the max', () => {
    expect(niceTicks(930, 4)).toEqual([0, 250, 500, 750, 1000])
    expect(niceTicks(0)).toEqual([0, 1])
  })
  it('picks evenly spaced label indices including both ends', () => {
    expect(pickIndices(30, 4)).toEqual([0, 10, 19, 29])
    expect(pickIndices(3, 10)).toEqual([0, 1, 2])
  })
  it('skips null points when drawing', () => {
    expect(linePath([[0, 0], null, [10, 10], [20, 5]])).toBe('M0.0,0.0M10.0,10.0L20.0,5.0')
  })
})

describe('PacingChart', () => {
  const rows = [
    { date: '2025-03-01', spend: 100, revenue: 0, clicks: 0, conversions: 0, budget: 120 },
    { date: '2025-03-02', spend: 100, revenue: 0, clicks: 0, conversions: 0, budget: 120 },
    { date: '2025-03-03', spend: 100, revenue: 0, clicks: 0, conversions: 0, budget: 120 },
  ]
  it('states the insight and exposes an accessible data table', async () => {
    const w = mount(PacingChart, { props: { rows } })
    expect(w.text()).toContain('Spend is 17% under plan')
    const svg = w.get('svg')
    expect(svg.attributes('role')).toBe('img')
    expect(svg.attributes('aria-label')).toContain('Line chart')
    expect(w.text()).toContain('Spend')
    expect(w.text()).toContain('Planned')
    await w.get('button.toggle').trigger('click')
    const trs = w.findAll('table.data tbody tr')
    expect(trs).toHaveLength(3)
    expect(trs[2].text()).toContain('$300')
    expect(trs[2].text()).toContain('$360')
  })
  it('shows an empty message when there are no rows', () => {
    const w = mount(PacingChart, { props: { rows: [] } })
    expect(w.text()).toContain('No daily rows')
    expect(w.find('svg').exists()).toBe(false)
  })
})
