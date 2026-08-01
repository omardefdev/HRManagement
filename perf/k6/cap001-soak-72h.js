import http from 'k6/http';
import { check, sleep } from 'k6';
import {
  baseUrl,
  cap001MixedPaths,
  cap001Stages,
  cap001Thresholds,
  payrollWindowActive,
  regionalTag,
  requestHeaders,
} from './lib/cap001.js';

/**
 * CAP-001 soak @100% peak — national envelope (72h production; lab via SOAK_DURATION).
 * Extends gateway-mixed with org-tree, regional tags, optional payroll-window weighting.
 */
const kind = 'soak';

export const options = {
  stages: cap001Stages(kind),
  thresholds: cap001Thresholds(kind),
  tags: { cap001: 'soak-100', card: 'CAP-001' },
};

export default function () {
  const paths = cap001MixedPaths();
  let path = paths[Math.floor(Math.random() * paths.length)];

  if (payrollWindowActive()) {
    path = Math.random() < 0.35
      ? '/api/hr-core/api/lookup/employees?q=PY&page=1&pageSize=50'
      : path;
  }

  const regionTags = regionalTag(__VU);
  const { headers, tags } = requestHeaders({
    name: path.split('?')[0],
    ...regionTags,
    season: payrollWindowActive() ? 'payroll-window' : 'steady',
  });

  const res = http.get(`${baseUrl()}${path}`, { headers, tags });
  check(res, {
    'status ok-ish': (r) => [200, 401, 403, 429, 503].includes(r.status),
  });
  sleep(Number(__ENV.THINK_TIME || 0.3));
}

export function handleSummary(data) {
  return { 'summary.json': JSON.stringify(data, null, 2) };
}
