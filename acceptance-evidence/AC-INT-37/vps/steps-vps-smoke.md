1. scripts/vps/smoke.ps1 -SshHost hrm-vps -RequireIntegrationWiring (the wiring checks are mandatory).
2. fail=0, deferred=0; every wiring-env (14), wiring-token (13), legal-pack (3) and the S2S candidate-upload call Pass.
3. Not run on the server: 37a and 37c (they need acceptance personas; the VpsDemo realm has the demo users only).