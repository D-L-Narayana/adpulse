<script setup lang="ts">
import { ref } from 'vue'

const props = withDefaults(defineProps<{ value: string; label?: string; masked?: string; multiline?: boolean; ariaLabel?: string }>(), { multiline: false })
const copied = ref(false)
const revealed = ref(false)
async function copy() {
  try {
    await navigator.clipboard.writeText(props.value)
    copied.value = true
    setTimeout(() => (copied.value = false), 1600)
  } catch {
    // Clipboard may be blocked (insecure context) — select the text instead.
    const el = document.getElementById(id)
    if (el) {
      const r = document.createRange()
      r.selectNodeContents(el)
      getSelection()?.removeAllRanges()
      getSelection()?.addRange(r)
    }
  }
}
const id = `cf-${Math.random().toString(36).slice(2, 8)}`
</script>

<template>
  <div class="cf" :class="{ multiline }">
    <span v-if="label" class="lbl">{{ label }}</span>
    <div class="box">
      <pre v-if="multiline" :id="id" class="val" tabindex="0" :aria-label="ariaLabel ?? label"><code>{{ value }}</code></pre>
      <code v-else :id="id" class="val single" :aria-label="ariaLabel ?? label">{{ masked && !revealed ? masked : value }}</code>
      <div class="btns">
        <button v-if="masked" type="button" class="b" :aria-pressed="revealed" @click="revealed = !revealed">{{ revealed ? 'Hide' : 'Reveal' }}</button>
        <button type="button" class="b" :aria-live="copied ? 'polite' : 'off'" @click="copy">{{ copied ? 'Copied' : 'Copy' }}</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.cf {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}
.lbl {
  font-size: var(--text-sm);
  font-weight: 500;
}
.box {
  display: flex;
  align-items: stretch;
  gap: var(--s-2);
  background: var(--surface-sunken);
  border: 1px solid var(--border);
  border-radius: var(--r-md);
  padding: 6px 6px 6px 12px;
  min-width: 0;
}
.multiline .box {
  flex-direction: column;
  padding: 12px;
}
.val {
  flex: 1;
  margin: 0;
  font-family: var(--font-mono);
  font-size: var(--text-xs);
  line-height: 1.6;
  overflow-x: auto;
  white-space: pre;
  min-width: 0;
  color: var(--text);
  align-self: center;
}
.val.single {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.btns {
  display: flex;
  gap: 4px;
  flex-shrink: 0;
  align-self: center;
}
.multiline .btns {
  align-self: flex-end;
}
.b {
  height: 28px;
  padding: 0 10px;
  font-size: var(--text-xs);
  font-weight: 500;
  border: 1px solid var(--border);
  background: #fff;
  border-radius: var(--r-sm);
  cursor: pointer;
  color: var(--text);
}
.b:hover {
  border-color: var(--text-muted);
}
</style>
