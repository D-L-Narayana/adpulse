<script setup lang="ts">
import { computed } from 'vue'
import type { ImportPreview } from '../lib/types'
import Badge from './ui/Badge.vue'

const props = defineProps<{ preview: ImportPreview; fileName?: string }>()

const formatLabel = computed(() => {
  const f = props.preview.format.toLowerCase()
  if (f.includes('google')) return 'Google Ads export'
  if (f.includes('meta') || f.includes('facebook')) return 'Meta Ads Manager export'
  if (f.includes('generic') || f.includes('template')) return 'AdPulse generic template'
  if (f === 'unknown' || !f) return 'Unrecognised format'
  return props.preview.format
})
const unknown = computed(() => /unknown/i.test(props.preview.format) || !props.preview.format)

/** mapping may be { targetField: sourceColumn } or { sourceColumn: targetField }; we show both sides. */
const mappingRows = computed(() => Object.entries(props.preview.mapping ?? {}).map(([k, v]) => ({ key: k, value: v })))
const unmapped = computed(() => mappingRows.value.filter((m) => m.value === null || m.value === '' || m.value === undefined))

const columns = computed(() => {
  const first = props.preview.rows[0]
  return first ? Object.keys(first) : []
})
const rows = computed(() => props.preview.rows.slice(0, 10))
function cell(v: unknown): string {
  if (v === null || v === undefined || v === '') return '—'
  if (typeof v === 'number') return Number.isInteger(v) ? v.toLocaleString('en-US') : v.toLocaleString('en-US', { maximumFractionDigits: 2 })
  return String(v)
}
function isNum(v: unknown) {
  return typeof v === 'number' || (typeof v === 'string' && v !== '' && !Number.isNaN(Number(v)))
}
</script>

<template>
  <div class="prev" data-testid="csv-preview">
    <div class="head">
      <div>
        <p class="eyebrow">Detected format</p>
        <p class="fmt" data-testid="format">{{ formatLabel }}<span v-if="fileName" class="file mono"> · {{ fileName }}</span></p>
      </div>
      <Badge :tone="unknown ? 'bad' : 'ok'" :dot="false">{{ preview.rowCount !== undefined ? `${preview.rowCount.toLocaleString('en-US')} rows` : `${preview.rows.length}+ rows` }}</Badge>
    </div>

    <div v-if="preview.warnings.length" class="warnings" role="alert" data-testid="warnings">
      <p class="wt">{{ preview.warnings.length }} warning{{ preview.warnings.length === 1 ? '' : 's' }}</p>
      <ul>
        <li v-for="(w, i) in preview.warnings" :key="i">{{ w }}</li>
      </ul>
    </div>

    <section v-if="mappingRows.length" class="block" aria-labelledby="map-h">
      <h3 id="map-h" class="bh">Column mapping <span class="muted">— {{ mappingRows.length - unmapped.length }} of {{ mappingRows.length }} mapped</span></h3>
      <dl class="map" data-testid="mapping">
        <div v-for="m in mappingRows" :key="m.key" class="m" :class="{ miss: m.value === null || m.value === '' }">
          <dt class="mono">{{ m.key }}</dt>
          <dd><span class="arrow" aria-hidden="true">→</span><span class="mono">{{ m.value === null || m.value === '' || m.value === undefined ? 'not found' : m.value }}</span></dd>
        </div>
      </dl>
    </section>

    <section class="block" aria-labelledby="rows-h">
      <h3 id="rows-h" class="bh">First {{ rows.length }} rows <span class="muted">— as the API parsed them</span></h3>
      <div v-if="rows.length" class="twrap">
        <table class="t" data-testid="rows">
          <thead>
            <tr><th v-for="c in columns" :key="c" scope="col" class="mono">{{ c }}</th></tr>
          </thead>
          <tbody>
            <tr v-for="(r, i) in rows" :key="i">
              <td v-for="c in columns" :key="c" :class="{ num: isNum(r[c]) }">{{ cell(r[c]) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-else class="muted small">The API did not return any parsed rows for this file.</p>
    </section>
  </div>
</template>

<style scoped>
.prev {
  display: flex;
  flex-direction: column;
  gap: var(--s-5);
}
.head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--s-3);
  flex-wrap: wrap;
}
.eyebrow {
  font-size: var(--text-xs);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-muted);
  font-weight: 500;
}
.fmt {
  font-size: var(--text-lg);
  font-weight: 700;
  margin-top: 2px;
}
.file {
  font-weight: 400;
  font-size: var(--text-sm);
  color: var(--text-muted);
}
.warnings {
  background: var(--warning-soft);
  border: 1px solid color-mix(in srgb, var(--warning) 30%, transparent);
  border-radius: var(--r-md);
  padding: var(--s-3) var(--s-4);
  font-size: var(--text-sm);
  color: var(--warning);
}
.wt {
  font-weight: 700;
  margin-bottom: 4px;
}
.warnings ul {
  margin: 0;
  padding-left: 18px;
  color: var(--text);
}
.bh {
  font-size: var(--text-sm);
  font-weight: 700;
  margin-bottom: var(--s-2);
}
.map {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 6px 16px;
  margin: 0;
}
.m {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 8px;
  padding: 6px 10px;
  border: 1px solid var(--border-soft);
  border-radius: var(--r-sm);
  background: var(--surface-alt);
  font-size: var(--text-xs);
  align-items: center;
}
.m dt,
.m dd {
  margin: 0;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.m dd {
  display: flex;
  gap: 6px;
  align-items: center;
}
.arrow {
  color: var(--text-faint);
}
.m.miss {
  border-style: dashed;
}
.m.miss dd {
  color: var(--warning);
}
.twrap {
  overflow-x: auto;
  border: 1px solid var(--border-soft);
  border-radius: var(--r-md);
}
.t {
  border-collapse: collapse;
  width: 100%;
  font-size: var(--text-xs);
}
.t th,
.t td {
  padding: 7px 10px;
  border-bottom: 1px solid var(--border-soft);
  text-align: left;
  white-space: nowrap;
}
.t th {
  background: var(--surface-sunken);
  color: var(--text-muted);
  font-weight: 500;
}
.t tbody tr:last-child td {
  border-bottom: 0;
}
.t td.num {
  text-align: right;
  font-variant-numeric: tabular-nums;
  font-family: var(--font-mono);
}
</style>
