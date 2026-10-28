<script setup lang="ts">
/**
 * Cumulative spend vs cumulative planned budget — hand-built SVG (no chart library).
 * Proper axes with nice ticks, direct end labels instead of a legend, keyboard + pointer hover tooltip,
 * prefers-reduced-motion support and a toggleable accessible data table.
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { areaPath, linePath, linearScale, niceTicks, pickIndices } from '../lib/chart'
import { fmtDate, fmtMoney, fmtMoneyCompact, fmtSignedPct } from '../lib/format'
import type { TrendRow } from '../lib/types'
import Skeleton from './ui/Skeleton.vue'

const props = withDefaults(defineProps<{ rows: TrendRow[]; loading?: boolean; height?: number; cumulative?: boolean }>(), { loading: false, height: 260, cumulative: true })

const wrap = ref<HTMLDivElement | null>(null)
const width = ref(640)
let ro: ResizeObserver | null = null
onMounted(() => {
  if (wrap.value) width.value = wrap.value.clientWidth || 640
  if (typeof ResizeObserver !== 'undefined' && wrap.value) {
    ro = new ResizeObserver((entries) => {
      const w = entries[0]?.contentRect.width
      if (w) width.value = w
    })
    ro.observe(wrap.value)
  }
})
onBeforeUnmount(() => ro?.disconnect())

const showTable = ref(false)
const hover = ref<number | null>(null)
const reduced = typeof matchMedia !== 'undefined' && matchMedia('(prefers-reduced-motion: reduce)').matches

interface Point {
  date: string
  spend: number
  budget: number | null
}
const series = computed<Point[]>(() => {
  let s = 0
  let b = 0
  const hasB = props.rows.some((r) => typeof r.budget === 'number')
  return props.rows.map((r) => {
    s += r.spend
    b += r.budget ?? 0
    return props.cumulative ? { date: r.date, spend: s, budget: hasB ? b : null } : { date: r.date, spend: r.spend, budget: hasB ? r.budget ?? 0 : null }
  })
})
const hasBudget = computed(() => series.value.some((p) => p.budget !== null))
const last = computed(() => series.value[series.value.length - 1])

const insight = computed(() => {
  if (!last.value) return 'No spend recorded in this period'
  if (!hasBudget.value || !last.value.budget) return `${fmtMoney(last.value.spend)} spent over ${series.value.length} day${series.value.length === 1 ? '' : 's'}`
  const pct = ((last.value.spend - last.value.budget) / last.value.budget) * 100
  if (Math.abs(pct) < 1) return `Spend is on plan — ${fmtMoney(last.value.spend)} of ${fmtMoney(last.value.budget)}`
  return `Spend is ${Math.abs(pct).toFixed(0)}% ${pct > 0 ? 'over' : 'under'} plan — ${fmtMoney(last.value.spend)} vs ${fmtMoney(last.value.budget)} planned`
})
const ariaLabel = computed(() => `Line chart. ${insight.value}. ${series.value.length} data points from ${fmtDate(series.value[0]?.date)} to ${fmtDate(last.value?.date)}. Use the data table toggle for exact values.`)

/* ---- geometry ---- */
const pad = { top: 14, right: 84, bottom: 28, left: 8 }
const yMax = computed(() => Math.max(1, ...series.value.map((p) => Math.max(p.spend, p.budget ?? 0))))
const ticks = computed(() => niceTicks(yMax.value, 4))
const yTop = computed(() => ticks.value[ticks.value.length - 1])
const yLabelW = computed(() => Math.max(...ticks.value.map((t) => fmtMoneyCompact(t).length)) * 7 + 10)
const plot = computed(() => ({
  x0: pad.left + yLabelW.value,
  x1: Math.max(pad.left + yLabelW.value + 40, width.value - pad.right),
  y0: pad.top,
  y1: props.height - pad.bottom,
}))
const x = computed(() => linearScale([0, Math.max(1, series.value.length - 1)], [plot.value.x0, plot.value.x1]))
const y = computed(() => linearScale([0, yTop.value], [plot.value.y1, plot.value.y0]))

const spendPts = computed<Array<[number, number]>>(() => series.value.map((p, i) => [x.value(i), y.value(p.spend)]))
const budgetPts = computed<Array<[number, number] | null>>(() => series.value.map((p, i) => (p.budget === null ? null : [x.value(i), y.value(p.budget)])))
const spendD = computed(() => linePath(spendPts.value))
const areaD = computed(() => areaPath(spendPts.value, plot.value.y1))
const budgetD = computed(() => linePath(budgetPts.value))
const xLabelIdx = computed(() => pickIndices(series.value.length, Math.max(2, Math.floor((plot.value.x1 - plot.value.x0) / 70))))

/* end labels: keep them from overlapping */
const endLabels = computed(() => {
  if (!last.value) return []
  const out: { text: string; y: number; color: string; weight: number }[] = []
  const ys = y.value(last.value.spend)
  out.push({ text: 'Spend', y: ys, color: 'var(--chart-1)', weight: 700 })
  if (last.value.budget !== null) {
    let yb = y.value(last.value.budget)
    if (Math.abs(yb - ys) < 14) yb = yb < ys ? ys - 14 : ys + 14
    out.push({ text: 'Planned', y: yb, color: 'var(--text-muted)', weight: 500 })
  }
  return out
})

