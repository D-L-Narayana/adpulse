import { reactive, readonly } from 'vue'
import { normalizePacingStatus, pick } from '../lib/api'
import type { PacingAlert, StreamEvent } from '../lib/types'

const STORAGE_PREFIX = 'adpulse.alerts:'
const SEEN_PREFIX = 'adpulse.alertsSeenAt:'
const MAX_ALERTS = 200

interface AlertsState {
  slug: string | null
  items: PacingAlert[]
  seenAt: number
}

const state = reactive<AlertsState>({ slug: null, items: [], seenAt: 0 })

function storage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

function load(slug: string) {
  if (state.slug === slug) return
  state.slug = slug
  try {
    const raw = storage()?.getItem(STORAGE_PREFIX + slug)
    state.items = raw ? (JSON.parse(raw) as PacingAlert[]) : []
    state.seenAt = Number(storage()?.getItem(SEEN_PREFIX + slug) ?? 0)
  } catch {
    state.items = []
  }
}

function persist() {
  if (!state.slug) return
  storage()?.setItem(STORAGE_PREFIX + state.slug, JSON.stringify(state.items.slice(0, MAX_ALERTS)))
}

export function alertFromEvent(e: StreamEvent): PacingAlert {
  const d = e.data
  const pacing = pick<Record<string, unknown>>(d, 'pacing') ?? d
  const n = (v: unknown) => (typeof v === 'number' ? v : typeof v === 'string' && v !== '' ? Number(v) : undefined)
  return {
    id: e.id ?? `${e.receivedAt}-${Math.random().toString(36).slice(2, 8)}`,
    at: (pick<string>(d, 'at', 'timestamp', 'occurredUtc', 'asOf') ?? new Date(e.receivedAt).toISOString()),
    campaignId: pick<string>(d, 'campaignId') ?? pick<string>(pacing, 'campaignId'),
    campaignName: pick<string>(d, 'campaignName', 'name') ?? pick<string>(pacing, 'campaignName'),
    channel: pick<string>(d, 'channel') ?? pick<string>(pacing, 'channel'),
    status: normalizePacingStatus(pick(d, 'status', 'pacingStatus') ?? pick(pacing, 'status')),
    pacingPercent: n(pick(d, 'pacingPercent') ?? pick(pacing, 'pacingPercent')),
    expectedSpendToDate: n(pick(d, 'expectedSpendToDate') ?? pick(pacing, 'expectedSpendToDate')),
    actualSpendToDate: n(pick(d, 'actualSpendToDate') ?? pick(pacing, 'actualSpendToDate')),
    message: pick<string>(d, 'message', 'detail'),
  }
}

export function recordAlert(slug: string, e: StreamEvent) {
  load(slug)
  const a = alertFromEvent(e)
  if (state.items.some((x) => x.id === a.id)) return
  state.items.unshift(a)
  if (state.items.length > MAX_ALERTS) state.items.length = MAX_ALERTS
  persist()
}

export function markAlertsSeen(slug: string) {
  load(slug)
  state.seenAt = Date.now()
  storage()?.setItem(SEEN_PREFIX + slug, String(state.seenAt))
}

export function clearAlerts(slug: string) {
  load(slug)
  state.items = []
  persist()
}

export function useAlerts(slug: string) {
  load(slug)
  return readonly(state)
}

export function unreadCount(): number {
  return state.items.filter((a) => new Date(a.at).getTime() > state.seenAt).length
}

export function _resetAlerts() {
  state.slug = null
  state.items = []
  state.seenAt = 0
}
