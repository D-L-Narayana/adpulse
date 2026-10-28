<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useSession } from '../stores/session'
import { markUpdated, useLiveRefetch } from '../stores/live'
import { loadDashboard, type PeriodDays } from '../lib/dashboard'
import { useAsync } from '../lib/useAsync'
import { fmtCompact, fmtInt, fmtMoney, fmtMoney2, fmtPct, fmtDate } from '../lib/format'
import type { CampaignSummary } from '../lib/types'
import KpiCard from '../components/KpiCard.vue'
import PacingChart from '../components/PacingChart.vue'
import ChannelBars from '../components/ChannelBars.vue'
import CampaignTable from '../components/CampaignTable.vue'
import ApiErrorState from '../components/ApiErrorState.vue'
import EmptyState from '../components/ui/EmptyState.vue'
import Card from '../components/ui/Card.vue'
import Button from '../components/ui/Button.vue'

const session = useSession()
const router = useRouter()
const slug = computed(() => session.slug.value ?? '')
const days = ref<PeriodDays>(30)

const dash = useAsync(() => loadDashboard(session.key.value!, days.value))
async function refresh(opts?: { silent?: boolean }) {
  if (!session.key.value) return
  const r = await dash.run(opts)
  if (r) markUpdated()
}
watch([() => session.key.value, days], () => void refresh({ silent: false }), { immediate: true })
useLiveRefetch(['metrics.upserted', 'campaign.changed', 'import.completed', 'pacing.alert'], () => refresh({ silent: true }))

const d = computed(() => dash.data.value)
const isEmpty = computed(() => !!d.value && d.value.campaigns.length === 0 && d.value.current.spend === 0 && d.value.current.impressions === 0)
const loading = computed(() => dash.loading.value && !d.value)

const utilisationValue = computed(() => {
  const u = d.value?.utilisation
  if (!u || u.percent === null) return '—'
  return fmtPct(u.percent, 0)
})
const utilisationSub = computed(() => {
  const u = d.value?.utilisation
  if (!u || u.percent === null) return 'no active flights'
  return `${fmtMoney(u.actual)} of ${fmtMoney(u.expected)} expected to date`
})
const pacingIssues = computed(() => (d.value?.campaigns ?? []).filter((c) => c.pacingStatus === 'over' || c.pacingStatus === 'under').length)

function open(c: CampaignSummary) {
  void router.push(`/t/${slug.value}/campaigns/${c.id}`)
}
const periodLabel = computed(() => (d.value ? `${fmtDate(d.value.period.from)} – ${fmtDate(d.value.period.to)}` : ''))
</script>

<template>
  <div class="page">
    <div class="page-head">
      <div>
        <h1>Dashboard</h1>
        <p class="lede">{{ periodLabel || 'Spend, pacing and performance across all channels' }}<template v-if="pacingIssues"> · <span class="issue">{{ pacingIssues }} campaign{{ pacingIssues === 1 ? '' : 's' }} off pace</span></template></p>
      </div>
      <div class="seg" role="radiogroup" aria-label="Period">
        <button v-for="p in [7, 30, 90] as PeriodDays[]" :key="p" type="button" role="radio" :aria-checked="days === p" class="segb" :class="{ on: days === p }" @click="days = p">{{ p }}d</button>
      </div>
    </div>

    <ApiErrorState v-if="dash.error.value && !d" :error="dash.error.value" :retrying="dash.loading.value" @retry="refresh({ silent: false })" />

    <template v-else>
      <EmptyState v-if="isEmpty" title="No data in this tenant yet" description="Import a Google Ads or Meta export, push metrics over REST, or load the attributed sample dataset. Everything below fills in live as rows arrive.">
        <Button variant="primary" :to="`/t/${slug}/import`">Import data</Button>
        <Button variant="ghost" :to="`/t/${slug}/import#api`">Use the API</Button>
      </EmptyState>

      <section class="grid-kpi" aria-label="Key metrics">
        <KpiCard label="Spend" :value="fmtMoney(d?.kpis.spend.value ?? 0)" :delta="d?.kpis.spend.delta ?? null" :spark="d?.sparks.spend" :loading="loading" />
        <KpiCard label="Budget utilisation" :value="utilisationValue" :sub="utilisationSub" hint="Actual spend to date divided by expected spend to date across all in-flight campaigns (100% = exactly on plan)." :loading="loading" />
        <KpiCard label="Impressions" :value="fmtCompact(d?.kpis.impressions.value ?? 0)" :delta="d?.kpis.impressions.delta ?? null" :spark="d?.sparks.impressions.some((v) => v > 0) ? d?.sparks.impressions : undefined" :loading="loading" />
        <KpiCard label="Clicks" :value="fmtInt(d?.kpis.clicks.value ?? 0)" :delta="d?.kpis.clicks.delta ?? null" :spark="d?.sparks.clicks" :loading="loading" />
        <KpiCard label="CTR" :value="fmtPct(d?.kpis.ctr.value ?? 0)" :delta="d?.kpis.ctr.delta ?? null" :loading="loading" />
        <KpiCard label="CPC" :value="fmtMoney2(d?.kpis.cpc.value ?? 0)" :delta="d?.kpis.cpc.delta ?? null" lower-is-better :loading="loading" />
        <KpiCard label="Conversions" :value="fmtInt(d?.kpis.conversions.value ?? 0)" :delta="d?.kpis.conversions.delta ?? null" :spark="d?.sparks.conversions" :loading="loading" />
        <KpiCard label="CPA" :value="fmtMoney2(d?.kpis.cpa.value ?? 0)" :delta="d?.kpis.cpa.delta ?? null" lower-is-better :loading="loading" />
      </section>

      <div class="grid-2">
        <Card title="Spend vs planned budget" :subtitle="`Cumulative over the last ${days} days`">
          <PacingChart :rows="d?.trend ?? []" :loading="loading" />
        </Card>
        <Card title="By channel" :subtitle="`Spend share, last ${days} days`">
          <ChannelBars :rows="d?.byChannel.channels ?? []" :total="d?.byChannel.total" :loading="loading" />
        </Card>
      </div>

      <Card title="Campaigns" subtitle="Lifetime KPIs and pacing against each campaign's own flight dates" :padded="false">
        <div class="table-pad">
          <CampaignTable :rows="d?.campaigns ?? []" :loading="loading" :import-to="`/t/${slug}/import`" @open="open" />
        </div>
      </Card>
    </template>
  </div>
</template>

<style scoped>
.issue {
  color: var(--warning);
  font-weight: 500;
}
.seg {
  display: inline-flex;
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  background: var(--surface-alt);
  padding: 2px;
  gap: 2px;
}
.segb {
  height: 30px;
  padding: 0 12px;
  border: 0;
  background: transparent;
  border-radius: 6px;
  font-size: var(--text-sm);
  font-weight: 500;
  color: var(--text-muted);
  cursor: pointer;
  font-variant-numeric: tabular-nums;
}
.segb.on {
  background: var(--accent);
  color: #fff;
}
.segb:not(.on):hover {
  color: var(--text);
}
.table-pad {
  padding-top: var(--s-4);
}
</style>