/* ---- interaction ---- */
function idxFromClientX(clientX: number) {
  const svg = wrap.value?.querySelector('svg')
  if (!svg) return null
  const rect = svg.getBoundingClientRect()
  const px = ((clientX - rect.left) / rect.width) * width.value
  const n = series.value.length
  if (n < 2) return n ? 0 : null
  const i = Math.round(((px - plot.value.x0) / (plot.value.x1 - plot.value.x0)) * (n - 1))
  return Math.max(0, Math.min(n - 1, i))
}
function onMove(e: PointerEvent) {
  hover.value = idxFromClientX(e.clientX)
}
function onKey(e: KeyboardEvent) {
  const n = series.value.length
  if (!n) return
  if (e.key === 'ArrowRight') hover.value = Math.min(n - 1, (hover.value ?? -1) + 1)
  else if (e.key === 'ArrowLeft') hover.value = Math.max(0, (hover.value ?? n) - 1)
  else if (e.key === 'Home') hover.value = 0
  else if (e.key === 'End') hover.value = n - 1
  else if (e.key === 'Escape') hover.value = null
  else return
  e.preventDefault()
}
const tip = computed(() => {
  if (hover.value === null) return null
  const p = series.value[hover.value]
  if (!p) return null
  const cx = x.value(hover.value)
  const flip = cx > (plot.value.x0 + plot.value.x1) / 2
  const delta = p.budget ? ((p.spend - p.budget) / p.budget) * 100 : null
  return { p, cx, flip, delta, cy: y.value(p.spend), cyb: p.budget === null ? null : y.value(p.budget) }
})
</script>

