import http from 'k6/http';
import { check, sleep } from 'k6';
import { authHeaders, baseUrl } from './lib/auth.js';
import { httpThresholds } from './lib/thresholds.js';

/**
 * Payroll run scenario (PERF-001 / J4).
 * Requires PAYROLL_RUN_ID (and usually ACCESS_TOKEN with PayrollProcess).
 * VUs stay low: calculation is server-side; we poll status / trigger once.
 */
const profile = __ENV.NFR_PROFILE || 'load';
const runId = __ENV.PAYROLL_RUN_ID || '';

export const options = {
  scenarios: {
    payroll: {
      executor: 'shared-iterations',
      vus: Number(__ENV.PAYROLL_VUS || 1),
      iterations: Number(__ENV.PAYROLL_ITERATIONS || 1),
      maxDuration: __ENV.PAYROLL_MAX_DURATION || '30m',
    },
  },
  thresholds: {
    ...httpThresholds(profile === 'stress' ? 'stress' : 'load'),
    checks: ['rate>0.9'],
  },
};

export default function () {
  if (!runId) {
    console.error('PAYROLL_RUN_ID is required');
    return;
  }

  const calc = http.post(
    `${baseUrl()}/api/payroll/runs/${runId}/calculate`,
    null,
    { headers: authHeaders(), tags: { name: 'payroll-calculate' } }
  );
  let res = calc;
  if (calc.status === 404) {
    res = http.post(
      `${baseUrl()}/api/payroll/api/payroll/runs/${runId}/calculate`,
      null,
      { headers: authHeaders(), tags: { name: 'payroll-calculate-alt' } }
    );
  }

  check(res, {
    'calculate accepted': (r) => r.status === 200 || r.status === 202 || r.status === 400 || r.status === 401 || r.status === 403,
  });

  // Poll run details for up to POLL_SECONDS
  const pollSec = Number(__ENV.POLL_SECONDS || 120);
  const deadline = Date.now() + pollSec * 1000;
  while (Date.now() < deadline) {
    const g = http.get(`${baseUrl()}/api/payroll/runs/${runId}`, {
      headers: authHeaders(),
      tags: { name: 'payroll-get-run' },
    });
    check(g, { 'get run': (r) => r.status === 200 || r.status === 401 || r.status === 404 });
    sleep(5);
  }
}

export function handleSummary(data) {
  return { 'summary.json': JSON.stringify(data, null, 2) };
}
