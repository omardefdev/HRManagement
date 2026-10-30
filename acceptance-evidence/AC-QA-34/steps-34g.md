1. bash scripts/prepare-ci-compose-env.sh; docker compose -f docker-compose.yml -f docker-compose.services.yml
   --profile full-stack up -d --wait --wait-timeout 600 (no MINIO_IMAGE, no MinIO container).
2. file-storage-web runs with FileStorage__Provider=Local; gateway /health/live = 200.
3. The AC-28 flow (scripts/acceptance-file-scan.ps1) through the gateway, done in-process: token for e2e-file-ops (CI
   realm persona with the file-storage role, scope openid), POST /api/files clean.pdf → Clean and downloadable; the same PDF with the EICAR signature →
   Infected/Quarantined and its download refused.
4. Negative: `--profile object-storage config` without MINIO_IMAGE fails; the object-storage overlay without it
   fails with the explicit message.