<template>
  <div class="pc">
    <div class="pc-head">
      <p class="insight" aria-live="polite">
        <Skeleton v-if="loading" width="60%" height="16px" :inline="true" />
        <template v-else>{{ insight }}</template>
      </p>
      <button type="button" class="toggle" :aria-pressed="showTable" :disabled="loading || !series.length" @click="showTable = !showTable">
        {{ showTable ? 'Show chart' : 'Show data table' }}
      </button>
    </div>

    <div v-if="loading" :style="{ height: height + 'px' }"><Skeleton height="100%" radius="8px" /></div>
    <div v-else-if="!series.length" class="none" :style="{ height: height + 'px' }"><p>No daily rows in this period.</p></div>

    <template v-else>
      <div v-show="!showTable" ref="wrap" class="canvas" :style="{ height: height + 'px' }">
        <svg
          :viewBox="`0 0 ${width} ${height}`"
          :width="width"
          :height="height"
          role="img"
          :aria-label="ariaLabel"
          tabindex="0"
          class="svg"
          :class="{ animate: !reduced }"
          @pointermove="onMove"
          @pointerleave="hover = null"
          @keydown="onKey"
          @blur="hover = null"
        >
          <!-- y grid + labels -->
          <g v-for="t in ticks" :key="t" class="tick">
            <line :x1="plot.x0" :x2="plot.x1" :y1="y(t)" :y2="y(t)" class="grid" />
            <text :x="plot.x0 - 8" :y="y(t)" text-anchor="end" dominant-baseline="middle" class="lbl num">{{ fmtMoneyCompact(t) }}</text>
          </g>
          <!-- x axis -->
          <line :x1="plot.x0" :x2="plot.x1" :y1="plot.y1" :y2="plot.y1" class="axis" />
          <text v-for="i in xLabelIdx" :key="'x' + i" :x="x(i)" :y="plot.y1 + 18" :text-anchor="i === 0 ? 'start' : i === series.length - 1 ? 'end' : 'middle'" class="lbl">{{ fmtDate(series[i].date) }}</text>

          <!-- series -->
          <path :d="areaD" class="area" />
          <path v-if="hasBudget" :d="budgetD" class="budget" />
          <path :d="spendD" class="spend" pathLength="1" />

          <!-- direct labels -->
          <text v-for="l in endLabels" :key="l.text" :x="plot.x1 + 8" :y="l.y" dominant-baseline="middle" class="end" :style="{ fill: l.color, fontWeight: l.weight }">{{ l.text }}</text>

          <!-- hover -->
          <g v-if="tip" class="hover" aria-hidden="true">
            <line :x1="tip.cx" :x2="tip.cx" :y1="plot.y0" :y2="plot.y1" class="cursor" />
            <circle :cx="tip.cx" :cy="tip.cy" r="4" class="dot spend-dot" />
            <circle v-if="tip.cyb !== null" :cx="tip.cx" :cy="tip.cyb" r="3.5" class="dot budget-dot" />
          </g>
        </svg>
        <div v-if="tip" class="tooltip" :style="{ left: tip.cx + 'px', top: plot.y0 + 'px', transform: tip.flip ? 'translateX(calc(-100% - 12px))' : 'translateX(12px)' }" role="status">
          <p class="tt-date">{{ fmtDate(tip.p.date, { weekday: 'short', month: 'short', day: 'numeric' }) }}</p>
          <p class="tt-row"><span class="k"><i class="sw spend-sw"></i>Spend</span><span class="num">{{ fmtMoney(tip.p.spend) }}</span></p>
          <p v-if="tip.p.budget !== null" class="tt-row"><span class="k"><i class="sw budget-sw"></i>Planned</span><span class="num">{{ fmtMoney(tip.p.budget) }}</span></p>
          <p v-if="tip.delta !== null" class="tt-delta num">{{ fmtSignedPct(tip.delta, 1) }} vs plan</p>
        </div>
      </div>

      <div v-if="showTable" class="table-wrap" :style="{ maxHeight: height + 'px' }">
        <table class="data">
          <caption class="sr-only">Daily {{ cumulative ? 'cumulative ' : '' }}spend and planned budget</caption>
          <thead>
            <tr><th scope="col">Date</th><th scope="col" class="num">Spend</th><th v-if="hasBudget" scope="col" class="num">Planned</th></tr>
          </thead>
          <tbody>
            <tr v-for="p in series" :key="p.date">
              <td>{{ fmtDate(p.date, { month: 'short', day: 'numeric', year: 'numeric' }) }}</td>
              <td class="num">{{ fmtMoney(p.spend) }}</td>
              <td v-if="hasBudget" class="num">{{ p.budget === null ? '—' : fmtMoney(p.budget) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
    <p v-if="!loading && series.length && !hasBudget" class="note">No active campaign budgets in this period, so only actual spend is plotted.</p>
  </div>
</template>

<style scoped>
.pc {
  min-width: 0;
}
.pc-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: var(--s-3);
  margin-bottom: var(--s-2);
  flex-wrap: wrap;
}
.insight {
  font-size: var(--text-sm);
  font-weight: 500;
  flex: 1 1 240px;
}
.toggle {
  background: none;
  border: 0;
  padding: 2px 4px;
  font-size: var(--text-xs);
  color: var(--accent);
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 2px;
  border-radius: var(--r-sm);
}
.toggle:disabled {
  color: var(--text-faint);
  cursor: default;
  text-decoration: none;
}
.canvas {
  position: relative;
  width: 100%;
  overflow: visible;
}
.svg {
  display: block;
  width: 100%;
  height: 100%;
  touch-action: pan-y;
  outline: none;
  border-radius: var(--r-md);
}
.svg:focus-visible {
  box-shadow: var(--focus-ring);
}
.grid {
  stroke: var(--border-soft);
  stroke-dasharray: 3 4;
}
.axis {
  stroke: var(--border);
}
.lbl {
  font-size: 12px;
  fill: var(--text-muted);
  font-family: var(--font-sans);
}
.end {
  font-size: 12px;
  font-family: var(--font-sans);
}
.area {
  fill: var(--chart-1);
  opacity: 0.08;
}
.spend {
  fill: none;
  stroke: var(--chart-1);
  stroke-width: 2.5;
  stroke-linejoin: round;
  stroke-linecap: round;
}
.budget {
  fill: none;
  stroke: var(--text-muted);
  stroke-width: 1.5;
  stroke-dasharray: 5 4;
}
.animate .spend {
  stroke-dasharray: 1;
  stroke-dashoffset: 1;
  animation: draw 700ms var(--ease) forwards;
}
@keyframes draw {
  to {
    stroke-dashoffset: 0;
  }
}
.cursor {
  stroke: var(--border);
}
.dot {
  stroke: #fff;
  stroke-width: 1.5;
}
.spend-dot {
  fill: var(--chart-1);
}
.budget-dot {
  fill: var(--text-muted);
}
.tooltip {
  position: absolute;
  pointer-events: none;
  background: #fff;
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  box-shadow: var(--shadow-md);
  padding: 8px 10px;
  font-size: var(--text-xs);
  min-width: 150px;
  z-index: 2;
}
.tt-date {
  font-weight: 700;
  margin-bottom: 4px;
}
.tt-row {
  display: flex;
  justify-content: space-between;
  gap: 14px;
}
.k {
  color: var(--text-muted);
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.sw {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  display: inline-block;
}
.spend-sw {
  background: var(--chart-1);
}
.budget-sw {
  background: var(--text-muted);
}
.tt-delta {
  margin-top: 4px;
  color: var(--text-muted);
}
.none {
  display: grid;
  place-items: center;
  color: var(--text-muted);
  font-size: var(--text-sm);
  border: 1px dashed var(--border);
  border-radius: var(--r-md);
}
.table-wrap {
  overflow: auto;
  border: 1px solid var(--border-soft);
  border-radius: var(--r-md);
}
.data {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--text-sm);
}
.data th,
.data td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--border-soft);
  text-align: left;
}
.data th {
  position: sticky;
  top: 0;
  background: var(--surface);
  font-size: var(--text-xs);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-muted);
  font-weight: 500;
}
.data .num {
  text-align: right;
  font-variant-numeric: tabular-nums;
}
.note {
  margin-top: var(--s-2);
  font-size: var(--text-xs);
  color: var(--text-muted);
}
</style>
