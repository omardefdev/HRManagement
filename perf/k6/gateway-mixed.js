import http from 'k6/http';
import { check, sleep } from 'k6';
import { authHeaders, baseUrl } from './lib/auth.js';
import { httpThresholds, stagesFor } from './lib/thresholds.js';

const profile = __ENV.NFR_PROFILE || 'load';

export const options = {
  stages: stagesFor(profile),
  thresholds: httpThresholds(profile),
};

const paths = [
  '/health/ready',
  '/health',
  '/api/hr-core/api/lookup/employees?q=NF&page=1&pageSize=20',
];

export default function () {
  const path = paths[Math.floor(Math.random() * paths.length)];
  const res = http.get(`${baseUrl()}${path}`, {
    headers: authHeaders(),
    tags: { name: path.split('?')[0] },
  });
  check(res, {
    'status ok-ish': (r) => r.status === 200 || r.status === 401 || r.status === 403 || r.status === 503,
  });
  sleep(Number(__ENV.THINK_TIME || 0.3));
}

export function handleSummary(data) {
  return { 'summary.json': JSON.stringify(data, null, 2) };
}
