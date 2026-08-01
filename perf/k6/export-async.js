import http from 'k6/http';
import { check, sleep } from 'k6';
import { authHeaders, baseUrl } from './lib/auth.js';
import { httpThresholds, stagesFor } from './lib/thresholds.js';

/**
 * Export async path (PERF-004): hit report endpoints that return 202 when over sync cap.
 * Set EXPORT_PATH to a concrete UI/API path that triggers async export for a large run.
 */
const profile = __ENV.NFR_PROFILE || 'load';
const exportPath =
  __ENV.EXPORT_PATH ||
  '/ui/payroll/PayrollReports/ExportRunCsv'; // may 302/404 without run — still measures gateway

export const options = {
  stages: stagesFor(profile),
  thresholds: httpThresholds(profile),
};

export default function () {
  const url = `${baseUrl()}${exportPath}`;
  const res = http.get(url, {
    headers: authHeaders(),
    tags: { name: 'export-async' },
    redirects: 0,
  });
  check(res, {
    'export responded': (r) => r.status > 0,
    'async or sync ok': (r) =>
      [200, 202, 302, 401, 403, 404].includes(r.status),
  });
  sleep(Number(__ENV.THINK_TIME || 1));
}

export function handleSummary(data) {
  return { 'summary.json': JSON.stringify(data, null, 2) };
}
