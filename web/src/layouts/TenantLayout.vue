<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSession } from '../stores/session'
import { connectLive, disconnectLive } from '../stores/live'
import { useAlerts, unreadCount } from '../stores/alerts'
import { rememberKey } from '../stores/session'
import { maskKey } from '../lib/format'
import LiveIndicator from '../components/LiveIndicator.vue'
import ApiErrorState from '../components/ApiErrorState.vue'
import EmptyState from '../components/ui/EmptyState.vue'
import Button from '../components/ui/Button.vue'
import Input from '../components/ui/Input.vue'
import Skeleton from '../components/ui/Skeleton.vue'

const props = defineProps<{ slug: string }>()
const route = useRoute()
const router = useRouter()
const session = useSession()
const alerts = useAlerts(props.slug)

const retrying = ref(false)
async function load() {
  retrying.value = true
  const qk = typeof route.query.key === 'string' ? route.query.key : null
  await session.load(props.slug, qk)
  if (qk) {
    const q = { ...route.query }
    delete q.key
    void router.replace({ query: q })
  }
  retrying.value = false
}
watch(() => props.slug, load, { immediate: true })

watch(
  () => [session.status.value, session.key.value] as const,
  ([status, key]) => {
    if (status === 'ready' && key) connectLive(props.slug, key)
    else disconnectLive()
  },
  { immediate: true },
)
onBeforeUnmount(() => disconnectLive())

// Manual key entry (no-key / invalid-key states)
const keyDraft = ref('')
async function useKey() {
  const k = keyDraft.value.trim()
  if (!k) return
  rememberKey(props.slug, k)
  keyDraft.value = ''
  await load()
}

const nav = computed(() => [
  { to: `/t/${props.slug}`, label: 'Dashboard', exact: true },
  { to: `/t/${props.slug}/import`, label: 'Import' },
  { to: `/t/${props.slug}/alerts`, label: 'Alerts', badge: unread.value },
  { to: `/t/${props.slug}/settings`, label: 'Settings' },
])
const unread = computed(() => {
  void alerts.items.length
  void alerts.seenAt
  return unreadCount()
})
function isActive(item: { to: string; exact?: boolean }) {
  return item.exact ? route.path === item.to : route.path.startsWith(item.to)
}
const tenantName = computed(() => session.tenant.value?.name ?? props.slug)
</script>

<template>
  <div class="shell">
    <aside class="side" aria-label="Tenant navigation">
      <router-link to="/" class="brand" aria-label="AdPulse home">
        <span class="logo" aria-hidden="true"></span><span>AdPulse</span>
      </router-link>
      <div class="tenant">
        <p class="tname">{{ tenantName }}</p>
        <p class="tslug mono">/t/{{ slug }}</p>
      </div>
      <nav class="nav">
        <router-link v-for="n in nav" :key="n.to" :to="n.to" class="nl" :class="{ active: isActive(n) }" :aria-current="isActive(n) ? 'page' : undefined">
          <span>{{ n.label }}</span>
          <span v-if="n.badge" class="nbadge num" :aria-label="`${n.badge} new alerts`">{{ n.badge }}</span>
        </router-link>
      </nav>
      <div class="side-foot">
        <LiveIndicator v-if="session.isReady.value" />
      </div>
    </aside>

    <div class="main-col">
      <header class="topbar">
        <router-link to="/" class="brand small" aria-label="AdPulse home"><span class="logo" aria-hidden="true"></span><span>AdPulse</span></router-link>
        <span class="tb-tenant"><span class="tname">{{ tenantName }}</span></span>
        <LiveIndicator v-if="session.isReady.value" class="tb-live" />
      </header>

      <main id="main" class="content" tabindex="-1">
        <!-- loading -->
        <div v-if="session.status.value === 'loading' || session.status.value === 'idle'" class="state" aria-busy="true">
          <div class="grid-kpi"><Skeleton v-for="i in 4" :key="i" height="104px" radius="12px" /></div>
          <Skeleton height="320px" radius="12px" />
        </div>

        <!-- no key -->
        <div v-else-if="session.status.value === 'no-key'" class="state narrow">
          <EmptyState title="This browser has no key for this tenant" :description="`Paste the API key you received when the tenant ‘${slug}’ was created, or open the magic link /t/${slug}?key=… from that moment.`">
            <form class="keyform" @submit.prevent="useKey">
              <Input v-model="keyDraft" label="API key" placeholder="adp_…" mono autocomplete="off" />
              <Button type="submit" variant="primary" :disabled="!keyDraft.trim()">Open tenant</Button>
            </form>
            <template #footer><router-link to="/">Create a new tenant instead</router-link></template>
          </EmptyState>
        </div>

        <!-- invalid key -->
        <div v-else-if="session.status.value === 'invalid-key'" class="state narrow">
          <EmptyState title="This key is not valid" tone="error" :description="`The API rejected the stored key ${maskKey(session.key.value)} (${session.state.error?.status ?? '401'}). Keys are shown once at creation and cannot be recovered — paste the right one or create a new tenant.`">
            <form class="keyform" @submit.prevent="useKey">
              <Input v-model="keyDraft" label="Different API key" placeholder="adp_…" mono autocomplete="off" />
              <Button type="submit" variant="primary" :disabled="!keyDraft.trim()">Try this key</Button>
            </form>
            <template #footer><router-link to="/">Create a new tenant</router-link> · <button type="button" class="linkbtn" @click="session.signOut(slug)">Forget stored key</button></template>
          </EmptyState>
        </div>

        <!-- key belongs to another tenant -->
        <div v-else-if="session.status.value === 'wrong-tenant'" class="state narrow">
          <EmptyState title="This key belongs to a different tenant" tone="warning" :description="`The stored key authenticates as ‘${session.tenant.value?.name}’ (/t/${session.tenant.value?.slug}), not ‘${slug}’.`">
            <Button variant="primary" :to="`/t/${session.tenant.value?.slug}`">Open {{ session.tenant.value?.slug }}</Button>
            <Button variant="ghost" @click="session.signOut(slug)">Use another key here</Button>
          </EmptyState>
        </div>

        <!-- unreachable / server error -->
        <div v-else-if="session.status.value === 'unreachable' || session.status.value === 'error'" class="state narrow">
          <ApiErrorState :error="session.state.error" :retrying="retrying" @retry="load" />
        </div>

        <router-view v-else />
      </main>

      <nav class="bottom-nav" aria-label="Tenant navigation (mobile)">
        <router-link v-for="n in nav" :key="n.to" :to="n.to" class="bl" :class="{ active: isActive(n) }" :aria-current="isActive(n) ? 'page' : undefined">
          {{ n.label }}<span v-if="n.badge" class="nbadge num">{{ n.badge }}</span>
        </router-link>
      </nav>
    </div>
  </div>
