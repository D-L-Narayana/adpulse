const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })
const money2 = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 })
const int = new Intl.NumberFormat('en-US')

export const fmtMoney = (v: number) => money.format(v)
export const fmtMoney2 = (v: number) => money2.format(v)
export const fmtInt = (v: number) => int.format(v)
export const fmtPct = (v: number, digits = 2) => `${v.toFixed(digits)}%`
export const fmtX = (v: number) => `${v.toFixed(2)}x`
export const fmtCompact = (v: number) =>
  v >= 1_000_000 ? `${(v / 1_000_000).toFixed(1)}M` : v >= 1_000 ? `${(v / 1_000).toFixed(1)}k` : `${Math.round(v)}`
export const fmtDate = (iso: string) =>
  new Date(iso + 'T00:00:00Z').toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })

export const channelLabel: Record<string, string> = {
  GoogleAds: 'Google Ads',
  Meta: 'Meta',
  Amazon: 'Amazon Ads',
  LinkedIn: 'LinkedIn',
  DV360: 'DV360',
  Total: 'All channels',
}
