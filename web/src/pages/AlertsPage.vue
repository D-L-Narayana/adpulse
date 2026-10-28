<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { api, ApiError } from '../lib/api'
import { useSession } from '../stores/session'
import { useAlerts, markAlertsSeen, clearAlerts } from '../stores/alerts'
import { markUpdated, useLiveRefetch, useLive } from '../stores/live'
import { useAsync, optional } from '../lib/useAsync'
import { channelLabel, fmtDateTime, fmtMoney } from '../lib/format'
import type { Pacing } from '../lib/types'
import Card from '../components/ui/Card.vue'
import Button from '../components/ui/Button.vue'
import EmptyState from '../components/ui/EmptyState.vue'
import Skeleton from '../components/ui/Skeleton.vue'
import PacingBadge from '../components/PacingBadge.vue'
import ApiErrorState from '../components/ApiErrorState.vue'

const session = useSession()
const slug = computed(() => session.slug.value ?? '')
const alerts = useAlerts(slug.value)
const live = useLive()

/** Current off-pace campaigns straight from the API (the "now" view), independent of the event history. */
const current = useAsync(async () => {
  const key = session.key.value!
  const rows = await optional(api.reportPacing(key), null as Pacing[] | null, (e) => e instanceof ApiError && e.kind === 'not-found')
  if (rows) return rows
  // fallback: derive from the campaign summary
  const cs = await api.campaignSummary(key)
  return cs.map<Pacing>((c) => ({
    campaignId: c.id,
    campaignName: c.name,
    channel: c.channel,
    asOf: '',
    daysElapsed: 0,
    daysTotal: 0,
    dailyBudget: c.dailyBudget,
    expectedSpendToDate: c.expectedSpendToDate ?? 0,
    actualSpendToDate: c.actualSpendToDate ?? 0,
    pacingPercent: c.pacingPercent,
    status: c.pacingStatus,
    deviation: 0,
    remainingBudget: 0,
    suggestedDailyBudget: c.suggestedDailyBudget,
  }))
})
async function refresh(opts?: { silent?: boolean }) {
  if (!session.key.value) return
  const r = await current.run(opts)
  if (r) markUpdated()
}
watch(() => session.key.value, () => void refresh({ silent: false }), { immediate: true })
useLiveRefetch(['metrics.upserted', 'campaign.changed', 'import.completed', 'pacing.alert'], () => refresh({ silent: true }))

const offPace = computed(() => (current.data.value ?? []).filter((p) => p.status === 'over' || p.status === 'under').sort((a, b) => Math.abs(b.pacingPercent - 100) - Math.abs(a.pacingPercent - 100)))

onMounted(() => markAlertsSeen(slug.value))
watch(() => alerts.items.length, () => markAlertsSeen(slug.value))

const streamNote = computed(() => {
  if (live.mode === 'sse' && live.sse === 'open') return 'Listening for pacing.alert events on the live stream.'
  if (live.mode === 'polling') return 'Live stream unavailable — the current view refreshes every 10 s; new alert events will appear once the stream reconnects.'
  return 'Connecting to the live stream…'
})
</script>

