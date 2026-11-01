# AC-OPS-36e on the rebuilt server (PLAN-38 P7)

AC-OPS-36e (two signed-in users behind one address: 200 asset HEADs + 25 module pages at once each, then an anonymous
burst) ran four times from the owner's machine against tag `7bd85bc3ac9d` on 2026-11-01 (18:2xZ–19:0xZ):

| Attempt | Result |
|---|---|
| 1 (full run) | Fail: HttpClient timeout (60 s) on `/ui/<module>/` GETs |
| 2 | Fail: same |
| 3 | Blocked: the test's gate `ssh hrm-vps docker ps` failed (the stack was up: 21 containers healthy) |
| 4 | Fail: same timeout |
| 5 (2026-11-01T21:5xZ, single requests back to 0.6–1.3 s) | **Pass**: two signed-in users 400 assets + 50 pages, no 429; anonymous 297 of 400 passed in 18 s, the rest 429 with Retry-After 60 |

Server side, for the same windows (gateway request log):

- attempt 2: 472 `/ui/` requests started and 472 finished, slowest 65 ms; no warning or error in any container; no pool,
  connection-slot or timeout error since the deploy;
- attempt 4: only 214 of the burst's requests reached the gateway, all finished (slowest 70 ms): the rest never arrived.

The same burst shape sent from the server to its own public edge (`curl --parallel`, 200 asset HEADs + 25 module pages,
anonymous): 225 responses in 504 ms (slowest 0.48 s); a second round at once was limited by the anonymous 300-per-minute
rule (151 x 429), as designed.

At the same time a single request from the owner's machine took 1.2–8.3 s and a local `docker pull` ran at a few KB/s.
Conclusion: the edge and the gateway answer the burst; the timeouts were on the owner's network path, and attempt 5, once the link
recovered, passed. The previous server
run of 36e (2026-11-01T01:57Z, PLAN-37) passed (anonymous 300 of 400 in 7 s).
