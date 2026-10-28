<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { api, ApiError, describeError } from '../lib/api'
import { useSession } from '../stores/session'
import { markUpdated, useLiveRefetch } from '../stores/live'
import { notify } from '../stores/toast'
import { useAsync, optional } from '../lib/useAsync'
import { channelLabel, fmtDateLong, fmtInt, fmtMoney, fmtMoney2, fmtPct, fmtDate } from '../lib/format'
import { projectFlight } from '../lib/pacing'
import type { Campaign, MetricRow, Pacing, TrendRow } from '../lib/types'
import Card from '../components/ui/Card.vue'
import Button from '../components/ui/Button.vue'
import Dialog from '../components/ui/Dialog.vue'
import Skeleton from '../components/ui/Skeleton.vue'
import Table, { type Column } from '../components/ui/Table.vue'
import StatusBadge from '../components/StatusBadge.vue'
import PacingBadge from '../components/PacingBadge.vue'
import PacingChart from '../components/PacingChart.vue'
import ApiErrorState from '../components/ApiErrorState.vue'
import EmptyState from '../components/ui/EmptyState.vue'

const route = useRoute()
const session = useSession()
const slug = computed(() => session.slug.value ?? '')
const id = computed(() => String(route.params.id))

interface Detail {
  campaign: Campaign
  metrics: MetricRow[]
  pacing: Pacing | null
}
const detail = useAsync<Detail>(async () => {
  const key = session.key.value!
  const [campaign, metrics, pacing] = await Promise.all([
    api.campaign(key, id.value),
    api.campaignMetrics(key, id.value),
    optional(api.campaignPacing(key, id.value), null as Pacing | null, (e) => e instanceof ApiError && e.kind === 'not-found'),
  ])
  return { campaign, metrics, pacing }
})
async function refresh(opts?: { silent?: boolean }) {
  if (!session.key.value) return
  const r = await detail.run(opts)
  if (r) markUpdated()
}
watch([() => session.key.value, id], () => void refresh({ silent: false }), { immediate: true })
useLiveRefetch(['metrics.upserted', 'campaign.changed', 'import.completed', 'pacing.alert'], () => refresh({ silent: true }))

const d = computed(() => detail.data.value)
const c = computed(() => d.value?.campaign)
const loading = computed(() => detail.loading.value && !d.value)

/** Trend rows for the chart: daily spend + the campaign's own daily budget while in flight. */
const trend = computed<TrendRow[]>(() => {
  if (!d.value) return []
  const cmp = d.value.campaign
  return d.value.metrics.map((m) => ({
    date: m.date.slice(0, 10),
    spend: m.spend,
    revenue: m.revenue,
    impressions: m.impressions,
    clicks: m.clicks,
    conversions: m.conversions,
    budget: cmp.startDate <= m.date.slice(0, 10) && m.date.slice(0, 10) <= cmp.endDate ? cmp.dailyBudget : 0,
  }))
})

const totals = computed(() => {
  const m = d.value?.metrics ?? []
  const t = m.reduce((a, r) => ({ spend: a.spend + r.spend, impressions: a.impressions + r.impressions, clicks: a.clicks + r.clicks, conversions: a.conversions + r.conversions, revenue: a.revenue + r.revenue }), { spend: 0, impressions: 0, clicks: 0, conversions: 0, revenue: 0 })
  return { ...t, ctr: t.impressions ? (t.clicks / t.impressions) * 100 : 0, cpc: t.clicks ? t.spend / t.clicks : 0, cpa: t.conversions ? t.spend / t.conversions : 0 }
})

const projection = computed(() => {
  const p = d.value?.pacing
  if (!p) return null
  return projectFlight({ dailyBudget: p.dailyBudget, daysElapsed: p.daysElapsed, daysTotal: p.daysTotal, actualSpendToDate: p.actualSpendToDate })
})

/* ----- lifecycle ----- */
type Action = 'activate' | 'pause' | 'complete'
const actionsFor = (status: string): Action[] =>
  status === 'Draft' ? ['activate'] : status === 'Active' ? ['pause', 'complete'] : status === 'Paused' ? ['activate', 'complete'] : []
const actionMeta: Record<Action, { label: string; title: string; body: string; danger: boolean }> = {
  activate: { label: 'Activate', title: 'Activate this campaign?', body: 'Pacing starts counting from the flight start date. You can pause it again at any time.', danger: false },
  pause: { label: 'Pause', title: 'Pause this campaign?', body: 'Metrics can still be imported while paused; pacing keeps using the original flight dates.', danger: false },
  complete: { label: 'Complete', title: 'Mark as completed?', body: 'Completed is terminal — the campaign becomes read-only and cannot be re-activated.', danger: true },
}
const pending = ref<Action | null>(null)
const busy = ref(false)
async function confirmAction() {
  if (!pending.value || !session.key.value || !c.value) return
  busy.value = true
  const a = pending.value
  try {
    const updated = await api.transition(session.key.value, c.value.id, a)
    notify.success(`${updated.name} is now ${updated.status}`)
    pending.value = null
    await refresh({ silent: true })
  } catch (e) {
    notify.error(`Could not ${a} campaign`, describeError(e))
  } finally {
    busy.value = false
  }
}

