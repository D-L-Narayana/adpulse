<script setup lang="ts" generic="T extends Record<string, any>">
import { computed, ref } from 'vue'
import Skeleton from './Skeleton.vue'

export interface Column<Row> {
  key: string
  label: string
  align?: 'left' | 'right' | 'center'
  sortable?: boolean
  width?: string
  /** value used for sorting / default rendering */
  value?: (row: Row) => string | number | null | undefined
  format?: (row: Row) => string
  mono?: boolean
  hideBelow?: number
  srLabel?: string
}

const props = withDefaults(
  defineProps<{
    columns: Column<T>[]
    rows: T[]
    rowKey: (row: T) => string
    loading?: boolean
    skeletonRows?: number
    caption?: string
    defaultSort?: { key: string; dir: 'asc' | 'desc' }
    clickable?: boolean
    dense?: boolean
    stickyHeader?: boolean
  }>(),
  { loading: false, skeletonRows: 6, clickable: false, dense: false, stickyHeader: false },
)
const emit = defineEmits<{ rowClick: [row: T] }>()

const sortKey = ref<string | null>(props.defaultSort?.key ?? null)
const sortDir = ref<'asc' | 'desc'>(props.defaultSort?.dir ?? 'desc')

function toggleSort(col: Column<T>) {
  if (!col.sortable) return
  if (sortKey.value === col.key) sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
  else {
    sortKey.value = col.key
    sortDir.value = col.align === 'right' ? 'desc' : 'asc'
  }
}

function cellValue(col: Column<T>, row: T): string | number | null | undefined {
  return col.value ? col.value(row) : (row[col.key] as string | number | null | undefined)
}

const sorted = computed(() => {
  const col = props.columns.find((c) => c.key === sortKey.value)
  if (!col) return props.rows
  const dir = sortDir.value === 'asc' ? 1 : -1
  return [...props.rows].sort((a, b) => {
    const va = cellValue(col, a)
    const vb = cellValue(col, b)
    if (va === vb) return 0
    if (va === null || va === undefined) return 1
    if (vb === null || vb === undefined) return -1
    if (typeof va === 'number' && typeof vb === 'number') return (va - vb) * dir
    return String(va).localeCompare(String(vb), undefined, { numeric: true, sensitivity: 'base' }) * dir
  })
})

function ariaSort(col: Column<T>): 'ascending' | 'descending' | 'none' | undefined {
  if (!col.sortable) return undefined
  if (sortKey.value !== col.key) return 'none'
  return sortDir.value === 'asc' ? 'ascending' : 'descending'
}

function onRowKey(e: KeyboardEvent, row: T) {
  if (!props.clickable) return
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    emit('rowClick', row)
  }
}

defineExpose({ sorted })
</script>

<template>
  <div class="table-wrap" :class="{ dense, sticky: stickyHeader }">
    <table class="table">
      <caption v-if="caption" class="sr-only">{{ caption }}</caption>
      <thead>
        <tr>
          <th
            v-for="col in columns"
            :key="col.key"
            scope="col"
            :class="[col.align ?? 'left', { sortable: col.sortable, sorted: sortKey === col.key }, col.hideBelow ? `hide-${col.hideBelow}` : '']"
            :style="col.width ? { width: col.width } : undefined"
            :aria-sort="ariaSort(col)"
          >
            <button v-if="col.sortable" type="button" class="sort-btn" @click="toggleSort(col)">
              <span>{{ col.label }}</span>
              <span class="sort-ind" aria-hidden="true">{{ sortKey === col.key ? (sortDir === 'asc' ? '↑' : '↓') : '↕' }}</span>
            </button>
            <span v-else>{{ col.label }}</span>
          </th>
        </tr>
      </thead>
      <tbody v-if="loading">
        <tr v-for="i in skeletonRows" :key="'sk' + i" class="sk-row">
          <td v-for="col in columns" :key="col.key" :class="[col.align ?? 'left', col.hideBelow ? `hide-${col.hideBelow}` : '']">
            <Skeleton :width="col.align === 'right' ? '60%' : '80%'" height="14px" :inline="true" />
          </td>
        </tr>
      </tbody>
      <tbody v-else-if="rows.length === 0">
        <tr>
          <td :colspan="columns.length" class="empty-cell"><slot name="empty">No rows.</slot></td>
        </tr>
      </tbody>
      <tbody v-else>
        <tr
          v-for="row in sorted"
          :key="rowKey(row)"
          :class="{ clickable }"
          :tabindex="clickable ? 0 : undefined"
          @click="clickable && emit('rowClick', row)"
          @keydown="onRowKey($event, row)"
        >
          <td v-for="col in columns" :key="col.key" :class="[col.align ?? 'left', { mono: col.mono, num: col.align === 'right' }, col.hideBelow ? `hide-${col.hideBelow}` : '']">
            <slot :name="`cell-${col.key}`" :row="row" :value="cellValue(col, row)">
              {{ col.format ? col.format(row) : cellValue(col, row) ?? '—' }}
            </slot>
          </td>
        </tr>
      </tbody>
      <tfoot v-if="$slots.foot">
        <slot name="foot" />
      </tfoot>
    </table>
  </div>
</template>

<style scoped>
.table-wrap {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
  border-radius: var(--r-md);
}
.table {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  font-size: var(--text-sm);
}
th,
td {
  padding: 12px 14px;
  border-bottom: 1px solid var(--border-soft);
  vertical-align: middle;
  white-space: nowrap;
}
.dense th,
.dense td {
  padding: 8px 12px;
}
th {
  font-size: var(--text-xs);
  font-weight: 500;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  background: var(--surface);
  text-align: left;
}
.sticky th {
  position: sticky;
  top: 0;
  z-index: 1;
}
th.right,
td.right {
  text-align: right;
}
th.center,
td.center {
  text-align: center;
}
td.mono {
  font-family: var(--font-mono);
  font-size: var(--text-xs);
}
td.num {
  font-variant-numeric: tabular-nums lining-nums;
}
.sort-btn {
  background: none;
  border: 0;
  padding: 0;
  font: inherit;
  color: inherit;
  text-transform: inherit;
  letter-spacing: inherit;
  cursor: pointer;
  display: inline-flex;
  gap: 4px;
  align-items: center;
  border-radius: var(--r-sm);
}
.sort-btn:hover {
  color: var(--text);
}
.sort-ind {
  opacity: 0.45;
  font-size: 11px;
}
th.sorted .sort-ind {
  opacity: 1;
  color: var(--accent);
}
tbody tr:last-child td {
  border-bottom: 0;
}
tbody tr.clickable {
  cursor: pointer;
}
tbody tr.clickable:hover td {
  background: var(--surface-alt);
}
tbody tr.clickable:focus-visible {
  outline: none;
}
tbody tr.clickable:focus-visible td {
  background: var(--accent-soft);
}
tbody tr.clickable:focus-visible td:first-child {
  box-shadow: inset 3px 0 0 var(--accent);
}
.empty-cell {
  text-align: center;
  color: var(--text-muted);
  padding: var(--s-8) var(--s-4);
  white-space: normal;
}
tfoot :deep(td) {
  font-weight: 700;
  border-top: 1px solid var(--border);
  border-bottom: 0;
}
.sk-row td {
  height: 44px;
}
@media (max-width: 1100px) {
  .hide-1100 {
    display: none;
  }
}
@media (max-width: 900px) {
  .hide-900 {
    display: none;
  }
}
@media (max-width: 640px) {
  .hide-640 {
    display: none;
  }
  th,
  td {
    padding: 10px 10px;
  }
}
</style>
