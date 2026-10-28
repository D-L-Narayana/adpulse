<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, buildUrl, describeError } from '../lib/api'
import { useSession } from '../stores/session'
import { notify } from '../stores/toast'
import { markUpdated } from '../stores/live'
import { maskKey } from '../lib/format'
import type { ImportPreview, ImportSummary } from '../lib/types'
import Card from '../components/ui/Card.vue'
import Button from '../components/ui/Button.vue'
import Tabs from '../components/ui/Tabs.vue'
import Badge from '../components/ui/Badge.vue'
import CsvPreview from '../components/CsvPreview.vue'
import CopyField from '../components/CopyField.vue'

const session = useSession()
const route = useRoute()
const router = useRouter()
const slug = computed(() => session.slug.value ?? '')
const key = computed(() => session.key.value ?? '')

const tabs = [
  { id: 'csv', label: 'CSV file' },
  { id: 'api', label: 'API ingest' },
]
const tab = ref(route.hash === '#api' ? 'api' : 'csv')
watch(tab, (t) => router.replace({ hash: t === 'api' ? '#api' : '' }))

/* ---------------- CSV flow ---------------- */
type Stage = 'idle' | 'previewing' | 'preview' | 'committing' | 'done'
const stage = ref<Stage>('idle')
const file = ref<File | null>(null)
const preview = ref<ImportPreview | null>(null)
const result = ref<ImportSummary | null>(null)
const error = ref<string | null>(null)
const dragging = ref(false)
const inputEl = ref<HTMLInputElement | null>(null)
const sampleMeta = ref<{ source?: string; license?: string } | null>(null)
const loadingSample = ref(false)

const MAX_MB = 20

async function startPreview(f: File) {
  error.value = null
  result.value = null
  preview.value = null
  if (!/\.(csv|txt|tsv)$/i.test(f.name) && !/csv|text/.test(f.type)) {
    error.value = `“${f.name}” does not look like a CSV file.`
    return
  }
  if (f.size > MAX_MB * 1024 * 1024) {
    error.value = `File is ${(f.size / 1024 / 1024).toFixed(1)} MB; the limit is ${MAX_MB} MB.`
    return
  }
  file.value = f
  stage.value = 'previewing'
  try {
    preview.value = await api.previewCsv(key.value, f)
    stage.value = 'preview'
  } catch (e) {
    error.value = describeError(e)
    stage.value = 'idle'
  }
}

function onPick(e: Event) {
  const f = (e.target as HTMLInputElement).files?.[0]
  if (f) void startPreview(f)
}
function onDrop(e: DragEvent) {
  dragging.value = false
  const f = e.dataTransfer?.files?.[0]
  if (f) void startPreview(f)
}

async function commit() {
  if (!file.value) return
  stage.value = 'committing'
  try {
    const r = await api.commitCsv(key.value, file.value)
    result.value = r
    stage.value = 'done'
    markUpdated()
    const parts = [r.inserted !== undefined ? `${r.inserted} inserted` : null, r.updated !== undefined ? `${r.updated} updated` : null, r.campaignsCreated ? `${r.campaignsCreated} new campaign${r.campaignsCreated === 1 ? '' : 's'}` : null].filter(Boolean)
    notify.success('Import complete', parts.length ? parts.join(' · ') : `${file.value.name} imported`)
  } catch (e) {
    stage.value = 'preview'
    notify.error('Import failed', describeError(e))
  }
}

function reset() {
  stage.value = 'idle'
  file.value = null
  preview.value = null
  result.value = null
  error.value = null
  sampleMeta.value = null
  if (inputEl.value) inputEl.value.value = ''
}

async function loadSample() {
  loadingSample.value = true
  error.value = null
  try {
    const s = await api.sampleDataset(key.value)
    sampleMeta.value = { source: s.source, license: s.license }
    const f = new File([s.csv], s.fileName, { type: 'text/csv' })
    await startPreview(f)
  } catch (e) {
    error.value = describeError(e)
  } finally {
    loadingSample.value = false
  }
}

