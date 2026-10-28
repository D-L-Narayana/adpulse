let currentCurrency = 'USD'
const cache = new Map<string, Intl.NumberFormat>()

export function setCurrency(code?: string) {
  if (code && /^[A-Z]{3}$/i.test(code)) currentCurrency = code.toUpperCase()
}
export function currency() {
  return currentCurrency
}

function nf(key: string, opts: Intl.NumberFormatOptions) {
  const k = key + currentCurrency
  let f = cache.get(k)
  if (!f) {
    f = new Intl.NumberFormat('en-US', opts)
    cache.set(k, f)
  }
  return f
}

const safe = (v: unknown): number => (typeof v === 'number' && Number.isFinite(v) ? v : 0)

/** Whole-unit money, e.g. $12,340 */
export const fmtMoney = (v: unknown) => nf('m0', { style: 'currency', currency: currentCurrency, maximumFractionDigits: 0 }).format(safe(v))
/** Money with cents, e.g. $1.23 */
export const fmtMoney2 = (v: unknown) => nf('m2', { style: 'currency', currency: currentCurrency, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(safe(v))
/** Compact money for axes/tooltips, e.g. $12.3k */
export const fmtMoneyCompact = (v: unknown) => nf('mc', { style: 'currency', currency: currentCurrency, notation: 'compact', maximumFractionDigits: 1 }).format(safe(v))
export const fmtInt = (v: unknown) => nf('i', { maximumFractionDigits: 0 }).format(safe(v))
export const fmtCompact = (v: unknown) => nf('c', { notation: 'compact', maximumFractionDigits: 1 }).format(safe(v))
export const fmtPct = (v: unknown, digits = 2) => `${safe(v).toFixed(digits)}%`
export const fmtX = (v: unknown) => `${safe(v).toFixed(2)}x`

/** Signed percentage with explicit sign: +4.2% / −3.0% / 0.0% */
export const fmtSignedPct = (v: unknown, digits = 1) => {
  const n = safe(v)
  const sign = n > 0 ? '+' : n < 0 ? '−' : ''
  return `${sign}${Math.abs(n).toFixed(digits)}%`
}

/** Relative change in percent between previous and current; null when previous is 0/absent. */
export function pctChange(current: number, previous: number | undefined | null): number | null {
  if (previous === undefined || previous === null || !Number.isFinite(previous) || previous === 0) return null
  return ((current - previous) / Math.abs(previous)) * 100
}

export const fmtDate = (iso: string | Date | undefined, opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' }) => {
  if (!iso) return '—'
  const d = typeof iso === 'string' ? new Date(iso.length === 10 ? iso + 'T00:00:00Z' : iso) : iso
  if (Number.isNaN(d.getTime())) return String(iso)
  return d.toLocaleDateString('en-US', { timeZone: 'UTC', ...opts })
}
export const fmtDateLong = (iso: string | undefined) => fmtDate(iso, { month: 'short', day: 'numeric', year: 'numeric' })
export const fmtDateTime = (iso: string | number | Date | undefined) => {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return String(iso)
  return d.toLocaleString('en-US', { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
}

/** "3s ago", "2m ago", "1h ago" */
export function fmtAgo(ms: number, now = Date.now()): string {
  const s = Math.max(0, Math.round((now - ms) / 1000))
  if (s < 60) return `${s}s ago`
  const m = Math.round(s / 60)
  if (m < 60) return `${m}m ago`
  const h = Math.round(m / 60)
  if (h < 48) return `${h}h ago`
  return `${Math.round(h / 24)}d ago`
}

export function isoDay(d: Date): string {
  return d.toISOString().slice(0, 10)
}
export function addDays(iso: string, n: number): string {
  const d = new Date(iso + 'T00:00:00Z')
  d.setUTCDate(d.getUTCDate() + n)
  return isoDay(d)
}
export function todayIso(): string {
  return isoDay(new Date())
}

/** Mask an API key for display: first 6 and last 4 chars. */
export function maskKey(key: string | null | undefined): string {
  if (!key) return '—'
  if (key.length <= 12) return key.slice(0, 3) + '•'.repeat(Math.max(4, key.length - 3))
  return `${key.slice(0, 6)}…${key.slice(-4)}`
}

export const channelLabel = (c: string | undefined): string =>
  ({ GoogleAds: 'Google Ads', Meta: 'Meta', Amazon: 'Amazon Ads', LinkedIn: 'LinkedIn', DV360: 'DV360', Total: 'All channels' } as Record<string, string>)[c ?? ''] ?? (c || 'Other')

export const statusLabel = (s: string | undefined): string => s ?? 'Draft'
