# S2S secret matrix (PLAN-42)

Which secret each container sees, from a code inventory (`configuration["..."]`, `GetConnectionString`, options binding), not from guesswork. A plan that makes a service read a new secret updates this file and `deploy/k8s/overlays/*/external-secrets/<deployment>-secrets.yaml` in the same commit. `S2s042K8sPerServiceSecretsTests` holds the rules.

## Rules

- A Deployment references `<deployment>-secrets` and nothing else. The shared `hr-app-secrets` is gone.
- `GatewayCallerProof__PrivateKeyPem` exists in `hr-gateway-secrets` alone; a module that holds it refuses to start.
- `Keycloak__DirectorySync__AdminClientSecret` exists in `auth-directory-web-secrets` alone.
- `FileStorage__AccessKey` and `FileStorage__SecretKey` exist in `file-storage-web-secrets` alone.
- `GatewayCallerProof__PublicKeyPem` is in every module's Secret because that is where the overlay gets its value; it is not a secret.
- `hr-migrate-secrets` is the one reader of all 15 connection strings: the migration Job, no application container.

## Per container

| Service | Secret | Keys |
|---------|--------|------|
| `admin-logistics-web` | `admin-logistics-web-secrets` | `ConnectionStrings__AdminDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_ADMIN_LOGISTICS_CLIENT_SECRET` |
| `auth-directory-web` | `auth-directory-web-secrets` | `ConnectionStrings__AuthDirectoryDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_AUTH_DIRECTORY_CLIENT_SECRET`<br>`Keycloak__DirectorySync__AdminClientSecret` |
| `compliance-web` | `compliance-web-secrets` | `ConnectionStrings__ComplianceDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_COMPLIANCE_CLIENT_SECRET` |
| `dev-analytics-web` | `dev-analytics-web-secrets` | `ConnectionStrings__AnalyticsDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_DEV_ANALYTICS_CLIENT_SECRET` |
| `file-storage-web` | `file-storage-web-secrets` | `ConnectionStrings__FileStorageDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`FileStorage__AccessKey`<br>`FileStorage__SecretKey` |
| `hr-contracts-web` | `hr-contracts-web-secrets` | `ConnectionStrings__ContractsDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_HR_CONTRACTS_CLIENT_SECRET` |
| `hr-core-web` | `hr-core-web-secrets` | `ConnectionStrings__HRCoreDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_HR_CORE_CLIENT_SECRET`<br>`Recruitment__Portal__OtpHmacKey` |
| `hr-gateway` | `hr-gateway-secrets` | `ConnectionStrings__GatewayAuditDb`<br>`Keycloak__ClientSecret`<br>`GatewayCallerProof__PrivateKeyPem` |
| `hr-health-web` | `hr-health-web-secrets` | `ConnectionStrings__HealthDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_HR_HEALTH_CLIENT_SECRET` |
| `hr-movements-web` | `hr-movements-web-secrets` | `ConnectionStrings__MovementsDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_HR_MOVEMENTS_CLIENT_SECRET` |
| `hr-training-web` | `hr-training-web-secrets` | `ConnectionStrings__TrainingDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_HR_TRAINING_CLIENT_SECRET` |
| `idcard-web` | `idcard-web-secrets` | `ConnectionStrings__IDCardDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_IDCARD_CLIENT_SECRET` |
| `it-infra-web` | `it-infra-web-secrets` | `ConnectionStrings__ITInfraDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_IT_INFRA_CLIENT_SECRET` |
| `payroll-web` | `payroll-web-secrets` | `ConnectionStrings__PayrollDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_PAYROLL_CLIENT_SECRET` |
| `records-archive-web` | `records-archive-web-secrets` | `ConnectionStrings__ArchiveDb`<br>`GatewayCallerProof__PublicKeyPem`<br>`RabbitMQ__Username`<br>`RabbitMQ__Password`<br>`SVC_RECORDS_ARCHIVE_CLIENT_SECRET` |

## Keys of the old shared secret that no service reads

`POSTGRES_PASSWORD`, `RABBITMQ_DEFAULT_PASS`, `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET` and `KEYCLOAK_DIRECTORY_SYNC_SECRET` were in every pod and no code reads them. They are in no Secret now. The last two only repeated the remote references of `Keycloak__ClientSecret` and `Keycloak__DirectorySync__AdminClientSecret`.

## Added by this split

`RabbitMQ__Username` and `RabbitMQ__Password` were nowhere in `deploy/k8s`, so the guard would have refused all 14 modules. Each module now reads its own `mq_*` user from `/hrmanagement/<environment>/rabbitmq/<service>`.

## Secrets that are conditional, and not provisioned

| Key | Service | Needed when |
|-----|---------|-------------|
| `DataProtection__CertificatePassword` | every service | the key ring is protected by a certificate |
| `LegalSignatures__CertificatePassword` | hr-core-web | a signing certificate is configured |
| `Notifications__Candidate__Smtp__Password` | hr-core-web | the SMTP channel is on |
| `Notifications__Candidate__Sms__AuthToken` | hr-core-web | the SMS channel is on |
| `Deployment__LicenseKey` | every service | `Deployment:RequireLicenseKey=true` |

A deployment that turns one of these on adds it to that service's ExternalSecret.

## Single host and acceptance

Compose passes each container its own variables: the same split, written service by service in `docker-compose.production.yml`. `GATEWAY_PROOF_PRIVATE_KEY_B64` reaches `hr-gateway` alone. The acceptance stacks read `ACCEPTANCE_GATEWAY_PROOF_*` and `ACCEPTANCE_SVC_CLIENT_SECRET`, generated under the git-ignored `.acceptance/`.
