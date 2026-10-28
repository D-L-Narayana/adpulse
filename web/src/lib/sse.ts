import { baseHeaders, buildUrl } from './api'
import type { StreamEvent } from './types'

/**
 * SSE over `fetch` + ReadableStream. EventSource cannot send `X-Api-Key` / `ngrok-skip-browser-warning`,
 * so we parse `text/event-stream` ourselves. Supports `id:` tracking + `Last-Event-ID` on reconnect,
 * `retry:` hints, multi-line `data:`, and exponential back-off capped at 30 s.
 */
export type SseState = 'idle' | 'connecting' | 'open' | 'reconnecting' | 'closed' | 'unsupported'

export interface SseOptions {
  key: string
  path?: string
  onEvent: (e: StreamEvent) => void
  onState?: (s: SseState, detail?: string) => void
  /** called when the server answers 401/404 — we stop reconnecting */
  onFatal?: (status: number) => void
  lastEventId?: string | null
  fetchImpl?: typeof fetch
}

export interface ParsedFrame {
  id?: string
  event?: string
  data: string
  retry?: number
}

/** Parse one SSE frame (lines between blank lines). Exported for tests. */
export function parseFrame(block: string): ParsedFrame | null {
  const lines = block.split(/\r?\n/)
  const frame: ParsedFrame = { data: '' }
  const dataLines: string[] = []
  let sawField = false
  for (const raw of lines) {
    if (!raw || raw.startsWith(':')) continue
    const idx = raw.indexOf(':')
    const field = idx === -1 ? raw : raw.slice(0, idx)
    let value = idx === -1 ? '' : raw.slice(idx + 1)
    if (value.startsWith(' ')) value = value.slice(1)
    sawField = true
    switch (field) {
      case 'event':
        frame.event = value
        break
      case 'data':
        dataLines.push(value)
        break
      case 'id':
        if (!value.includes('\0')) frame.id = value
        break
      case 'retry': {
        const n = parseInt(value, 10)
        if (Number.isFinite(n)) frame.retry = n
        break
      }
    }
  }
  if (!sawField) return null
  frame.data = dataLines.join('\n')
  return frame
}

export function frameToEvent(f: ParsedFrame): StreamEvent | null {
  if (!f.event && !f.data) return null
  let data: Record<string, unknown> = {}
  if (f.data) {
    try {
      const parsed = JSON.parse(f.data)
      data = parsed && typeof parsed === 'object' ? parsed : { value: parsed }
    } catch {
      data = { raw: f.data }
    }
  }
  const type = f.event || (typeof data.type === 'string' ? (data.type as string) : 'message')
  return { id: f.id, type, data, receivedAt: Date.now() }
}

export class SseClient {
  private ctrl: AbortController | null = null
  private attempt = 0
  private retryMs = 2000
  private timer: ReturnType<typeof setTimeout> | null = null
  private stopped = false
  lastEventId: string | null
  state: SseState = 'idle'

  constructor(private readonly opts: SseOptions) {
    this.lastEventId = opts.lastEventId ?? null
  }

  private setState(s: SseState, detail?: string) {
    this.state = s
    this.opts.onState?.(s, detail)
  }

  start() {
    this.stopped = false
    void this.connect()
  }

  stop() {
    this.stopped = true
    if (this.timer) clearTimeout(this.timer)
    this.ctrl?.abort()
    this.ctrl = null
    this.setState('closed')
  }

  private scheduleReconnect(reason: string) {
    if (this.stopped) return
    this.attempt += 1
    const delay = Math.min(30_000, this.retryMs * Math.pow(1.8, Math.min(6, this.attempt - 1))) + Math.random() * 400
    this.setState('reconnecting', reason)
    this.timer = setTimeout(() => void this.connect(), delay)
  }

  private async connect() {
    if (this.stopped) return
    this.ctrl?.abort()
    const ctrl = new AbortController()
    this.ctrl = ctrl
    this.setState(this.attempt === 0 ? 'connecting' : 'reconnecting')
    const headers = baseHeaders(this.opts.key, { Accept: 'text/event-stream', 'Cache-Control': 'no-cache' })
    if (this.lastEventId) headers['Last-Event-ID'] = this.lastEventId
    const f = this.opts.fetchImpl ?? fetch
    let res: Response
    try {
      res = await f(buildUrl(this.opts.path ?? '/stream'), { headers, signal: ctrl.signal, cache: 'no-store' })
    } catch (e) {
      if (ctrl.signal.aborted) return
      this.scheduleReconnect(e instanceof Error ? e.message : 'network error')
      return
    }
    if (res.status === 401 || res.status === 403 || res.status === 404) {
      this.opts.onFatal?.(res.status)
      this.setState(res.status === 404 ? 'unsupported' : 'closed', `HTTP ${res.status}`)
      return
    }
    if (!res.ok || !res.body) {
      this.scheduleReconnect(`HTTP ${res.status}`)
      return
    }
    const ct = res.headers.get('content-type') ?? ''
    if (!ct.includes('text/event-stream')) {
      // Not an SSE endpoint (e.g. ngrok HTML page). Treat as unsupported so the caller can poll.
      this.setState('unsupported', `content-type ${ct || 'unknown'}`)
      return
    }
    this.attempt = 0
    this.setState('open')
    const reader = res.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    try {
      for (;;) {
        const { value, done } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        let sep: number
        // frames are separated by a blank line (\n\n or \r\n\r\n)
        while ((sep = buffer.search(/\r?\n\r?\n/)) !== -1) {
          const block = buffer.slice(0, sep)
          buffer = buffer.slice(sep).replace(/^\r?\n\r?\n/, '')
          const frame = parseFrame(block)
          if (!frame) continue
          if (frame.retry) this.retryMs = Math.max(500, frame.retry)
          if (frame.id !== undefined) this.lastEventId = frame.id
          const ev = frameToEvent(frame)
          if (ev) this.opts.onEvent(ev)
        }
      }
    } catch (e) {
      if (ctrl.signal.aborted) return
      this.scheduleReconnect(e instanceof Error ? e.message : 'stream error')
      return
    }
    if (!ctrl.signal.aborted) this.scheduleReconnect('stream ended')
  }
}