<template>
  <div class="page">
    <div class="page-head">
      <div>
        <h1>Pacing alerts</h1>
        <p class="lede">{{ streamNote }}</p>
      </div>
    </div>

    <Card title="Off pace right now" :subtitle="current.data.value ? `${offPace.length} of ${current.data.value.length} campaigns outside the ±10% band` : 'Computed by the API against each flight'">
      <ApiErrorState v-if="current.error.value && !current.data.value" :error="current.error.value" :retrying="current.loading.value" compact @retry="refresh({ silent: false })" />
      <div v-else-if="current.loading.value && !current.data.value" class="stack"><Skeleton v-for="i in 3" :key="i" height="56px" radius="8px" /></div>
      <p v-else-if="!offPace.length" class="ok">Every in-flight campaign is within ±10% of its expected spend.</p>
      <ul v-else class="list">
        <li v-for="p in offPace" :key="p.campaignId" class="item">
          <div class="who">
            <router-link :to="`/t/${slug}/campaigns/${p.campaignId}`" class="name">{{ p.campaignName ?? p.campaignId }}</router-link>
            <span class="meta">{{ channelLabel(p.channel) }}<template v-if="p.daysTotal"> · day {{ p.daysElapsed }} of {{ p.daysTotal }}</template></span>
          </div>
          <PacingBadge :status="p.status" :percent="p.pacingPercent" :expected="p.expectedSpendToDate" :actual="p.actualSpendToDate" detail />
          <span v-if="p.suggestedDailyBudget" class="sugg num">Suggest {{ fmtMoney(p.suggestedDailyBudget) }}/day</span>
        </li>
      </ul>
    </Card>

    <Card title="Alert history" :subtitle="alerts.items.length ? `${alerts.items.length} event${alerts.items.length === 1 ? '' : 's'} received in this browser` : 'pacing.alert events received over the stream are kept in this browser'">
      <template #actions>
        <Button v-if="alerts.items.length" size="sm" variant="ghost" @click="clearAlerts(slug)">Clear history</Button>
      </template>
      <EmptyState v-if="!alerts.items.length" title="No alerts received yet" description="When an import or metrics push moves a campaign outside its ±10% pacing band, the API emits a pacing.alert and it lands here instantly." compact>
        <Button variant="primary" :to="`/t/${slug}/import`">Import data</Button>
      </EmptyState>
      <ol v-else class="feed">
        <li v-for="a in alerts.items" :key="a.id" class="ev">
          <time class="when num" :datetime="a.at">{{ fmtDateTime(a.at) }}</time>
          <div class="body">
            <div class="row wrap">
              <router-link v-if="a.campaignId" :to="`/t/${slug}/campaigns/${a.campaignId}`" class="name">{{ a.campaignName ?? a.campaignId }}</router-link>
              <span v-else class="name">{{ a.campaignName ?? 'Campaign' }}</span>
              <PacingBadge :status="a.status" :percent="a.pacingPercent ?? 0" :expected="a.expectedSpendToDate" :actual="a.actualSpendToDate" />
            </div>
            <p v-if="a.message" class="msg">{{ a.message }}</p>
            <p v-else-if="a.expectedSpendToDate !== undefined" class="msg">{{ fmtMoney(a.actualSpendToDate ?? 0) }} spent vs {{ fmtMoney(a.expectedSpendToDate) }} expected{{ a.channel ? ` · ${channelLabel(a.channel)}` : '' }}</p>
          </div>
        </li>
      </ol>
    </Card>
  </div>
</template>

<style scoped>
.ok {
  color: var(--success);
  font-weight: 500;
  font-size: var(--text-sm);
  padding: var(--s-4);
  background: var(--success-soft);
  border-radius: var(--r-md);
}
.list,
.feed {
  list-style: none;
  margin: 0;
  padding: 0;
}
.item {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  gap: var(--s-4);
  align-items: center;
  padding: var(--s-3) 0;
  border-bottom: 1px solid var(--border-soft);
}
.item:last-child {
  border-bottom: 0;
}
.who {
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.name {
  font-weight: 500;
  color: var(--text);
  text-decoration: none;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.name:hover {
  color: var(--accent);
  text-decoration: underline;
}
.meta,
.msg {
  font-size: var(--text-xs);
  color: var(--text-muted);
}
.sugg {
  font-size: var(--text-sm);
  color: var(--text);
  white-space: nowrap;
}
.ev {
  display: grid;
  grid-template-columns: 120px minmax(0, 1fr);
  gap: var(--s-4);
  padding: var(--s-3) 0;
  border-bottom: 1px solid var(--border-soft);
}
.ev:last-child {
  border-bottom: 0;
}
.when {
  font-size: var(--text-xs);
  color: var(--text-muted);
  padding-top: 3px;
}
.msg {
  margin-top: 4px;
}
@media (max-width: 640px) {
  .item {
    grid-template-columns: minmax(0, 1fr);
    gap: var(--s-2);
  }
  .ev {
    grid-template-columns: minmax(0, 1fr);
    gap: 4px;
  }
}
</style>
