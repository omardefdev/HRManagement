import http from 'k6/http';
import { check, sleep } from 'k6';
import {
  baseUrl,
  cap001MixedPaths,
  cap001Stages,
  cap001Thresholds,
  failureInjectionTag,
  regionalTag,
  requestHeaders,
} from './lib/cap001.js';

/**
 * CAP-001 failure-during-load — sustained 100% with FAILURE_SCENARIO tag for chaos correlation.
 * Pair with docker-compose.chaos.yml or scripts/nfr-chaos-inject.ps1 during the load window.
 */
const kind = 'failure';
const scenario = __ENV.FAILURE_SCENARIO || 'none';

export const options = {
  stages: cap001Stages(kind),
  thresholds: cap001Thresholds(kind),
  tags: {
    cap001: 'failure-during-load',
    card: 'CAP-001',
    failure_injection: scenario,
  },
};

export default function () {
  const paths = cap001MixedPaths();
  const path = paths[Math.floor(Math.random() * paths.length)];
  const injectTags = failureInjectionTag();
  const { headers, tags } = requestHeaders({
    name: path.split('?')[0],
    ...regionalTag(__VU),
    ...injectTags,
  });

  const res = http.get(`${baseUrl()}${path}`, { headers, tags });
  check(res, {
    'explicit response': (r) => r.status > 0,
    'no silent drop': (r) => r.status !== 0,
    'failure handled': (r) =>
      [200, 401, 403, 429, 503, 502, 504, 500].includes(r.status),
  });

  if (scenario === 'payroll_worker_loss' && __ITER % 20 === 0) {
    const runId = __ENV.PAYROLL_RUN_ID || '';
    if (runId) {
      const pr = http.get(`${baseUrl()}/api/payroll/runs/${runId}`, {
        headers: requestHeaders({ name: 'payroll-get-run', ...injectTags }).headers,
        tags: { name: 'payroll-get-run', ...injectTags },
      });
      check(pr, {
        'payroll status visible': (r) => [200, 401, 404, 503].includes(r.status),
      });
    }
  }

  sleep(Number(__ENV.THINK_TIME || 0.25));
}

export function handleSummary(data) {
  return { 'summary.json': JSON.stringify(data, null, 2) };
}
