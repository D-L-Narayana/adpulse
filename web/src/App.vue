<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { DEMO_TENANTS, defaultApiKey, isLiveConfigured, loadDashboard, rememberApiKey, transition } from './api'
import type { Dashboard, DataSource } from './types'
import { fmtDate, fmtInt, fmtMoney, fmtMoney2, fmtPct, fmtX } from './format'
import KpiCard from './components/KpiCard.vue'
import SpendChart from './components/SpendChart.vue'
import ChannelTable from './components/ChannelTable.vue'
import CampaignTable from './components/CampaignTable.vue'

const apiKey = ref(defaultApiKey())
const data = ref<Dashboard | null>(null)
const source = ref<DataSource>({ kind: 'demo', reason: 'loading' })
const loading = ref(true)
const error = ref<string | null>(null)
const busy = ref<string | null>(null)
const refreshedAt = ref<Date | null>(null)

async function refresh() {
  loading.value = true
  error.value = null
  const result = await loadDashboard(apiKey.value)
  data.value = result.data
  source.value = result.source
  refreshedAt.value = new Date()
  loading.value = false
}

async function onAction(id: string, action: 'activate' | 'pause' | 'complete') {
  if (source.value.kind !== 'live') return
  busy.value = id
  try {
    await transition(id, action, apiKey.value)
    await refresh()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Request failed'
  } finally {
    busy.value = null
  }
}

function switchTenant(key: string) {
  apiKey.value = key
  rememberApiKey(key)
  refresh()
}

const total = computed(() => data.value?.report.total)
const attention = computed(() => data.value?.summary.filter((c) => c.status === 'Active' && (c.pacingStatus === 'over' || c.pacingStatus === 'under')) ?? [])
const activeCount = computed(() => data.value?.summary.filter((c) => c.status === 'Active').length ?? 0)
const tenantLabel = computed(() => DEMO_TENANTS.find((t) => t.key === apiKey.value)?.label ?? data.value?.me.name ?? 'Tenant')
const isLive = computed(() => source.value.kind === 'live')

onMounted(refresh)
</script>

<template>
  <div class="app">
    <header class="topbar">
      <div class="brand">
        <div class="brand-mark">
          <svg width="22" height="22" viewBox="0 0 32 32"><path d="M6 20l5-8 4 6 4-10 7 12" fill="none" stroke="#22d3ee" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" /></svg>
        </div>
        <div>
          <h1>AdPulse</h1>
          <p>Campaign performance &amp; budget pacing · {{ data?.me.name ?? tenantLabel }}</p>
        </div>
      </div>
      <div class="controls">
        <span class="source-pill" :class="isLive ? 'live' : 'demo'" :title="source.kind === 'demo' ? source.reason : source.baseUrl">
          <span class="dot"></span>
          <template v-if="isLive">Live API <small>{{ (source as any).baseUrl.replace(/^https?:\/\//, '') }}</small></template>
          <template v-else>Demo data <small>bundled snapshot</small></template>
        </span>
        <select class="select" :value="apiKey" @change="switchTenant(($event.target as HTMLSelectElement).value)" aria-label="Tenant">
          <option v-for="t in DEMO_TENANTS" :key="t.key" :value="t.key">{{ t.label }}</option>
        </select>
        <button class="btn" :disabled="loading" @click="refresh">{{ loading ? 'Loading…' : 'Refresh' }}</button>
      </div>
    </header>

    <div v-if="source.kind === 'demo' && isLiveConfigured && !loading" class="banner warn">
      ⚠ Could not reach the API ({{ source.reason }}) — showing the bundled demo snapshot. Lifecycle actions need the live API.
    </div>
    <div v-if="error" class="banner bad">✕ {{ error }}</div>

    <template v-if="data && total">
      <section class="grid-kpi">
        <KpiCard label="Spend (30d)" :value="fmtMoney(total.spend)" :sub="`${fmtDate(data.report.from)} – ${fmtDate(data.report.to)}`" />
        <KpiCard label="Revenue" :value="fmtMoney(total.revenue)" :sub="`${fmtInt(total.conversions)} conversions`" />
        <KpiCard label="ROAS" :value="fmtX(total.roas)" sub="revenue ÷ spend" />
        <KpiCard label="CTR" :value="fmtPct(total.ctr)" :sub="`${fmtInt(total.clicks)} clicks`" />
        <KpiCard label="CPC" :value="fmtMoney2(total.cpc)" sub="cost per click" />
        <KpiCard label="CPA" :value="fmtMoney2(total.cpa)" sub="cost per acquisition" />
      </section>

      <section class="grid-2">
        <div class="card">
          <div class="card-head"><h2>Daily spend vs revenue</h2><span>{{ data.daily.length }} days · all channels</span></div>
          <div class="card-body"><SpendChart :rows="data.daily" /></div>
        </div>
        <div class="card">
          <div class="card-head"><h2>Channel mix</h2><span>server-side KPIs</span></div>
          <div class="card-body" style="padding-top: 8px"><ChannelTable :report="data.report" /></div>
        </div>
      </section>

      <section class="card">
        <div class="card-head" style="padding-bottom: 14px">
          <h2>Campaigns</h2>
          <span>
            {{ activeCount }} active ·
            <strong :style="{ color: attention.length ? '#b45309' : '#166534' }">{{ attention.length }} need{{ attention.length === 1 ? 's' : '' }} budget attention</strong>
            · pacing band ±10%
          </span>
        </div>
        <CampaignTable :rows="data.summary" :live="isLive" :busy="busy" @action="onAction" />
      </section>
    </template>

    <template v-else>
      <section class="grid-kpi"><div v-for="i in 6" :key="i" class="card kpi"><div class="skeleton" style="height: 64px"></div></div></section>
      <div class="card"><div class="skeleton" style="height: 320px; margin: 20px"></div></div>
    </template>

    <footer class="footer">
      <span>ASP.NET Core 8 · EF Core · SQL Server · Vue 3 + Vite + TypeScript</span>
      <a href="https://github.com/D-L-Narayana/adpulse" target="_blank" rel="noopener">Source</a>
      <span v-if="refreshedAt">Updated {{ refreshedAt.toLocaleTimeString() }}</span>
    </footer>
  </div>
</template>
