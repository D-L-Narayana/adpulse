import { beforeEach, describe, expect, it } from 'vitest'
import { addDays, currency, fmtAgo, fmtCompact, fmtDate, fmtMoney, fmtMoney2, fmtMoneyCompact, fmtSignedPct, maskKey, pctChange, setCurrency, channelLabel } from '../src/lib/format'

describe('format helpers', () => {
  beforeEach(() => setCurrency('USD'))

  it('formats money with and without cents', () => {
    expect(fmtMoney(12345.67)).toBe('$12,346')
    expect(fmtMoney2(1.5)).toBe('$1.50')
    expect(fmtMoneyCompact(12_300)).toBe('$12.3K')
  })
  it('switches currency at runtime but ignores garbage codes', () => {
    setCurrency('INR')
    expect(currency()).toBe('INR')
    expect(fmtMoney(10)).toContain('10')
    setCurrency('not-a-code')
    expect(currency()).toBe('INR')
  })
  it('is safe for NaN / undefined', () => {
    expect(fmtMoney(undefined)).toBe('$0')
    expect(fmtCompact(NaN)).toBe('0')
  })
  it('signs percentages explicitly with a real minus sign', () => {
    expect(fmtSignedPct(4.26)).toBe('+4.3%')
    expect(fmtSignedPct(-3)).toBe('−3.0%')
    expect(fmtSignedPct(0)).toBe('0.0%')
  })
  it('computes relative change and returns null when there is no baseline', () => {
    expect(pctChange(120, 100)).toBe(20)
    expect(pctChange(80, 100)).toBe(-20)
    expect(pctChange(10, 0)).toBeNull()
    expect(pctChange(10, undefined)).toBeNull()
  })
  it('formats ISO days in UTC without off-by-one', () => {
    expect(fmtDate('2025-03-01')).toBe('Mar 1')
    expect(addDays('2025-03-01', -1)).toBe('2025-02-28')
    expect(addDays('2024-02-28', 1)).toBe('2024-02-29')
  })
  it('renders relative time', () => {
    const now = 1_000_000
    expect(fmtAgo(now - 3000, now)).toBe('3s ago')
    expect(fmtAgo(now - 125_000, now)).toBe('2m ago')
    expect(fmtAgo(now - 3 * 3_600_000, now)).toBe('3h ago')
  })
  it('masks keys keeping a recognisable prefix/suffix', () => {
    expect(maskKey('adp_live_0123456789abcdef')).toBe('adp_li…cdef')
    expect(maskKey('short')).toBe('sho••••')
    expect(maskKey(null)).toBe('—')
  })
  it('labels channels', () => {
    expect(channelLabel('GoogleAds')).toBe('Google Ads')
    expect(channelLabel('Total')).toBe('All channels')
    expect(channelLabel('TikTok')).toBe('TikTok')
    expect(channelLabel(undefined)).toBe('Other')
  })
})
