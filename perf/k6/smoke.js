import http from 'k6/http';
import { check, sleep } from 'k6';
import { authHeaders, baseUrl } from './lib/auth.js';
import { httpThresholds } from './lib/thresholds.js';

export const options = {
  vus: 1,
  duration: '20s',
  thresholds: httpThresholds('smoke'),
};

export default function () {
  const url = `${baseUrl()}/health/ready`;
  const res = http.get(url, { headers: authHeaders(), tags: { name: 'ready' } });
  check(res, {
    'ready status 200/503': (r) => r.status === 200 || r.status === 503,
  });
  sleep(0.5);
}

export function handleSummary(data) {
  return {
    stdout: textSummary(data),
    'summary.json': JSON.stringify(data, null, 2),
  };
}

function textSummary(data) {
  const m = data.metrics || {};
  const failed = m.http_req_failed && m.http_req_failed.values ? m.http_req_failed.values.rate : 'n/a';
  const p95 = m.http_req_duration && m.http_req_duration.values ? m.http_req_duration.values['p(95)'] : 'n/a';
  return `NFR-001 smoke: failed_rate=${failed} p95_ms=${p95}\n`;
}
