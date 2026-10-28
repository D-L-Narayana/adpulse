import { describe, expect, it, vi } from 'vitest'
import { frameToEvent, parseFrame, SseClient } from '../src/lib/sse'

describe('SSE frame parsing', () => {
  it('parses id/event/data/retry and joins multi-line data', () => {
    const f = parseFrame('id: 42\nevent: metrics.upserted\nretry: 5000\ndata: {"campaignId":\ndata: "abc"}')
    expect(f).toEqual({ id: '42', event: 'metrics.upserted', retry: 5000, data: '{"campaignId":\n"abc"}' })
  })
  it('ignores comments (heartbeats) and returns null for empty blocks', () => {
    expect(parseFrame(': keep-alive')).toBeNull()
    expect(parseFrame('')).toBeNull()
  })
  it('converts frames to typed events, falling back to data.type', () => {
    const ev = frameToEvent({ data: '{"type":"pacing.alert","campaignId":"x"}' })
    expect(ev?.type).toBe('pacing.alert')
    expect(ev?.data.campaignId).toBe('x')
    const raw = frameToEvent({ event: 'heartbeat', data: 'ping' })
    expect(raw?.data.raw).toBe('ping')
  })
})

function streamOf(chunks: string[]): ReadableStream<Uint8Array> {
  const enc = new TextEncoder()
  return new ReadableStream({
    start(c) {
      for (const ch of chunks) c.enqueue(enc.encode(ch))
      c.close()
    },
  })
}

describe('SseClient', () => {
  it('sends X-Api-Key + ngrok header + Last-Event-ID and dispatches events split across chunks', async () => {
    const seen: string[] = []
    let headers: Headers | undefined
    const fetchImpl = vi.fn(async (_url: string | URL | Request, init?: RequestInit) => {
      headers = new Headers(init?.headers)
      return new Response(streamOf(['id: 1\nevent: campaign.ch', 'anged\ndata: {"id":"c1"}\n\nid: 2\nevent: import.completed\ndata: {}\n\n']), { status: 200, headers: { 'content-type': 'text/event-stream' } })
    }) as unknown as typeof fetch
    const states: string[] = []
    const client = new SseClient({ key: 'k1', lastEventId: '0', fetchImpl, onEvent: (e) => seen.push(e.type), onState: (s) => states.push(s) })
    client.start()
    await new Promise((r) => setTimeout(r, 20))
    client.stop()
    expect(headers?.get('X-Api-Key')).toBe('k1')
    expect(headers?.get('ngrok-skip-browser-warning')).toBe('1')
    expect(headers?.get('Last-Event-ID')).toBe('0')
    expect(headers?.get('Accept')).toBe('text/event-stream')
    expect(seen).toEqual(['campaign.changed', 'import.completed'])
    expect(client.lastEventId).toBe('2')
    expect(states).toContain('open')
  })
  it('stops on 401 and reports fatal instead of reconnecting', async () => {
    const fetchImpl = vi.fn(async () => new Response('{}', { status: 401 })) as unknown as typeof fetch
    const onFatal = vi.fn()
    const client = new SseClient({ key: 'bad', fetchImpl, onEvent: () => {}, onFatal })
    client.start()
    await new Promise((r) => setTimeout(r, 10))
    expect(onFatal).toHaveBeenCalledWith(401)
    expect(fetchImpl).toHaveBeenCalledTimes(1)
  })
  it('flags non-SSE responses as unsupported so the caller can poll', async () => {
    const fetchImpl = vi.fn(async () => new Response('<html>', { status: 200, headers: { 'content-type': 'text/html' } })) as unknown as typeof fetch
    const states: string[] = []
    const client = new SseClient({ key: 'k', fetchImpl, onEvent: () => {}, onState: (s) => states.push(s) })
    client.start()
    await new Promise((r) => setTimeout(r, 10))
    expect(states[states.length - 1]).toBe('unsupported')
  })
})
