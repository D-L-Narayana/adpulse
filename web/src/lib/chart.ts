/** Small helpers for the hand-built SVG charts. */

/** "Nice" axis ticks (1/2/5 × 10^n) covering [0, max]. */
export function niceTicks(max: number, count = 4): number[] {
  if (!Number.isFinite(max) || max <= 0) return [0, 1]
  const rough = max / count
  const pow = Math.pow(10, Math.floor(Math.log10(rough)))
  const frac = rough / pow
  const niceFrac = frac <= 1 ? 1 : frac <= 2 ? 2 : frac <= 2.5 ? 2.5 : frac <= 5 ? 5 : 10
  const step = niceFrac * pow
  const top = Math.ceil(max / step) * step
  const ticks: number[] = []
  for (let v = 0; v <= top + step / 2; v += step) ticks.push(Math.round(v * 1e6) / 1e6)
  return ticks
}

export function linearScale(domain: [number, number], range: [number, number]) {
  const [d0, d1] = domain
  const [r0, r1] = range
  const span = d1 - d0 || 1
  return (v: number) => r0 + ((v - d0) / span) * (r1 - r0)
}

/** Pick at most `max` evenly spaced indices from 0..n-1, always including first and last. */
export function pickIndices(n: number, max: number): number[] {
  if (n <= max) return Array.from({ length: n }, (_, i) => i)
  const step = (n - 1) / (max - 1)
  const out = new Set<number>()
  for (let i = 0; i < max; i++) out.add(Math.round(i * step))
  return Array.from(out)
}

/** Build an SVG path through points (straight segments; monotone enough for cumulative data). */
export function linePath(points: Array<[number, number] | null>): string {
  let d = ''
  let pen = false
  for (const p of points) {
    if (!p) {
      pen = false
      continue
    }
    d += `${pen ? 'L' : 'M'}${p[0].toFixed(1)},${p[1].toFixed(1)}`
    pen = true
  }
  return d
}

export function areaPath(points: Array<[number, number]>, baseline: number): string {
  if (!points.length) return ''
  const first = points[0]
  const last = points[points.length - 1]
  return `${linePath(points)}L${last[0].toFixed(1)},${baseline.toFixed(1)}L${first[0].toFixed(1)},${baseline.toFixed(1)}Z`
}
