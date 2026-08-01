import http from 'k6/http';
import { check, sleep } from 'k6';
import {
  baseUrl,
  cap001MixedPaths,
  cap001Stages,
  cap001Thresholds,
  regionalTag,
  requestHeaders,
} from './lib/cap001.js';

/**
 * CAP-001 spike @200% for 15m — burst then recovery to baseline.
 */
const kind = 'spike';

export const options = {
  stages: cap001Stages(kind),
  thresholds: cap001Thresholds(kind),
  tags: { cap001: 'spike-200', card: 'CAP-001' },
};

export default function () {
  const paths = cap001MixedPaths();
  const path = paths[Math.floor(Math.random() * paths.length)];
  const { headers, tags } = requestHeaders({
    name: path.split('?')[0],
    ...regionalTag(__VU),
    load_pct: '200',
  });

  const res = http.get(`${baseUrl()}${path}`, { headers, tags });
  check(res, {
    'responded': (r) => r.status > 0,
    'burst survivable': (r) => [200, 401, 403, 429, 503, 502, 504].includes(r.status),
  });
  sleep(Number(__ENV.THINK_TIME || 0.1));
}

export function handleSummary(data) {
  return { 'summary.json': JSON.stringify(data, null, 2) };
}