const resultRows = computed(() => {
  const r = result.value
  if (!r) return []
  const rows: { label: string; value: string }[] = []
  const add = (label: string, v: unknown) => v !== undefined && v !== null && rows.push({ label, value: String(v) })
  add('Format', r.format)
  add('Rows read', r.rowsRead)
  add('Inserted', r.inserted)
  add('Updated', r.updated)
  add('Skipped', r.skipped)
  add('Campaigns created', r.campaignsCreated)
  add('Import id', r.importId)
  return rows
})

/* ---------------- API ingest snippets ---------------- */
const endpointBulk = computed(() => buildUrl('/metrics/bulk'))
const endpointCsv = computed(() => buildUrl('/imports/csv'))
const showKey = ref(false)
const k = computed(() => (showKey.value ? key.value : maskKey(key.value)))
const today = new Date().toISOString().slice(0, 10)
const curlBulk = computed(
  () => `curl -X POST '${endpointBulk.value}' \\
  -H 'X-Api-Key: ${k.value}' \\
  -H 'Idempotency-Key: $(uuidgen)' \\
  -H 'Content-Type: application/json' \\
  -d '{
    "rows": [
      { "campaign": "Brand search", "channel": "GoogleAds", "date": "${today}",
        "impressions": 12840, "clicks": 412, "conversions": 19, "spend": 236.40 }
    ]
  }'`,
)
const curlCsv = computed(
  () => `curl -X POST '${endpointCsv.value}?dryRun=true' \\
  -H 'X-Api-Key: ${k.value}' \\
  -F 'file=@google-ads-export.csv'`,
)
const curlStream = computed(() => `curl -N '${buildUrl('/stream')}' -H 'X-Api-Key: ${k.value}'`)

onMounted(() => {
  if (route.hash === '#api') tab.value = 'api'
})
</script>

