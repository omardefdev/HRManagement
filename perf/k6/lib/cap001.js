// CAP-001 — National Workload Envelope shared k6 helpers (extends NFR-001 lib/thresholds.js)
import { httpThresholds } from './thresholds.js';
import { authHeaders, baseUrl } from './auth.js';

/** 14 Syria governorates for regional mix weighting (STR-001 GeographyWaves). */
export const GOVERNORATES = [
  'damascus', 'rif-dimashq', 'aleppo', 'homs', 'hama', 'latakia', 'tartus',
  'idlib', 'deir-ez-zor', 'raqqa', 'hasakah', 'daraa', 'suwayda', 'quneitra',
];

export function cap001BaseVus() {
  return Number(__ENV.VUS_BASE || 50);
}

export function cap001Stages(kind) {
  const base = cap001BaseVus();
  switch (kind) {
    case 'soak':
      return [
        { duration: '5m', target: base },
        { duration: __ENV.SOAK_DURATION || '72h', target: base },
        { duration: '5m', target: 0 },
      ];
    case 'stress': {
      const target = Math.ceil(base * 1.5);
      return [
        { duration: '10m', target: base },
        { duration: __ENV.STRESS_DURATION || '4h', target },
        { duration: '10m', target: 0 },
      ];
    }
    case 'spike': {
      const spike = Math.ceil(base * 2);
      return [
        { duration: '2m', target: base },
        { duration: __ENV.SPIKE_DURATION || '15m', target: spike },
        { duration: '3m', target: base },
        { duration: '2m', target: 0 },
      ];
    }
    case 'failure':
      return [
        { duration: '5m', target: base },
        { duration: __ENV.FAILURE_LOAD_DURATION || '30m', target: base },
        { duration: '5m', target: 0 },
      ];
    default:
      return [{ duration: '5m', target: base }];
  }
}

export function cap001Thresholds(kind) {
  if (kind === 'stress' || kind === 'spike') {
    return httpThresholds('stress');
  }
  return httpThresholds('load');
}

export function cap001MixedPaths() {
  return [
    '/health/ready',
    '/health',
    '/api/hr-core/api/lookup/employees?q=NF&page=1&pageSize=20',
    '/api/hr-core/api/lookup/org-units/tree',
  ];
}

export function regionalTag(vu) {
  const gov = GOVERNORATES[(vu - 1) % GOVERNORATES.length];
  return { region: gov, mix: 'regional' };
}

export function payrollWindowActive() {
  return (__ENV.CAP001_PAYROLL_WINDOW || 'false').toLowerCase() === 'true';
}

export function failureInjectionTag() {
  const scenario = __ENV.FAILURE_SCENARIO || 'none';
  return {
    failure_injection: scenario,
    cap001: 'failure-during-load',
  };
}

export function requestHeaders(extraTags) {
  const h = authHeaders();
  const region = GOVERNORATES[(__VU - 1) % GOVERNORATES.length];
  h['X-CAP001-Region'] = region;
  if (__ENV.TENANT_ID) {
    h['X-Tenant-Id'] = __ENV.TENANT_ID;
  }
  return { headers: h, tags: extraTags || {} };
}

export { authHeaders, baseUrl };
