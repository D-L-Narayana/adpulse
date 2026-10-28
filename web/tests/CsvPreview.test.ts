import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import CsvPreview from '../src/components/CsvPreview.vue'

const preview = {
  format: 'google-ads',
  mapping: { campaign: 'Campaign', spend: 'Cost', impressions: 'Impressions', clicks: 'Clicks', conversions: 'Conversions', date: 'Day', revenue: null },
  rows: [
    { campaign: 'Brand search', date: '2025-03-01', spend: 236.4, impressions: 12840, clicks: 412, conversions: 19 },
    { campaign: 'Generic', date: '2025-03-01', spend: 90, impressions: 5000, clicks: 100, conversions: 2 },
  ],
  warnings: ['Row 14: Cost is empty, treated as 0'],
  rowCount: 1480,
}

describe('CsvPreview', () => {
  it('shows the detected format, row count, mapping and first rows', () => {
    const w = mount(CsvPreview, { props: { preview, fileName: 'export.csv' } })
    expect(w.get('[data-testid=format]').text()).toContain('Google Ads export')
    expect(w.get('[data-testid=format]').text()).toContain('export.csv')
    expect(w.text()).toContain('1,480 rows')
    const mapping = w.get('[data-testid=mapping]')
    expect(mapping.text()).toContain('Campaign')
    expect(mapping.text()).toContain('not found')
    expect(w.text()).toContain('6 of 7 mapped')
    const rows = w.get('[data-testid=rows]').findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain('Brand search')
    expect(rows[0].text()).toContain('12,840')
  })
  it('lists warnings in an alert region', () => {
    const w = mount(CsvPreview, { props: { preview } })
    const warn = w.get('[data-testid=warnings]')
    expect(warn.attributes('role')).toBe('alert')
    expect(warn.text()).toContain('1 warning')
    expect(warn.text()).toContain('Row 14')
  })
  it('labels Meta exports and unknown formats', () => {
    expect(mount(CsvPreview, { props: { preview: { ...preview, format: 'meta-ads-manager' } } }).get('[data-testid=format]').text()).toContain('Meta Ads Manager export')
    expect(mount(CsvPreview, { props: { preview: { ...preview, format: 'unknown', warnings: [] } } }).get('[data-testid=format]').text()).toContain('Unrecognised format')
  })
  it('caps the preview at 10 rows and handles no rows', () => {
    const many = { ...preview, rows: Array.from({ length: 25 }, (_, i) => ({ campaign: `c${i}`, spend: i })) }
    expect(mount(CsvPreview, { props: { preview: many } }).get('[data-testid=rows]').findAll('tbody tr')).toHaveLength(10)
    const none = mount(CsvPreview, { props: { preview: { ...preview, rows: [] } } })
    expect(none.text()).toContain('did not return any parsed rows')
  })
})
