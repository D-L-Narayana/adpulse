<script setup lang="ts">
import { computed, ref } from 'vue'
import { channelLabel, fmtInt, fmtMoney, fmtMoney2, fmtPct } from '../lib/format'
import type { CampaignSummary } from '../lib/types'
import Table, { type Column } from './ui/Table.vue'
import StatusBadge from './StatusBadge.vue'
import PacingBadge from './PacingBadge.vue'
import EmptyState from './ui/EmptyState.vue'
import Button from './ui/Button.vue'

const props = withDefaults(defineProps<{ rows: CampaignSummary[]; loading?: boolean; importTo?: string }>(), { loading: false })
const emit = defineEmits<{ open: [row: CampaignSummary] }>()

const q = ref('')
const status = ref<'all' | 'Active' | 'Paused' | 'Draft' | 'Completed'>('all')
const pacing = ref<'all' | 'over' | 'under' | 'on-track'>('all')
const channel = ref('all')

const channels = computed(() => Array.from(new Set(props.rows.map((r) => r.channel))).sort())

const filtered = computed(() =>
  props.rows.filter((r) => {
    if (status.value !== 'all' && r.status !== status.value) return false
    if (pacing.value !== 'all' && r.pacingStatus !== pacing.value) return false
    if (channel.value !== 'all' && r.channel !== channel.value) return false
    if (q.value.trim()) {
      const s = q.value.trim().toLowerCase()
      return r.name.toLowerCase().includes(s) || r.id.toLowerCase().startsWith(s) || channelLabel(r.channel).toLowerCase().includes(s)
    }
    return true
  }),
)

const counts = computed(() => ({
  over: props.rows.filter((r) => r.pacingStatus === 'over').length,
  under: props.rows.filter((r) => r.pacingStatus === 'under').length,
}))

const columns: Column<CampaignSummary>[] = [
  { key: 'name', label: 'Campaign', sortable: true },
  { key: 'status', label: 'Status', sortable: true, width: '110px' },
  { key: 'pacing', label: 'Pacing', sortable: true, value: (r) => (r.pacingStatus === 'not-started' ? -1 : r.pacingPercent), width: '150px' },
  { key: 'spend', label: 'Spend', align: 'right', sortable: true, format: (r) => fmtMoney(r.spend) },
  { key: 'dailyBudget', label: 'Budget / day', align: 'right', sortable: true, format: (r) => fmtMoney(r.dailyBudget), hideBelow: 1100 },
  { key: 'impressions', label: 'Impr.', align: 'right', sortable: true, format: (r) => fmtInt(r.impressions), hideBelow: 900 },
  { key: 'clicks', label: 'Clicks', align: 'right', sortable: true, format: (r) => fmtInt(r.clicks), hideBelow: 640 },
  { key: 'ctr', label: 'CTR', align: 'right', sortable: true, format: (r) => fmtPct(r.ctr), hideBelow: 1100 },
  { key: 'cpc', label: 'CPC', align: 'right', sortable: true, format: (r) => fmtMoney2(r.cpc), hideBelow: 900 },
  { key: 'conversions', label: 'Conv.', align: 'right', sortable: true, format: (r) => fmtInt(r.conversions), hideBelow: 640 },
]

function reset() {
  q.value = ''
  status.value = 'all'
  pacing.value = 'all'
  channel.value = 'all'
}
const hasFilters = computed(() => q.value || status.value !== 'all' || pacing.value !== 'all' || channel.value !== 'all')
</script>

