<script setup lang="ts">
import { computed, ref } from 'vue'
import type { DailyTrendRow } from '../types'
import { fmtCompact, fmtDate, fmtMoney2 } from '../format'

const props = defineProps<{ rows: DailyTrendRow[] }>()

const W = 720, H = 260, PL = 48, PR = 16, PT = 16, PB = 32
const hover = ref<number | null>(null)

const scale = computed(() => {
  const n = props.rows.length
  const max = Math.max(1, ...props.rows.map((r) => Math.max(r.spend, r.revenue)))
  const niceMax = Math.ceil(max / Math.pow(10, Math.floor(Math.log10(max)))) * Math.pow(10, Math.floor(Math.log10(max)))
  const x = (i: number) => (n <= 1 ? PL : PL + (i * (W - PL - PR)) / (n - 1))
  const y = (v: number) => PT + (H - PT - PB) * (1 - v / niceMax)
  return { n, niceMax, x, y }
})

const path = (key: 'spend' | 'revenue') =>
  props.rows.map((r, i) => `${i === 0 ? 'M' : 'L'}${scale.value.x(i).toFixed(1)},${scale.value.y(r[key]).toFixed(1)}`).join(' ')

const area = computed(() => {
  if (!props.rows.length) return ''
  const { x, y } = scale.value
  const base = y(0)
  return `${path('revenue')} L${x(props.rows.length - 1).toFixed(1)},${base} L${x(0).toFixed(1)},${base} Z`
})

const ticks = computed(() => [0, 0.25, 0.5, 0.75, 1].map((t) => ({ v: scale.value.niceMax * t, y: scale.value.y(scale.value.niceMax * t) })))
const xLabels = computed(() => {
  const step = Math.max(1, Math.round(props.rows.length / 6))
  return props.rows.map((r, i) => ({ i, label: fmtDate(r.date) })).filter((_, i) => i % step === 0)
})

function onMove(e: MouseEvent) {
  const svg = e.currentTarget as SVGSVGElement
  const rect = svg.getBoundingClientRect()
  const px = ((e.clientX - rect.left) / rect.width) * W
  const n = props.rows.length
  if (n < 2) return
  const i = Math.round(((px - PL) / (W - PL - PR)) * (n - 1))
  hover.value = Math.max(0, Math.min(n - 1, i))
}
</script>

<template>
  <div>
    <div class="legend" style="margin-bottom: 8px">
      <span><i style="background: #0ea5e9"></i>Spend</span>
      <span><i style="background: #a78bfa"></i>Revenue</span>
      <span v-if="hover !== null && rows[hover]" style="margin-left: auto; color: #0f172a">
        <strong>{{ fmtDate(rows[hover].date) }}</strong> · spend {{ fmtMoney2(rows[hover].spend) }} · revenue {{ fmtMoney2(rows[hover].revenue) }}
      </span>
    </div>
    <svg class="chart" :viewBox="`0 0 ${W} ${H}`" role="img" aria-label="Daily spend and revenue" @mousemove="onMove" @mouseleave="hover = null">
      <defs>
        <linearGradient id="rev" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0" stop-color="#a78bfa" stop-opacity="0.25" />
          <stop offset="1" stop-color="#a78bfa" stop-opacity="0" />
        </linearGradient>
      </defs>
      <g v-for="t in ticks" :key="t.v">
        <line :x1="PL" :x2="W - PR" :y1="t.y" :y2="t.y" stroke="#eef2f7" />
        <text :x="PL - 8" :y="t.y + 4" text-anchor="end" font-size="11" fill="#94a3b8">${{ fmtCompact(t.v) }}</text>
      </g>
      <text v-for="l in xLabels" :key="l.i" :x="scale.x(l.i)" :y="H - 10" text-anchor="middle" font-size="11" fill="#94a3b8">{{ l.label }}</text>
      <path :d="area" fill="url(#rev)" />
      <path :d="path('revenue')" fill="none" stroke="#a78bfa" stroke-width="2" stroke-linejoin="round" />
      <path :d="path('spend')" fill="none" stroke="#0ea5e9" stroke-width="2.5" stroke-linejoin="round" />
      <g v-if="hover !== null && rows[hover]">
        <line :x1="scale.x(hover)" :x2="scale.x(hover)" :y1="PT" :y2="H - PB" stroke="#cbd5e1" stroke-dasharray="3 3" />
        <circle :cx="scale.x(hover)" :cy="scale.y(rows[hover].spend)" r="4" fill="#0ea5e9" stroke="#fff" stroke-width="2" />
        <circle :cx="scale.x(hover)" :cy="scale.y(rows[hover].revenue)" r="4" fill="#a78bfa" stroke="#fff" stroke-width="2" />
      </g>
    </svg>
  </div>
</template>
