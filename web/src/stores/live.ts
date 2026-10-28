import { onBeforeUnmount, reactive, readonly, watch, type WatchSource } from 'vue'
import { SseClient, type SseState } from '../lib/sse'
import type { StreamEvent } from '../lib/types'
import { recordAlert } from './alerts'

export type LiveMode = 'sse' | 'polling' | 'off'
export const POLL_INTERVAL_MS = 10_000

interface LiveState {
  mode: LiveMode
  sse: SseState
  detail?: string
  lastEventAt: number | null
  lastUpdatedAt: number | null
  eventsSeen: number
  key: string | null
  slug: string | null
}

const state = reactive<LiveState>({ mode: 'off', sse: 'idle', lastEventAt: null, lastUpdatedAt: null, eventsSeen: 0, key: null, slug: null })

type Handler = (e: StreamEvent) => void
const handlers = new Set<Handler>()
let client: SseClient | null = null
let pollTimer: ReturnType<typeof setInterval> | null = null
const LAST_ID_STORAGE = 'adpulse.lastEventId'

function emit(e: StreamEvent) {
  for (const h of handlers) {
    try {
      h(e)
    } catch (err) {
      console.error('live handler failed', err)
    }
  }
}

function startPolling() {
  if (pollTimer) return
  state.mode = 'polling'
  pollTimer = setInterval(() => {
    if (typeof document !== 'undefined' && document.hidden) return
    emit({ type: 'poll', data: {}, receivedAt: Date.now() })
  }, POLL_INTERVAL_MS)
}
function stopPolling() {
  if (pollTimer) clearInterval(pollTimer)
  pollTimer = null
}

export function connectLive(slug: string, key: string) {
  if (state.key === key && client) return
  disconnectLive()
  state.key = key
  state.slug = slug
  state.mode = 'sse'
  let lastId: string | null = null
  try {
    lastId = sessionStorage.getItem(`${LAST_ID_STORAGE}:${slug}`)
  } catch {
    /* ignore */
  }
  client = new SseClient({
    key,
    lastEventId: lastId,
    onEvent: (e) => {
      state.lastEventAt = e.receivedAt
      if (e.type !== 'heartbeat' && e.type !== 'ping') {
        state.eventsSeen += 1
        state.lastUpdatedAt = e.receivedAt
      }
      if (e.id) {
        try {
          sessionStorage.setItem(`${LAST_ID_STORAGE}:${slug}`, e.id)
        } catch {
          /* ignore */
        }
      }
      if (e.type === 'pacing.alert') recordAlert(slug, e)
      emit(e)
    },
    onState: (s, detail) => {
      state.sse = s
      state.detail = detail
      if (s === 'open') {
        stopPolling()
        state.mode = 'sse'
        if (!state.lastUpdatedAt) state.lastUpdatedAt = Date.now()
      } else if (s === 'unsupported' || s === 'reconnecting') {
        // Poll while the stream is down so the UI still refreshes.
        startPolling()
      }
    },
    onFatal: () => {
      stopPolling()
      state.mode = 'off'
    },
  })
  client.start()
}

export function disconnectLive() {
  client?.stop()
  client = null
  stopPolling()
  state.mode = 'off'
  state.sse = 'idle'
  state.key = null
  state.slug = null
}

/** Mark that fresh data has been rendered (used by pages after a successful fetch). */
export function markUpdated() {
  state.lastUpdatedAt = Date.now()
}

export function useLive() {
  return readonly(state)
}

/**
 * Subscribe to live events for the lifetime of the component.
 * `types` may include 'poll' to run on the polling fallback tick.
 */
export function onLive(types: string[] | '*', handler: Handler) {
  const wrapped: Handler = (e) => {
    if (types === '*' || types.includes(e.type)) handler(e)
  }
  handlers.add(wrapped)
  onBeforeUnmount(() => handlers.delete(wrapped))
  return () => handlers.delete(wrapped)
}

/** Debounced refetch helper: coalesces bursts of events into one refresh. */
export function useLiveRefetch(types: string[], refetch: () => void | Promise<void>, debounceMs = 400) {
  let t: ReturnType<typeof setTimeout> | null = null
  onLive([...types, 'poll'], () => {
    if (t) clearTimeout(t)
    t = setTimeout(() => void refetch(), debounceMs)
  })
}

export function watchLiveSource(source: WatchSource<{ slug: string | null; key: string | null }>) {
  watch(
    source,
    (v) => {
      if (v.slug && v.key) connectLive(v.slug, v.key)
      else disconnectLive()
    },
    { immediate: true },
  )
}

/** Test helper */
export function _emitForTest(e: StreamEvent) {
  emit(e)
}
