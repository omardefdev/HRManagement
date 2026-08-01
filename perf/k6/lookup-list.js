import http from 'k6/http';
import { check, sleep } from 'k6';
import { authHeaders, baseUrl } from './lib/auth.js';
import { httpThresholds, stagesFor } from './lib/thresholds.js';

const profile = __ENV.NFR_PROFILE || 'load';

export const options = {
  stages: stagesFor(profile),
  thresholds: httpThresholds(profile),
};

export default function () {
  const q = `NF${(__VU % 100) + 1}`;
  const page = (__ITER % 5) + 1;
  const url = `${baseUrl()}/api/hr-core/api/lookup/employees?q=${encodeURIComponent(q)}&page=${page}&pageSize=20`;
  const res = http.get(url, { headers: authHeaders(), tags: { name: 'lookup-employees' } });
  check(res, {
    'lookup 200/401/403': (r) => [200, 401, 403].includes(r.status),
  });

  const list = http.get(`${baseUrl()}/ui/hr-core/Employees?page=1&pageSize=25`, {
    headers: authHeaders(),
    tags: { name: 'employees-index' },
  });
  check(list, {
    'list responded': (r) => r.status > 0,
  });
  sleep(Number(__ENV.THINK_TIME || 0.4));
}

export function handleSummary(data) {
  return { 'summary.json': JSON.stringify(data, null, 2) };
}
