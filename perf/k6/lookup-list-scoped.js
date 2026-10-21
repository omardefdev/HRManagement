import http from 'k6/http';
import { check, sleep } from 'k6';
import { authHeaders, baseUrl } from './lib/auth.js';
import { httpThresholds, stagesFor } from './lib/thresholds.js';

const profile = __ENV.NFR_PROFILE || 'load';

export const options = {
  stages: stagesFor(profile),
  thresholds: httpThresholds(profile),
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
};

export default function () {
  const q = `ACC${String((__VU % 9) + 1).padStart(2, '0')}`;
  const url = `${baseUrl()}/api/hr-core/api/lookup/employees?q=${encodeURIComponent(q)}&page=1&pageSize=20`;
  const res = http.get(url, { headers: authHeaders(), tags: { name: 'lookup-employees-scoped' } });
  check(res, {
    'scoped lookup 200': (r) => r.status === 200,
    'no server error': (r) => r.status < 500,
    'json body': (r) => r.headers['Content-Type'] && r.headers['Content-Type'].includes('json'),
  });
  if (res.status === 200 && res.body) {
    try {
      const doc = JSON.parse(res.body);
      const count = doc.items ? doc.items.length : doc.totalCount ?? 0;
      check(null, { 'within page size': () => count <= 20 });
    } catch (_) {
      // body parse optional
    }
  }
  sleep(Number(__ENV.THINK_TIME || 0.4));
}

export function handleSummary(data) {
  return { 'summary.json': JSON.stringify(data, null, 2) };
}