const metricColumns: Column<MetricRow>[] = [
  { key: 'date', label: 'Date', sortable: true, format: (r) => fmtDateLong(r.date) },
  { key: 'spend', label: 'Spend', align: 'right', sortable: true, format: (r) => fmtMoney2(r.spend) },
  { key: 'impressions', label: 'Impressions', align: 'right', sortable: true, format: (r) => fmtInt(r.impressions), hideBelow: 640 },
  { key: 'clicks', label: 'Clicks', align: 'right', sortable: true, format: (r) => fmtInt(r.clicks) },
  { key: 'ctr', label: 'CTR', align: 'right', value: (r) => (r.impressions ? (r.clicks / r.impressions) * 100 : 0), format: (r) => fmtPct(r.impressions ? (r.clicks / r.impressions) * 100 : 0), hideBelow: 900 },
  { key: 'conversions', label: 'Conv.', align: 'right', sortable: true, format: (r) => fmtInt(r.conversions) },
  { key: 'revenue', label: 'Revenue', align: 'right', sortable: true, format: (r) => fmtMoney2(r.revenue), hideBelow: 900 },
]
</script>

<template>
  <div class="page">
    <nav class="crumbs" aria-label="Breadcrumb">
      <router-link :to="`/t/${slug}`">Dashboard</router-link><span aria-hidden="true"> / </span><span>Campaign</span>
    </nav>

    <ApiErrorState v-if="detail.error.value && !d" :error="detail.error.value" :retrying="detail.loading.value" @retry="refresh({ silent: false })" />

    <template v-else>
      <div class="page-head">
        <div class="title">
          <template v-if="loading"><Skeleton width="320px" height="28px" /><Skeleton width="220px" height="14px" style="margin-top: 8px" /></template>
          <template v-else-if="c">
            <div class="row wrap">
              <h1>{{ c.name }}</h1>
              <StatusBadge :status="c.status" />
            </div>
            <p class="lede">{{ channelLabel(c.channel) }} · {{ fmtDateLong(c.startDate) }} → {{ fmtDateLong(c.endDate) }} · {{ fmtMoney(c.dailyBudget) }}/day · <span class="mono">{{ c.id }}</span></p>
          </template>
        </div>
        <div v-if="c && actionsFor(c.status).length" class="actions">
          <Button v-for="a in actionsFor(c.status)" :key="a" :variant="a === 'complete' ? 'danger' : a === 'activate' ? 'primary' : 'secondary'" @click="pending = a">{{ actionMeta[a].label }}</Button>
        </div>
      </div>

      <div class="grid-2">
        <Card title="Pacing projection" subtitle="Linear expectation against the flight; ±10% is on pace">
          <div v-if="loading" class="stack"><Skeleton height="22px" width="40%" /><Skeleton height="80px" /></div>
          <template v-else-if="d?.pacing">
            <div class="pace-top">
              <PacingBadge :status="d.pacing.status" :percent="d.pacing.pacingPercent" :expected="d.pacing.expectedSpendToDate" :actual="d.pacing.actualSpendToDate" :campaign-status="c?.status" detail />
              <p class="days num">Day {{ d.pacing.daysElapsed }} of {{ d.pacing.daysTotal }}</p>
            </div>
            <div class="bar-wrap" role="img" :aria-label="`Actual ${fmtMoney(d.pacing.actualSpendToDate)} versus expected ${fmtMoney(d.pacing.expectedSpendToDate)} to date`">
              <div class="bar">
                <div class="fill" :class="d.pacing.status" :style="{ width: Math.min(100, (d.pacing.pacingPercent / 200) * 100) + '%' }"></div>
                <div class="band"></div>
                <div class="mark"></div>
              </div>
              <div class="bar-labels num"><span>0%</span><span>100% expected</span><span>200%</span></div>
            </div>
            <dl class="facts">
              <div><dt>Expected to date</dt><dd class="num">{{ fmtMoney(d.pacing.expectedSpendToDate) }}</dd></div>
              <div><dt>Actual to date</dt><dd class="num">{{ fmtMoney(d.pacing.actualSpendToDate) }}</dd></div>
              <div><dt>Deviation</dt><dd class="num" :class="d.pacing.deviation > 0 ? 'bad' : d.pacing.deviation < 0 ? 'warn' : ''">{{ d.pacing.deviation > 0 ? '+' : '' }}{{ fmtMoney(d.pacing.deviation) }}</dd></div>
              <div><dt>Remaining budget</dt><dd class="num">{{ fmtMoney(d.pacing.remainingBudget) }}</dd></div>
              <div v-if="projection"><dt>Projected total at this run-rate</dt><dd class="num">{{ fmtMoney(projection.projected) }} <span class="muted">of {{ fmtMoney(projection.totalBudget) }} ({{ fmtPct(projection.projectedPercent, 0) }})</span></dd></div>
              <div><dt>Suggested daily budget</dt><dd class="num strong">{{ d.pacing.suggestedDailyBudget ? fmtMoney2(d.pacing.suggestedDailyBudget) : '—' }} <span v-if="c && d.pacing.suggestedDailyBudget" class="muted">vs {{ fmtMoney2(c.dailyBudget) }} now</span></dd></div>
            </dl>
          </template>
          <p v-else class="muted small">The API did not return pacing for this campaign.</p>
        </Card>

        <Card title="Lifetime performance" subtitle="Totals over every daily row">
          <div v-if="loading" class="stack"><Skeleton height="60px" /><Skeleton height="60px" /></div>
          <dl v-else class="kgrid">
            <div><dt>Spend</dt><dd class="num">{{ fmtMoney(totals.spend) }}</dd></div>
            <div><dt>Impressions</dt><dd class="num">{{ fmtInt(totals.impressions) }}</dd></div>
            <div><dt>Clicks</dt><dd class="num">{{ fmtInt(totals.clicks) }}</dd></div>
            <div><dt>CTR</dt><dd class="num">{{ fmtPct(totals.ctr) }}</dd></div>
            <div><dt>CPC</dt><dd class="num">{{ fmtMoney2(totals.cpc) }}</dd></div>
            <div><dt>Conversions</dt><dd class="num">{{ fmtInt(totals.conversions) }}</dd></div>
            <div><dt>CPA</dt><dd class="num">{{ fmtMoney2(totals.cpa) }}</dd></div>
            <div><dt>Revenue</dt><dd class="num">{{ fmtMoney(totals.revenue) }}</dd></div>
          </dl>
        </Card>
      </div>

      <Card title="Daily spend vs budget" :subtitle="c ? `Cumulative since ${fmtDate(c.startDate)}` : ''">
        <PacingChart :rows="trend" :loading="loading" :height="240" />
      </Card>

      <Card title="Daily rows" :subtitle="d ? `${d.metrics.length} day${d.metrics.length === 1 ? '' : 's'} of metrics` : ''" :padded="false">
        <div style="padding-top: 12px">
          <Table :columns="metricColumns" :rows="d?.metrics ?? []" :row-key="(r) => r.date" :loading="loading" :default-sort="{ key: 'date', dir: 'desc' }" dense caption="Daily metrics for this campaign">
            <template #empty>
              <EmptyState title="No daily rows yet" description="Import a CSV or push metrics for this campaign to see its daily history." compact>
                <Button variant="primary" :to="`/t/${slug}/import`">Import data</Button>
              </EmptyState>
            </template>
          </Table>
        </div>
      </Card>
    </template>

    <Dialog :open="!!pending" :title="pending ? actionMeta[pending].title : ''" :description="pending ? actionMeta[pending].body : ''" :danger="pending === 'complete'" @close="pending = null">
      <template #footer>
        <Button variant="ghost" @click="pending = null">Cancel</Button>
        <Button :variant="pending === 'complete' ? 'danger' : 'primary'" :loading="busy" data-autofocus @click="confirmAction">{{ pending ? actionMeta[pending].label : '' }}</Button>
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.crumbs {
  font-size: var(--text-sm);
  color: var(--text-muted);
}
.title h1 {
  font-size: var(--text-xl);
  overflow-wrap: anywhere;
}
.actions {
  display: flex;
  gap: var(--s-2);
  flex-wrap: wrap;
}
.pace-top {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--s-3);
  margin-bottom: var(--s-4);
}
.days {
  font-size: var(--text-sm);
  color: var(--text-muted);
  white-space: nowrap;
}
.bar-wrap {
  margin-bottom: var(--s-4);
}
.bar {
  position: relative;
  height: 12px;
  background: var(--surface-sunken);
  border-radius: 6px;
  overflow: hidden;
}
.fill {
  height: 100%;
  border-radius: 6px;
  background: var(--success);
  transition: width var(--dur-slow) var(--ease);
}
.fill.over {
  background: var(--error);
}
.fill.under {
  background: var(--warning);
}
.fill.not-started {
  background: var(--text-faint);
}
.band {
  position: absolute;
  top: 0;
  bottom: 0;
  left: 45%;
  width: 10%;
  background: rgba(67, 122, 34, 0.14);
  pointer-events: none;
}
.mark {
  position: absolute;
  top: -2px;
  bottom: -2px;
  left: 50%;
  width: 2px;
  background: var(--text);
}
.bar-labels {
  display: flex;
  justify-content: space-between;
  font-size: var(--text-xs);
  color: var(--text-muted);
  margin-top: 4px;
}
.facts,
.kgrid {
  margin: 0;
  display: grid;
  gap: var(--s-3) var(--s-4);
}
.facts {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}
.kgrid {
  grid-template-columns: repeat(4, minmax(0, 1fr));
}
.facts dt,
.kgrid dt {
  font-size: var(--text-xs);
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  font-weight: 500;
}
.facts dd,
.kgrid dd {
  margin: 2px 0 0;
  font-size: var(--text-md);
  font-weight: 500;
}
.kgrid dd {
  font-size: var(--text-lg);
  font-weight: 700;
}
dd.strong {
  font-weight: 700;
}
dd.bad {
  color: var(--error);
}
dd.warn {
  color: var(--warning);
}
@media (max-width: 640px) {
  .kgrid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
