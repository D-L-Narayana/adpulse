<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError, describeError } from '../lib/api'
import { apiBase } from '../lib/config'
import { knownTenants, lastSlug, rememberKey } from '../stores/session'
import type { TenantCreated } from '../lib/types'
import Button from '../components/ui/Button.vue'
import Input from '../components/ui/Input.vue'
import Card from '../components/ui/Card.vue'
import Tabs from '../components/ui/Tabs.vue'
import CopyField from '../components/CopyField.vue'
import AppShellPreview from '../components/AppShellPreview.vue'

const router = useRouter()
const tabs = [
  { id: 'create', label: 'Create tenant' },
  { id: 'open', label: 'Open existing tenant' },
]
const tab = ref<'create' | 'open'>(knownTenants().length ? 'open' : 'create')

/* create */
const name = ref('')
const email = ref('')
const creating = ref(false)
const createError = ref<string | null>(null)
const created = ref<TenantCreated | null>(null)
const touched = ref(false)
const nameError = computed(() => ((touched.value || name.value) && name.value.trim().length < 2 ? 'At least 2 characters' : undefined))
const emailError = computed(() => ((touched.value || email.value) && !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email.value) ? 'Enter a valid email' : undefined))
const canCreate = computed(() => name.value.trim().length >= 2 && /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email.value))

async function create() {
  touched.value = true
  if (!canCreate.value) return
  creating.value = true
  createError.value = null
  try {
    const t = await api.createTenant(name.value.trim(), email.value.trim())
    if (!t.apiKey || !t.slug) throw new Error('The API did not return a slug and API key.')
    rememberKey(t.slug, t.apiKey)
    created.value = t
  } catch (e) {
    createError.value = e instanceof ApiError && e.isUnreachable ? `${e.message} (${apiBase()})` : describeError(e)
  } finally {
    creating.value = false
  }
}
const magic = computed(() => (created.value ? `${location.origin}/t/${created.value.slug}?key=${created.value.apiKey}` : ''))

/* open */
const key = ref('')
const opening = ref(false)
const openError = ref<string | null>(null)
const recent = knownTenants()
const last = lastSlug()
async function open() {
  const k = key.value.trim()
  if (!k) return
  opening.value = true
  openError.value = null
  try {
    const me = await api.me(k)
    if (!me.slug) throw new Error('The API did not return a tenant slug for this key.')
    rememberKey(me.slug, k)
    await router.push(`/t/${me.slug}`)
  } catch (e) {
    openError.value = e instanceof ApiError && e.isAuthProblem ? 'That key was not recognised by the API.' : e instanceof ApiError && e.isUnreachable ? `${e.message} (${apiBase()})` : describeError(e)
  } finally {
    opening.value = false
  }
}
</script>

<template>
  <div class="landing">
    <header class="top">
      <router-link to="/" class="brand" aria-label="AdPulse"><span class="logo" aria-hidden="true"></span>AdPulse</router-link>
      <nav class="links" aria-label="Project links">
        <a :href="apiBase() + '/swagger'" target="_blank" rel="noopener">API docs</a>
        <a href="https://github.com/D-L-Narayana/adpulse" target="_blank" rel="noopener">Source</a>
      </nav>
    </header>

    <main id="main" class="hero" tabindex="-1">
      <div class="copy">
        <h1>Know today whether every campaign will land on budget.</h1>
        <p class="sub">AdPulse ingests Google Ads and Meta exports or REST pushes into your own tenant, computes CTR, CPC, CPA and budget pacing server-side, and streams changes to the dashboard as they happen.</p>
        <ul class="facts">
          <li><b>Multi-tenant</b> — one API key per tenant, cross-tenant access is a 404.</li>
          <li><b>Pacing engine</b> — linear expectation, ±10% band, corrected daily budget.</li>
          <li><b>Live</b> — server-sent events for imports, metrics and pacing alerts.</li>
        </ul>
      </div>

      <Card class="entry" :padded="true">
        <template v-if="created">
          <h2 class="et">Tenant created</h2>
          <p class="es">Copy the key now — <strong>it is shown only once</strong>. It is also saved in this browser.</p>
          <div class="stack" style="margin-top: 16px">
            <CopyField label="API key" :value="created.apiKey" aria-label="API key" />
            <CopyField label="Magic link" :value="magic" aria-label="Magic link" />
          </div>
          <div class="row" style="margin-top: 20px">
            <Button variant="primary" size="lg" :to="`/t/${created.slug}`">Open {{ created.name }}</Button>
            <Button variant="ghost" :to="`/t/${created.slug}/import`">Import data first</Button>
          </div>
        </template>
        <template v-else>
          <Tabs v-model="tab" :tabs="tabs" label="Get started" />
          <form v-if="tab === 'create'" class="form" @submit.prevent="create">
            <Input v-model="name" label="Tenant name" placeholder="Acme Retail" required autocomplete="organization" :error="nameError" />
            <Input v-model="email" label="Email" type="email" placeholder="you@company.com" required autocomplete="email" inputmode="email" :error="emailError" hint="No password. You get an API key once; keep it." />
            <p v-if="createError" class="ferr" role="alert">{{ createError }}</p>
            <Button type="submit" variant="primary" size="lg" block :loading="creating">Create tenant</Button>
          </form>
          <form v-else class="form" @submit.prevent="open">
            <Input v-model="key" label="API key" placeholder="Paste the key you were given" mono required autocomplete="off" hint="The key is only stored in this browser." />
            <p v-if="openError" class="ferr" role="alert">{{ openError }}</p>
            <Button type="submit" variant="primary" size="lg" block :loading="opening">Open tenant</Button>
            <div v-if="recent.length" class="recent">
              <p class="rl">Tenants on this device</p>
              <div class="chips">
                <router-link v-for="s in recent" :key="s" :to="`/t/${s}`" class="chip mono">{{ s }}<span v-if="s === last" class="lastdot" aria-label="most recent"></span></router-link>
              </div>
            </div>
          </form>
        </template>
      </Card>
    </main>

    <section class="preview-wrap" aria-labelledby="pv">
      <p id="pv" class="pv-cap">The dashboard shell you get — it fills with your own data, nothing is pre-loaded.</p>
      <AppShellPreview />
    </section>

    <section class="how" aria-labelledby="how-h">
      <h2 id="how-h">How data gets in</h2>
      <ol class="steps">
        <li><span class="n">1</span><div><b>CSV import</b><p>Drop a Google Ads or Meta Ads Manager export. The API detects the format, shows the column mapping and first rows, and only writes after you confirm.</p></div></li>
        <li><span class="n">2</span><div><b>REST ingest</b><p><code>POST /api/v1/metrics/bulk</code> with your key and an <code>Idempotency-Key</code>; the in-app snippet is pre-filled with your real key.</p></div></li>
        <li><span class="n">3</span><div><b>Sample dataset</b><p>Optionally load a real, attributed public marketing dataset through the same preview flow — never automatically.</p></div></li>
      </ol>
    </section>

    <footer class="foot">
      <span>ASP.NET Core 8 API · Vue 3 dashboard · API at <code class="mono">{{ apiBase() }}</code></span>
    </footer>
  </div>
