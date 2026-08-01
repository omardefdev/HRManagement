// NFR-001 shared auth helpers for k6
export function authHeaders() {
  const token = __ENV.ACCESS_TOKEN || '';
  const h = { Accept: 'application/json' };
  if (token) {
    h.Authorization = `Bearer ${token}`;
  }
  return h;
}

export function baseUrl() {
  return (__ENV.BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
}