<template>
  <div class="page">
    <div class="page-head">
      <div>
        <h1>Import data</h1>
        <p class="lede">Bring real spend into <strong>{{ session.tenant.value?.name ?? slug }}</strong> — the dashboard, pacing and alerts update as rows land.</p>
      </div>
    </div>

    <Tabs v-model="tab" :tabs="tabs" label="Import method" />

    <!-- ---------------- CSV ---------------- -->
    <div v-if="tab === 'csv'" class="stack" role="tabpanel">
      <Card v-if="stage === 'idle' || stage === 'previewing'" title="Upload an export" subtitle="Google Ads (Campaign, Cost, Impressions, Clicks, Conversions, Day), Meta Ads Manager (Campaign name, Amount spent, Impressions, Link clicks, Results, Day) or the generic template. Nothing is written until you confirm the preview.">
        <label
          class="drop"
          :class="{ dragging, busy: stage === 'previewing' }"
          @dragover.prevent="dragging = true"
          @dragleave="dragging = false"
          @drop.prevent="onDrop"
        >
          <input ref="inputEl" type="file" accept=".csv,text/csv,.tsv,.txt" class="sr-only" :disabled="stage === 'previewing'" @change="onPick" />
          <span v-if="stage === 'previewing'" class="drop-title">Analysing {{ file?.name }}…</span>
          <template v-else>
            <span class="drop-title">Drop a CSV here or <span class="link">browse</span></span>
            <span class="drop-sub">Up to {{ MAX_MB }} MB · format is detected automatically</span>
          </template>
        </label>
        <p v-if="error" class="err" role="alert">{{ error }}</p>

        <div class="or"><span>or</span></div>

        <div class="sample">
          <div>
            <p class="sample-t">Load sample dataset <Badge tone="accent" :dot="false">real public data, attributed</Badge></p>
            <p class="sample-s">Fetches a real, publicly licensed marketing dataset from the API (attribution shown in the preview and in the README) and runs it through the same preview → confirm flow. It is never loaded automatically.</p>
          </div>
          <Button :loading="loadingSample" @click="loadSample">Load sample dataset</Button>
        </div>

        <details class="tpl">
          <summary>Generic template columns</summary>
          <code class="mono">campaign,channel,date,impressions,clicks,conversions,spend,revenue</code>
          <p class="muted small">Channel is one of GoogleAds, Meta, Amazon, LinkedIn, DV360. Dates are ISO (YYYY-MM-DD). Rows are upserted per campaign + date, so re-importing a file is safe.</p>
        </details>
      </Card>

      <Card v-else-if="(stage === 'preview' || stage === 'committing') && preview" title="Preview before import" subtitle="This is a dry run — confirm to write these rows to your tenant.">
        <template #actions>
          <Button variant="ghost" :disabled="stage === 'committing'" @click="reset">Choose another file</Button>
        </template>
        <p v-if="sampleMeta" class="attrib">
          <strong>Sample dataset.</strong>
          <template v-if="sampleMeta.source"> Source: <a :href="sampleMeta.source" target="_blank" rel="noopener">{{ sampleMeta.source }}</a>.</template>
          <template v-else> Served by the API from a real public dataset; see the README for the source and licence.</template>
          <template v-if="sampleMeta.license"> Licence: {{ sampleMeta.license }}.</template>
        </p>
        <CsvPreview :preview="preview" :file-name="file?.name" />
        <div class="commit">
          <p class="muted small">{{ preview.rowCount !== undefined ? `${preview.rowCount.toLocaleString('en-US')} rows` : 'All rows' }} will be upserted into <strong>{{ session.tenant.value?.name ?? slug }}</strong>. Existing campaign + date pairs are updated, new campaigns are created as Draft.</p>
          <div class="row">
            <Button variant="ghost" :disabled="stage === 'committing'" @click="reset">Cancel</Button>
            <Button variant="primary" :loading="stage === 'committing'" :disabled="/unknown/i.test(preview.format)" @click="commit">Import {{ preview.rowCount !== undefined ? preview.rowCount.toLocaleString('en-US') + ' rows' : 'rows' }}</Button>
          </div>
        </div>
      </Card>

      <Card v-else-if="stage === 'done' && result" title="Import complete" subtitle="The dashboard has already been refreshed over the live stream.">
        <dl class="result">
          <div v-for="r in resultRows" :key="r.label"><dt>{{ r.label }}</dt><dd class="num">{{ r.value }}</dd></div>
        </dl>
        <div v-if="result.warnings?.length" class="warn-list">
          <p class="small"><strong>{{ result.warnings.length }} warning{{ result.warnings.length === 1 ? '' : 's' }}</strong></p>
          <ul class="small"><li v-for="(w, i) in result.warnings" :key="i">{{ w }}</li></ul>
        </div>
        <div class="row" style="margin-top: 16px">
          <Button variant="primary" :to="`/t/${slug}`">Open dashboard</Button>
          <Button variant="ghost" @click="reset">Import another file</Button>
        </div>
      </Card>
    </div>

    <!-- ---------------- API ---------------- -->
    <div v-else class="stack" role="tabpanel" id="api">
      <Card title="Push metrics over REST" subtitle="One row per campaign per day. Campaigns that do not exist yet are created. Re-sending the same Idempotency-Key is a no-op.">
        <template #actions>
          <Button size="sm" variant="ghost" :aria-pressed="showKey" @click="showKey = !showKey">{{ showKey ? 'Mask key in snippets' : 'Use my real key in snippets' }}</Button>
        </template>
        <div class="stack">
          <CopyField label="Bulk upsert" :value="curlBulk" multiline aria-label="curl command for bulk metrics upsert" />
          <CopyField label="CSV via API (dry run)" :value="curlCsv" multiline aria-label="curl command for CSV import" />
          <CopyField label="Subscribe to the event stream" :value="curlStream" multiline aria-label="curl command for the SSE stream" />
        </div>
      </Card>
      <Card title="Reference">
        <table class="ref">
          <thead><tr><th scope="col">Header / field</th><th scope="col">Meaning</th></tr></thead>
          <tbody>
            <tr><td class="mono">X-Api-Key</td><td>Your tenant key — scopes every call; cross-tenant ids return 404.</td></tr>
            <tr><td class="mono">Idempotency-Key</td><td>Any unique string per logical batch; retries with the same key are safe.</td></tr>
            <tr><td class="mono">rows[].campaign</td><td>Campaign name; created as Draft on first sight.</td></tr>
            <tr><td class="mono">rows[].channel</td><td>GoogleAds · Meta · Amazon · LinkedIn · DV360</td></tr>
            <tr><td class="mono">rows[].date</td><td>ISO day (YYYY-MM-DD); one row per campaign per day.</td></tr>
            <tr><td class="mono">impressions, clicks, conversions, spend, revenue</td><td>Non-negative numbers; revenue optional.</td></tr>
          </tbody>
        </table>
        <p class="muted small" style="margin-top: 12px">Full OpenAPI at <a :href="buildUrl('/swagger').replace('/api/v1', '')" target="_blank" rel="noopener">/swagger</a>.</p>
      </Card>
    </div>
  </div>
