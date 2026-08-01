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
  const tree = `${baseUrl()}/api/hr-core/api/lookup/org-units/tree`;
  const res = http.get(tree, { headers: authHeaders(), tags: { name: 'org-tree' } });
  check(res, {
    'tree 200/401/403': (r) => [200, 401, 403].includes(r.status),
    'bounded body': (r) => !r.body || r.body.length < 5_000_000,
  });
  sleep(Number(__ENV.THINK_TIME || 0.5));
}

export function handleSummary(data) {
  return { 'summary.json': JSON.stringify(data, null, 2) };
}
