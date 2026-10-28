// k6 load profile for the AdPulse API.
//   k6 run -e BASE=http://localhost:8080 -e KEY=acme-demo-key scripts/bench.k6.js
// Thresholds fail the run if p95 > 150 ms or error rate > 1 % - the same budget documented in docs/benchmarks.md.
import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE = __ENV.BASE || 'http://localhost:8080';
const KEY = __ENV.KEY || 'acme-demo-key';

export const options = {
  scenarios: {
    dashboard_readers: { executor: 'ramping-vus', stages: [{ duration: '30s', target: 25 }, { duration: '60s', target: 25 }, { duration: '15s', target: 0 }] },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<150'],
    'http_req_duration{name:summary}': ['p(95)<150'],
    'http_req_duration{name:cross-channel}': ['p(95)<120'],
  },
};

const params = (name) => ({ headers: { 'X-Api-Key': KEY }, tags: { name } });

export function setup() {
  const campaigns = http.get(`${BASE}/api/campaigns`, params('list')).json();
  return { id: campaigns[0].id };
}

export default function (data) {
  // one "dashboard refresh" = the four calls the Vue app makes
  const res = http.batch([
    ['GET', `${BASE}/api/campaigns/summary`, null, params('summary')],
    ['GET', `${BASE}/api/reports/cross-channel`, null, params('cross-channel')],
    ['GET', `${BASE}/api/reports/daily`, null, params('daily')],
    ['GET', `${BASE}/api/campaigns/${data.id}/pacing`, null, params('pacing')],
  ]);
  res.forEach((r) => check(r, { 'status 200': (x) => x.status === 200 }));
  sleep(0.5);
}