</template>

<style scoped>
.drop {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-height: 168px;
  padding: var(--s-6);
  border: 2px dashed var(--border);
  border-radius: var(--r-lg);
  background: var(--surface-alt);
  cursor: pointer;
  text-align: center;
  transition: border-color var(--dur-fast) var(--ease), background var(--dur-fast) var(--ease);
}
.drop:hover,
.drop.dragging {
  border-color: var(--accent);
  background: var(--accent-soft);
}
.drop:focus-within {
  box-shadow: var(--focus-ring);
}
.drop.busy {
  cursor: progress;
  border-style: solid;
}
.drop-title {
  font-weight: 700;
  font-size: var(--text-md);
}
.link {
  color: var(--accent);
  text-decoration: underline;
  text-underline-offset: 2px;
}
.drop-sub {
  font-size: var(--text-sm);
  color: var(--text-muted);
}
.err {
  margin-top: var(--s-3);
  color: var(--error);
  font-size: var(--text-sm);
}
.or {
  display: flex;
  align-items: center;
  gap: var(--s-3);
  margin: var(--s-5) 0;
  color: var(--text-faint);
  font-size: var(--text-xs);
  text-transform: uppercase;
  letter-spacing: 0.06em;
}
.or::before,
.or::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--border-soft);
}
.sample {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-4);
  padding: var(--s-4);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  background: var(--surface-alt);
}
.sample-t {
  font-weight: 700;
  font-size: var(--text-sm);
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}
.sample-s {
  font-size: var(--text-sm);
  color: var(--text-muted);
  margin-top: 4px;
  max-width: 60ch;
}
.tpl {
  margin-top: var(--s-5);
  font-size: var(--text-sm);
}
.tpl summary {
  cursor: pointer;
  color: var(--accent);
  font-weight: 500;
}
.tpl code {
  display: block;
  margin: var(--s-2) 0;
  padding: 8px 10px;
  background: var(--surface-sunken);
  border-radius: var(--r-sm);
  font-size: var(--text-xs);
  overflow-x: auto;
}
.attrib {
  font-size: var(--text-sm);
  padding: var(--s-3) var(--s-4);
  background: var(--accent-soft);
  border-radius: var(--r-md);
  margin-bottom: var(--s-4);
  overflow-wrap: anywhere;
}
.commit {
  margin-top: var(--s-5);
  padding-top: var(--s-4);
  border-top: 1px solid var(--border-soft);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-4);
  flex-wrap: wrap;
}
.result {
  margin: 0;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: var(--s-3);
}
.result dt {
  font-size: var(--text-xs);
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}
.result dd {
  margin: 2px 0 0;
  font-size: var(--text-lg);
  font-weight: 700;
  overflow-wrap: anywhere;
}
.warn-list {
  margin-top: var(--s-4);
  padding: var(--s-3) var(--s-4);
  background: var(--warning-soft);
  border-radius: var(--r-md);
}
.warn-list ul {
  margin: 4px 0 0;
  padding-left: 18px;
}
.ref {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--text-sm);
}
.ref th,
.ref td {
  text-align: left;
  padding: 8px 10px;
  border-bottom: 1px solid var(--border-soft);
  vertical-align: top;
}
.ref th {
  font-size: var(--text-xs);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--text-muted);
  font-weight: 500;
}
.ref td.mono {
  font-size: var(--text-xs);
  white-space: nowrap;
}
@media (max-width: 640px) {
  .sample {
    flex-direction: column;
    align-items: stretch;
  }
  .ref td.mono {
    white-space: normal;
  }
}
</style>