</template>

<style scoped>
.shell {
  display: grid;
  grid-template-columns: var(--sidebar-w) minmax(0, 1fr);
  min-height: 100dvh;
}
.side {
  position: sticky;
  top: 0;
  height: 100dvh;
  display: flex;
  flex-direction: column;
  gap: var(--s-6);
  padding: var(--s-5) var(--s-4);
  border-right: 1px solid var(--border);
  background: var(--surface);
}
.brand {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-weight: 700;
  color: var(--text);
  text-decoration: none;
  font-size: var(--text-md);
  letter-spacing: -0.01em;
}
.logo {
  width: 20px;
  height: 20px;
  border-radius: 5px;
  background: var(--accent);
  position: relative;
}
.logo::after {
  content: '';
  position: absolute;
  left: 4px;
  right: 4px;
  bottom: 5px;
  height: 7px;
  border-left: 2px solid #fff;
  border-bottom: 2px solid #fff;
  transform: skewX(-20deg);
  opacity: 0.95;
}
.tenant {
  padding: var(--s-3);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  background: var(--surface-alt);
}
.tname {
  font-weight: 700;
  font-size: var(--text-sm);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.tslug {
  font-size: var(--text-xs);
  color: var(--text-muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.nav {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.nl {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 10px;
  border-radius: var(--r-md);
  color: var(--text-muted);
  text-decoration: none;
  font-weight: 500;
  font-size: var(--text-sm);
}
.nl:hover {
  background: var(--surface-sunken);
  color: var(--text);
}
.nl.active {
  background: var(--accent-soft);
  color: var(--accent);
}
.nbadge {
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  border-radius: 9px;
  background: var(--error);
  color: #fff;
  font-size: 11px;
  font-weight: 700;
  display: inline-grid;
  place-items: center;
}
.side-foot {
  margin-top: auto;
}
.main-col {
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.topbar {
  display: none;
}
.content {
  padding: var(--s-6) var(--s-8) var(--s-12);
  max-width: var(--content-max);
  width: 100%;
  margin: 0 auto;
  outline: none;
}
.state {
  display: flex;
  flex-direction: column;
  gap: var(--s-4);
}
.state.narrow {
  max-width: 640px;
  margin: var(--s-10) auto 0;
}
.keyform {
  display: flex;
  gap: var(--s-2);
  align-items: flex-end;
  width: min(460px, 100%);
  text-align: left;
}
.keyform :deep(.field) {
  flex: 1;
}
.linkbtn {
  background: none;
  border: 0;
  padding: 0;
  color: var(--accent);
  text-decoration: underline;
  text-underline-offset: 2px;
  cursor: pointer;
  font-size: inherit;
}
.bottom-nav {
  display: none;
}

@media (max-width: 900px) {
  .shell {
    grid-template-columns: minmax(0, 1fr);
  }
  .side {
    display: none;
  }
  .topbar {
    position: sticky;
    top: 0;
    z-index: 20;
    display: flex;
    align-items: center;
    gap: var(--s-3);
    height: 52px;
    padding: 0 var(--s-4);
    background: var(--surface);
    border-bottom: 1px solid var(--border);
  }
  .tb-tenant {
    flex: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: var(--text-sm);
  }
  .tb-tenant::before {
    content: '/';
    color: var(--text-faint);
  }
  .content {
    padding: var(--s-4) var(--s-4) calc(var(--s-16) + env(safe-area-inset-bottom));
  }
  .bottom-nav {
    position: fixed;
    left: 0;
    right: 0;
    bottom: 0;
    z-index: 20;
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    background: var(--surface);
    border-top: 1px solid var(--border);
    padding-bottom: env(safe-area-inset-bottom);
  }
  .bl {
    display: inline-flex;
    justify-content: center;
    align-items: center;
    gap: 6px;
    height: 50px;
    font-size: var(--text-xs);
    font-weight: 500;
    color: var(--text-muted);
    text-decoration: none;
    border-top: 2px solid transparent;
  }
  .bl.active {
    color: var(--accent);
    border-top-color: var(--accent);
  }
  .keyform {
    flex-direction: column;
    align-items: stretch;
  }
  .state.narrow {
    margin-top: var(--s-4);
  }
}
</style>
