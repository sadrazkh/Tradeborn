<script setup lang="ts">
import { ref } from 'vue'
import { login, register } from '@/api/client'

const emit = defineEmits<{ (e: 'authenticated'): void }>()

const mode = ref<'login' | 'register'>('register')
const email = ref('')
const password = ref('')
const displayName = ref('')
const busy = ref(false)
const error = ref('')

async function submit() {
  if (busy.value) return
  error.value = ''
  busy.value = true

  try {
    if (mode.value === 'register') {
      await register(email.value, password.value, displayName.value)
    } else {
      await login(email.value, password.value)
    }
    emit('authenticated')
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Something went wrong.'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="screen">
    <form class="panel tb-panel" @submit.prevent="submit">
      <div class="mark">TRADEBORN</div>
      <p class="tagline">Build a trading city you can watch work.</p>

      <label>
        <span>Email</span>
        <input v-model="email" type="email" required autocomplete="email" :disabled="busy" />
      </label>

      <label v-if="mode === 'register'">
        <span>Name</span>
        <input v-model="displayName" type="text" maxlength="32" placeholder="Founder" :disabled="busy" />
      </label>

      <label>
        <span>Password</span>
        <input
          v-model="password"
          type="password"
          required
          minlength="8"
          :autocomplete="mode === 'register' ? 'new-password' : 'current-password'"
          :disabled="busy"
        />
      </label>

      <p v-if="error" class="error" role="alert">{{ error }}</p>

      <button type="submit" :disabled="busy">
        {{ busy ? 'Please wait…' : mode === 'register' ? 'Found my city' : 'Return to my city' }}
      </button>

      <button type="button" class="switch" :disabled="busy" @click="mode = mode === 'register' ? 'login' : 'register'">
        {{ mode === 'register' ? 'I already have a city' : 'Start a new city' }}
      </button>
    </form>
  </div>
</template>

<style scoped>
.screen {
  position: fixed;
  inset: 0;
  display: grid;
  place-items: center;
  background: radial-gradient(circle at 50% 30%, #1d2b3d 0%, #131b26 70%);
  padding: 24px;
}

.panel {
  padding: 30px 32px;
  width: min(360px, 100%);
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.mark {
  font-size: 21px;
  font-weight: 700;
  letter-spacing: 0.18em;
  color: var(--tb-gold);
  text-align: center;
}

.tagline {
  margin: -6px 0 6px;
  text-align: center;
  color: var(--tb-text-dim);
  font-size: 13px;
  line-height: 1.5;
}

label {
  display: flex;
  flex-direction: column;
  gap: 5px;
  font-size: 12px;
  color: var(--tb-text-dim);
}

input {
  background: rgba(255, 255, 255, 0.06);
  border: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 10px;
  padding: 10px 12px;
  color: var(--tb-text);
  font-size: 14px;
  font-family: inherit;
}

input:focus {
  outline: none;
  border-color: var(--tb-gold);
}

button {
  border: 0;
  border-radius: 10px;
  padding: 11px 16px;
  font-weight: 650;
  font-size: 14px;
  font-family: inherit;
  cursor: pointer;
  background: var(--tb-gold);
  color: #16202e;
}

button:disabled {
  opacity: 0.6;
  cursor: default;
}

.switch {
  background: transparent;
  color: var(--tb-text-dim);
  font-weight: 500;
  font-size: 12px;
  padding: 4px;
}

.error {
  margin: 0;
  color: var(--tb-danger);
  font-size: 12px;
  line-height: 1.5;
}
</style>
