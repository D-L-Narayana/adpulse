<script setup lang="ts">
import { computed, useId } from 'vue'

const props = withDefaults(
  defineProps<{
    modelValue: string
    label?: string
    type?: string
    placeholder?: string
    hint?: string
    error?: string
    required?: boolean
    autocomplete?: string
    mono?: boolean
    disabled?: boolean
    name?: string
    inputmode?: 'text' | 'email' | 'url' | 'search' | 'none'
  }>(),
  { type: 'text', mono: false, required: false, disabled: false },
)
const emit = defineEmits<{ 'update:modelValue': [string] }>()
const id = useId()
const describedBy = computed(() => (props.error ? `${id}-err` : props.hint ? `${id}-hint` : undefined))
</script>

<template>
  <div class="field" :class="{ invalid: !!error }">
    <label v-if="label" :for="id" class="label">{{ label }}<span v-if="required" class="req" aria-hidden="true"> *</span></label>
    <div class="control">
      <slot name="prefix" />
      <input
        :id="id"
        :name="name"
        :type="type"
        :value="modelValue"
        :placeholder="placeholder"
        :required="required"
        :autocomplete="autocomplete"
        :disabled="disabled"
        :inputmode="inputmode"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="describedBy"
        :class="{ mono }"
        spellcheck="false"
        @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      />
      <slot name="suffix" />
    </div>
    <p v-if="error" :id="`${id}-err`" class="msg err" role="alert">{{ error }}</p>
    <p v-else-if="hint" :id="`${id}-hint`" class="msg hint">{{ hint }}</p>
  </div>
</template>

<style scoped>
.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}
.label {
  font-size: var(--text-sm);
  font-weight: 500;
  color: var(--text);
}
.req {
  color: var(--error);
}
.control {
  display: flex;
  align-items: center;
  gap: var(--s-2);
  min-width: 0;
}
input {
  flex: 1;
  min-width: 0;
  height: 40px;
  padding: 0 12px;
  border-radius: var(--r-md);
  border: 1px solid var(--border);
  background: #fff;
  color: var(--text);
  font-size: var(--text-md);
  transition: border-color var(--dur-fast) var(--ease), box-shadow var(--dur-fast) var(--ease);
}
input.mono {
  font-family: var(--font-mono);
  font-size: var(--text-sm);
}
input::placeholder {
  color: var(--text-faint);
}
input:hover:not(:disabled) {
  border-color: var(--text-muted);
}
input:focus-visible {
  outline: none;
  border-color: var(--accent);
  box-shadow: 0 0 0 3px var(--accent-ring);
}
input:disabled {
  background: var(--surface-sunken);
  color: var(--text-muted);
}
.invalid input {
  border-color: var(--error);
}
.msg {
  font-size: var(--text-xs);
  line-height: 1.4;
}
.hint {
  color: var(--text-muted);
}
.err {
  color: var(--error);
}
</style>
