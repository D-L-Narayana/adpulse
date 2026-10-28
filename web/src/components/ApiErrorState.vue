<script setup lang="ts">
import { computed, ref } from 'vue'
import { ApiError } from '../lib/api'
import { apiBase, setApiBase } from '../lib/config'
import EmptyState from './ui/EmptyState.vue'
import Button from './ui/Button.vue'
import Input from './ui/Input.vue'

const props = withDefaults(defineProps<{ error: unknown; retrying?: boolean; compact?: boolean }>(), { retrying: false, compact: false })
const emit = defineEmits<{ retry: [] }>()

const err = computed(() => (props.error instanceof ApiError ? props.error : null))
const unreachable = computed(() => !err.value || err.value.isUnreachable || err.value.kind === 'server')
const title = computed(() => {
  if (!err.value) return 'Something went wrong'
  if (err.value.kind === 'timeout') return 'The API is not responding'
  if (err.value.kind === 'network') return 'API unreachable'
  if (err.value.kind === 'server') return 'The API returned an error'
  return 'Request failed'
})
const message = computed(() => (err.value ? err.value.message : props.error instanceof Error ? props.error.message : String(props.error)))

const editing = ref(false)
const draft = ref(apiBase())
function saveBase() {
  setApiBase(draft.value)
  editing.value = false
  emit('retry')
}
</script>

<template>
  <EmptyState :title="title" tone="error" :compact="compact">
    <template #description>
      {{ message }}
      <template v-if="unreachable"> If the API was just started or the tunnel address changed, retry or point the dashboard at a different base URL.</template>
    </template>
    <Button variant="primary" :loading="retrying" @click="emit('retry')">Retry</Button>
    <Button v-if="unreachable" variant="ghost" @click="editing = !editing">{{ editing ? 'Cancel' : 'Change API URL' }}</Button>
    <template #footer>
      <form v-if="editing" class="edit" @submit.prevent="saveBase">
        <Input v-model="draft" label="API base URL" placeholder="https://adpulse-api.ngrok-free.app" mono inputmode="url" hint="Stored in this browser only. You can also open any page with ?api=https://host once." />
        <Button type="submit" variant="primary" size="sm">Save and retry</Button>
      </form>
      <span v-else>Tried <code class="url">{{ err?.url || apiBase() }}</code></span>
    </template>
  </EmptyState>
</template>

<style scoped>
.url {
  font-size: var(--text-xs);
  color: var(--text);
  background: var(--surface-sunken);
  padding: 2px 6px;
  border-radius: var(--r-sm);
}
.edit {
  display: flex;
  flex-direction: column;
  gap: var(--s-3);
  text-align: left;
  width: min(420px, 100%);
  margin: 0 auto;
  word-break: normal;
}
</style>