</template>

<style scoped>
.landing {
  max-width: var(--content-max);
  margin: 0 auto;
  padding: 0 var(--s-6) var(--s-12);
}
.top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  height: 64px;
}
.brand {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-weight: 700;
  color: var(--text);
  text-decoration: none;
  font-size: var(--text-md);
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
}
.links {
  display: flex;
  gap: var(--s-5);
  font-size: var(--text-sm);
}
.links a {
  color: var(--text-muted);
  text-decoration: none;
}
.links a:hover {
  color: var(--accent);
}
.hero {
  display: grid;
  grid-template-columns: minmax(0, 1.1fr) minmax(320px, 440px);
  gap: var(--s-12);
  align-items: start;
  padding: var(--s-10) 0 var(--s-12);
  outline: none;
}
h1 {
  font-size: clamp(32px, 4.2vw, 50px);
  letter-spacing: -0.025em;
  line-height: 1.08;
  max-width: 16ch;
}
.sub {
  margin-top: var(--s-5);
  font-size: var(--text-lg);
  color: var(--text-muted);
  max-width: 56ch;
  line-height: 1.5;
}
.facts {
  margin: var(--s-6) 0 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: var(--s-2);
  font-size: var(--text-sm);
  color: var(--text-muted);
}
.facts b {
  color: var(--text);
  font-weight: 700;
}
.entry {
  position: sticky;
  top: var(--s-4);
}
.et {
  font-size: var(--text-xl);
}
.es {
  color: var(--text-muted);
  font-size: var(--text-sm);
  margin-top: 6px;
}
.form {
  display: flex;
  flex-direction: column;
  gap: var(--s-4);
  padding-top: var(--s-5);
}
.ferr {
  color: var(--error);
  font-size: var(--text-sm);
  overflow-wrap: anywhere;
}
.recent {
  border-top: 1px solid var(--border-soft);
  padding-top: var(--s-4);
}
.rl {
  font-size: var(--text-xs);
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: var(--s-2);
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  border: 1px solid var(--border);
  border-radius: 999px;
  font-size: var(--text-xs);
  color: var(--text);
  text-decoration: none;
  background: var(--surface-alt);
}
.chip:hover {
  border-color: var(--accent);
  color: var(--accent);
}
.lastdot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--accent);
}
.preview-wrap {
  display: flex;
  flex-direction: column;
  gap: var(--s-3);
}
.pv-cap {
  font-size: var(--text-sm);
  color: var(--text-muted);
}
.how {
  margin-top: var(--s-16);
}
.how h2 {
  font-size: var(--text-xl);
  margin-bottom: var(--s-6);
}
.steps {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--s-6);
}
.steps li {
  display: flex;
  gap: var(--s-3);
  align-items: flex-start;
}
.n {
  flex-shrink: 0;
  width: 26px;
  height: 26px;
  border-radius: 50%;
  display: grid;
  place-items: center;
  background: var(--accent-soft);
  color: var(--accent);
  font-weight: 700;
  font-size: var(--text-sm);
  font-variant-numeric: tabular-nums;
}
.steps b {
  display: block;
  margin-bottom: 4px;
}
.steps p {
  font-size: var(--text-sm);
  color: var(--text-muted);
  line-height: 1.5;
}
.steps code {
  font-size: 0.85em;
  background: var(--surface-sunken);
  padding: 1px 5px;
  border-radius: 4px;
  color: var(--text);
}
.foot {
  margin-top: var(--s-16);
  padding-top: var(--s-6);
  border-top: 1px solid var(--border);
  font-size: var(--text-xs);
  color: var(--text-muted);
  overflow-wrap: anywhere;
}
@media (max-width: 960px) {
  .hero {
    grid-template-columns: minmax(0, 1fr);
    gap: var(--s-8);
    padding-top: var(--s-6);
  }
  .entry {
    position: static;
  }
  .steps {
    grid-template-columns: minmax(0, 1fr);
  }
}
@media (max-width: 640px) {
  .landing {
    padding: 0 var(--s-4) var(--s-10);
  }
  .top {
    height: 56px;
  }
  h1 {
    font-size: 30px;
  }
  .sub {
    font-size: var(--text-md);
  }
  .how {
    margin-top: var(--s-12);
  }
}
</style>
