<script setup lang="ts">
/**
 * Honest preview of the real app shell: the actual components in their empty/awaiting states.
 * No invented numbers — the layout fills in once a tenant imports data.
 */
import KpiCard from './KpiCard.vue'

const kpis = ['Spend', 'Budget utilisation', 'Impressions', 'Clicks']
const nav = ['Dashboard', 'Import', 'Alerts', 'Settings']
</script>

<template>
  <div class="prev" aria-label="Preview of the AdPulse dashboard layout, awaiting data" role="img">
    <div class="side">
      <span class="logo"></span>
      <div class="tn"><span class="l1"></span><span class="l2"></span></div>
      <ul class="nav"><li v-for="(n, i) in nav" :key="n" :class="{ on: i === 0 }">{{ n }}</li></ul>
      <span class="live"><span class="dot"></span>Live · updated 2s ago</span>
    </div>
    <div class="main">
      <div class="head"><span class="h">Dashboard</span><span class="seg"><b>7d</b><b class="on">30d</b><b>90d</b></span></div>
      <div class="kpis">
        <KpiCard v-for="k in kpis" :key="k" :label="k" value="—" sub="awaiting data" />
      </div>
      <div class="two">
        <div class="card chart">
          <span class="ct">Spend vs planned budget</span>
          <div class="axes">
            <span v-for="i in 4" :key="i" class="grid"></span>
            <span class="lbl">your cumulative spend appears here</span>
          </div>
        </div>
        <div class="card">
          <span class="ct">By channel</span>
          <div class="bars"><span v-for="i in 3" :key="i" class="bar"></span></div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.prev {
  display: grid;
  grid-template-columns: 148px minmax(0, 1fr);
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: var(--r-lg);
  overflow: hidden;
  box-shadow: var(--shadow-md);
  font-size: var(--text-xs);
  user-select: none;
}
.side {
  background: var(--surface);
  border-right: 1px solid var(--border);
  padding: 14px 12px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.logo {
  width: 16px;
  height: 16px;
  border-radius: 4px;
  background: var(--accent);
}
.tn {
  display: flex;
  flex-direction: column;
  gap: 5px;
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 6px;
}
.l1,
.l2 {
  display: block;
  height: 7px;
  border-radius: 3px;
  background: var(--border);
}
.l1 {
  width: 70%;
}
.l2 {
  width: 50%;
  background: var(--border-soft);
}
.nav {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.nav li {
  padding: 6px 8px;
  border-radius: 6px;
  color: var(--text-muted);
  font-weight: 500;
}
.nav li.on {
  background: var(--accent-soft);
  color: var(--accent);
}
.live {
  margin-top: auto;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: var(--text-muted);
}
.dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--success);
}
.main {
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}
.head {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.h {
  font-weight: 700;
  font-size: var(--text-md);
}
.seg {
  display: inline-flex;
  gap: 2px;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 2px;
  background: var(--surface-alt);
}
.seg b {
  font-weight: 500;
  padding: 2px 8px;
  border-radius: 4px;
  color: var(--text-muted);
}
.seg b.on {
  background: var(--accent);
  color: #fff;
}
.kpis {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 8px;
}
.kpis :deep(.kpi) {
  padding: 10px 12px;
}
.kpis :deep(.value) {
  font-size: 20px;
}
.kpis :deep(.label) {
  font-size: 10px;
  margin-bottom: 4px;
}
.kpis :deep(.delta) {
  font-size: 10px;
}
.two {
  display: grid;
  grid-template-columns: 3fr 2fr;
  gap: 8px;
}
.card {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 120px;
}
.ct {
  font-weight: 700;
}
.axes {
  position: relative;
  flex: 1;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  min-height: 80px;
}
.grid {
  height: 1px;
  background: var(--border-soft);
}
.lbl {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  color: var(--text-faint);
}
.bars {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-top: 6px;
}
.bar {
  height: 8px;
  border-radius: 4px;
  background: var(--surface-sunken);
}
@media (max-width: 640px) {
  .prev {
    grid-template-columns: minmax(0, 1fr);
  }
  .side {
    display: none;
  }
  .kpis {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
  .two {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
