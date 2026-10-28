<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useSession } from '../stores/session'
import { apiBase, apiBaseSource, setApiBase } from '../lib/config'
import { fmtDateTime, maskKey } from '../lib/format'
import { clearAlerts } from '../stores/alerts'
import { notify } from '../stores/toast'
import Card from '../components/ui/Card.vue'
import Button from '../components/ui/Button.vue'
import Input from '../components/ui/Input.vue'
import Dialog from '../components/ui/Dialog.vue'
import CopyField from '../components/CopyField.vue'

const session = useSession()
const router = useRouter()
const slug = computed(() => session.slug.value ?? '')
const t = computed(() => session.tenant.value)
const key = computed(() => session.key.value ?? '')
const magic = computed(() => `${location.origin}/t/${slug.value}?key=${key.value}`)

const base = ref(apiBase())
const source = apiBaseSource()
function saveBase() {
  setApiBase(base.value)
  notify.success('API URL updated', 'Reloading to reconnect…')
  setTimeout(() => location.reload(), 600)
}
function resetBase() {
  setApiBase('')
  notify.info('API URL reset to the build default', 'Reloading…')
  setTimeout(() => location.reload(), 600)
}

const confirmForget = ref(false)
const confirmText = ref('')
function forget() {
  session.signOut(slug.value)
  clearAlerts(slug.value)
  confirmForget.value = false
  notify.info('Key removed from this browser')
  void router.push('/')
}
</script>

<template>
  <div class="page">
    <div class="page-head">
      <div>
        <h1>Settings</h1>
        <p class="lede">Tenant identity, access key and where this dashboard talks to.</p>
      </div>
    </div>

    <Card title="Tenant">
      <dl class="facts">
        <div><dt>Name</dt><dd>{{ t?.name }}</dd></div>
        <div><dt>Slug</dt><dd class="mono">{{ slug }}</dd></div>
        <div v-if="t?.email"><dt>Email</dt><dd>{{ t.email }}</dd></div>
        <div v-if="t?.id"><dt>Tenant id</dt><dd class="mono">{{ t.id }}</dd></div>
        <div v-if="t?.createdUtc"><dt>Created</dt><dd>{{ fmtDateTime(t.createdUtc) }}</dd></div>
        <div v-if="t?.currency"><dt>Currency</dt><dd class="mono">{{ t.currency }}</dd></div>
      </dl>
    </Card>

    <Card title="API key" subtitle="Stored only in this browser's localStorage. The API never returns it again — if you lose it, create a new tenant.">
      <div class="stack">
        <CopyField label="Key" :value="key" :masked="maskKey(key)" aria-label="API key" />
        <CopyField label="Magic link (opens this tenant on another device)" :value="magic" :masked="`${magic.split('?')[0]}?key=${maskKey(key)}`" aria-label="Magic link" />
      </div>
    </Card>

    <Card title="API endpoint" :subtitle="`Currently ${source === 'storage' || source === 'query' ? 'overridden in this browser' : source === 'env' ? 'from the build environment' : 'the build default'}. Change it if the ngrok domain moves.`">
      <form class="baseform" @submit.prevent="saveBase">
        <Input v-model="base" label="Base URL" mono inputmode="url" placeholder="https://adpulse-api.ngrok-free.app" hint="You can also append ?api=https://host to any AdPulse URL once." />
        <div class="row">
          <Button type="submit" variant="primary" :disabled="!base.trim() || base.trim() === apiBase()">Save and reconnect</Button>
          <Button variant="ghost" :disabled="source === 'env' || source === 'default'" @click="resetBase">Reset to default</Button>
        </div>
      </form>
    </Card>

    <Card title="Danger zone" class="danger">
      <div class="dz">
        <div>
          <p class="dz-t">Forget this tenant on this device</p>
          <p class="dz-s">Removes the stored key and alert history from this browser. Data on the server is untouched; you can come back with the key or the magic link.</p>
        </div>
        <Button variant="danger" @click="confirmForget = true">Forget key</Button>
      </div>
    </Card>

    <Dialog :open="confirmForget" title="Forget this tenant's key?" :description="`Type the slug (${slug}) to confirm. Without the key you will not be able to open this tenant again unless you saved the magic link.`" danger @close="confirmForget = false">
      <Input v-model="confirmText" label="Slug" mono autocomplete="off" />
      <template #footer>
        <Button variant="ghost" @click="confirmForget = false">Cancel</Button>
        <Button variant="danger" :disabled="confirmText.trim() !== slug" @click="forget">Forget key</Button>
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.facts {
  margin: 0;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: var(--s-4);
}
.facts dt {
  font-size: var(--text-xs);
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  font-weight: 500;
}
.facts dd {
  margin: 2px 0 0;
  font-weight: 500;
  overflow-wrap: anywhere;
}
.baseform {
  display: flex;
  flex-direction: column;
  gap: var(--s-4);
  max-width: 560px;
}
.danger {
  border-color: color-mix(in srgb, var(--error) 40%, var(--border));
}
.dz {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--s-4);
  flex-wrap: wrap;
}
.dz-t {
  font-weight: 700;
  font-size: var(--text-sm);
}
.dz-s {
  font-size: var(--text-sm);
  color: var(--text-muted);
  max-width: 60ch;
  margin-top: 2px;
}
</style>
