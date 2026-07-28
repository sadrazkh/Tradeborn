import http from 'k6/http'
import { check, sleep } from 'k6'
import { Trend, Rate } from 'k6/metrics'

/**
 * Drives the vertical slice's core loop under load.
 *
 * Deliberately exercises the *whole* loop rather than hammering one endpoint. A benchmark that
 * only reads the city would report excellent numbers and tell us nothing: the interesting
 * contention is the shared market price row, which only appears when many players sell at once.
 */

const BASE = __ENV.BASE_URL || 'http://localhost:5084'

const cityRead = new Trend('tb_city_read', true)
const command = new Trend('tb_command', true)
const sale = new Trend('tb_sale', true)
const refused = new Rate('tb_refused')

export const options = {
  thresholds: {
    // Targets from docs/architecture/PERFORMANCE_BUDGET.md §6.
    tb_city_read: ['p(95)<120'],
    tb_command: ['p(95)<150'],
    tb_sale: ['p(95)<150'],
    http_req_failed: ['rate<0.001'],
  },
}

function idempotencyKey() {
  // Per attempt, not per request: a retry of the same attempt must reuse it or the whole
  // point of the header is lost.
  return `${__VU}-${__ITER}-${Math.random().toString(36).slice(2)}`
}

/** Registers a fresh account and returns its access token. */
export function setup() {
  return { startedAt: new Date().toISOString() }
}

export default function () {
  const email = `load-${__VU}-${__ITER}-${Date.now()}@example.invalid`

  const registered = http.post(
    `${BASE}/api/auth/register`,
    JSON.stringify({ email, password: 'correct horse battery', displayName: `Load ${__VU}` }),
    { headers: { 'Content-Type': 'application/json' } },
  )

  if (registered.status !== 200) {
    // Registration is rate limited to 5/hour per IP, so a distributed run needs distinct
    // source addresses. Bailing loudly beats reporting a throughput number that only measures
    // how fast the limiter says no.
    check(registered, { 'registered (raise the register limit or use more source IPs)': (r) => r.status === 200 })
    return
  }

  const token = registered.json('accessToken')
  const auth = { headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } }

  // 1. Read the city.
  let response = http.get(`${BASE}/api/cities/me`, auth)
  cityRead.add(response.timings.duration)
  check(response, { 'city read': (r) => r.status === 200 })

  // 2. Build a Lumber Camp.
  response = http.post(
    `${BASE}/api/cities/me/buildings`,
    JSON.stringify({ definitionId: 'lumber_camp', col: 1, row: 2 }),
    { headers: { ...auth.headers, 'Idempotency-Key': idempotencyKey() } },
  )
  command.add(response.timings.duration)
  refused.add(response.status === 409)

  const buildingId = response.status === 200 ? response.json('building.id') : null

  // 3. Wait out the 30 s build, then switch it on.
  sleep(31)

  if (buildingId) {
    response = http.put(
      `${BASE}/api/cities/me/buildings/${buildingId}/production`,
      JSON.stringify({ active: true }),
      { headers: { ...auth.headers, 'Idempotency-Key': idempotencyKey() } },
    )
    command.add(response.timings.duration)
  }

  // 4. Let production and a delivery happen.
  sleep(45)

  // 5. Sell — the step that contends on the shared market price row, and therefore the one
  //    most likely to serialise under load.
  response = http.post(
    `${BASE}/api/market/sell`,
    JSON.stringify({ resource: 'wood', quantity: 20 }),
    { headers: { ...auth.headers, 'Idempotency-Key': idempotencyKey() } },
  )
  sale.add(response.timings.duration)
  refused.add(response.status === 409)

  // 6. Read back, so the run measures the read path under concurrent writes rather than in
  //    isolation.
  response = http.get(`${BASE}/api/cities/me`, auth)
  cityRead.add(response.timings.duration)

  sleep(1)
}
