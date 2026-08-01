// NFR-001 thresholds derived from OBS-002 J2 (and loose stress variants)
export function httpThresholds(profile) {
  const p = (profile || __ENV.NFR_PROFILE || 'load').toLowerCase();
  if (p === 'smoke') {
    return {
      http_req_failed: ['rate<0.05'],
      http_req_duration: ['p(95)<5000'],
    };
  }
  if (p === 'stress') {
    // Allow higher error with documented degradation; still fail on total collapse.
    return {
      http_req_failed: ['rate<0.05'],
      http_req_duration: ['p(95)<4000'],
    };
  }
  // load | soak | spike — align with J2 P95 < 2s
  return {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<2000'],
  };
}

export function stagesFor(profile) {
  const p = (profile || __ENV.NFR_PROFILE || 'load').toLowerCase();
  const base = Number(__ENV.VUS_BASE || 50);
  switch (p) {
    case 'smoke':
      return [{ duration: '30s', target: 2 }];
    case 'soak':
      // Lab soak ≥2h @100% NFR model (override with SOAK_DURATION)
      return [
        { duration: '2m', target: base },
        { duration: __ENV.SOAK_DURATION || '2h', target: base },
        { duration: '2m', target: 0 },
      ];
    case 'spike':
      return [
        { duration: '30s', target: base },
        { duration: '1m', target: Number(__ENV.SPIKE_VUS || base * 3) },
        { duration: '2m', target: base },
        { duration: '30s', target: 0 },
      ];
    case 'stress':
      return [
        { duration: '2m', target: base },
        { duration: '5m', target: Math.ceil(base * 1.5) },
        { duration: '2m', target: 0 },
      ];
    case 'load':
    default:
      return [
        { duration: '1m', target: Math.ceil(base / 2) },
        { duration: '3m', target: base },
        { duration: '1m', target: 0 },
      ];
  }
}