<template>
  <div class="ct">
    <div class="toolbar" role="search">
      <label class="search">
        <span class="sr-only">Search campaigns</span>
        <input v-model="q" type="search" placeholder="Search by name, id or channel" autocomplete="off" />
      </label>
      <div class="filters">
        <label class="sel"><span class="sr-only">Status</span>
          <select v-model="status"><option value="all">All statuses</option><option>Active</option><option>Paused</option><option>Draft</option><option>Completed</option></select>
        </label>
        <label class="sel"><span class="sr-only">Pacing</span>
          <select v-model="pacing">
            <option value="all">Any pacing</option>
            <option value="over">Over pace ({{ counts.over }})</option>
            <option value="under">Under pace ({{ counts.under }})</option>
            <option value="on-track">On pace</option>
          </select>
        </label>
        <label v-if="channels.length > 1" class="sel"><span class="sr-only">Channel</span>
          <select v-model="channel"><option value="all">All channels</option><option v-for="c in channels" :key="c" :value="c">{{ channelLabel(c) }}</option></select>
        </label>
        <button v-if="hasFilters" type="button" class="clear" @click="reset">Clear</button>
      </div>
      <p class="count num" aria-live="polite">{{ loading ? '' : `${filtered.length} of ${rows.length}` }}</p>
    </div>

    <Table :columns="columns" :rows="filtered" :row-key="(r) => r.id" :loading="loading" :default-sort="{ key: 'spend', dir: 'desc' }" clickable caption="Campaigns with lifetime KPIs and pacing status" @row-click="emit('open', $event)">
      <template #cell-name="{ row }">
        <div class="namecell">
          <span class="nm">{{ row.name }}</span>
          <span class="meta mono">{{ channelLabel(row.channel) }} · {{ row.id.slice(0, 8) }}</span>
        </div>
      </template>
      <template #cell-status="{ row }"><StatusBadge :status="row.status" /></template>
      <template #cell-pacing="{ row }">
        <PacingBadge :status="row.pacingStatus" :percent="row.pacingPercent" :expected="row.expectedSpendToDate" :actual="row.actualSpendToDate" :campaign-status="row.status" detail />
      </template>
      <template #empty>
        <EmptyState v-if="rows.length === 0" title="No campaigns yet" description="Campaigns are created automatically when you import a CSV or push metrics to the API." compact>
          <Button v-if="importTo" variant="primary" :to="importTo">Import data</Button>
        </EmptyState>
        <span v-else>No campaigns match these filters. <button type="button" class="link" @click="reset">Clear filters</button></span>
      </template>
    </Table>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  gap: var(--s-3);
  align-items: center;
  flex-wrap: wrap;
  padding: 0 var(--s-5) var(--s-4);
}
.search {
  flex: 1 1 220px;
  min-width: 0;
}
.search input {
  width: 100%;
  height: 36px;
  padding: 0 12px;
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  background: #fff;
  font-size: var(--text-sm);
}
.search input:focus-visible {
  outline: none;
  border-color: var(--accent);
  box-shadow: 0 0 0 3px var(--accent-ring);
}
.filters {
  display: flex;
  gap: var(--s-2);
  flex-wrap: wrap;
  align-items: center;
}
select {
  height: 36px;
  padding: 0 28px 0 10px;
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  background: #fff url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='10' height='6' viewBox='0 0 10 6'><path d='M1 1l4 4 4-4' fill='none' stroke='%237A7974' stroke-width='1.5'/></svg>") no-repeat right 10px center;
  appearance: none;
  font-size: var(--text-sm);
  color: var(--text);
  cursor: pointer;
}
select:focus-visible {
  outline: none;
  border-color: var(--accent);
  box-shadow: 0 0 0 3px var(--accent-ring);
}
.clear,
.link {
  background: none;
  border: 0;
  padding: 0 4px;
  color: var(--accent);
  font-size: var(--text-sm);
  cursor: pointer;
  text-decoration: underline;
  text-underline-offset: 2px;
}
.count {
  font-size: var(--text-xs);
  color: var(--text-muted);
  margin-left: auto;
  min-width: 60px;
  text-align: right;
}
.namecell {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 160px;
  max-width: 320px;
}
.nm {
  font-weight: 500;
  white-space: normal;
  line-height: 1.3;
}
.meta {
  font-size: var(--text-xs);
  color: var(--text-muted);
}
@media (max-width: 640px) {
  .toolbar {
    padding: 0 var(--s-4) var(--s-3);
  }
  .count {
    display: none;
  }
}
</style>
