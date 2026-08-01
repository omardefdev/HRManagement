# REL-003 — On-call roster

Extends pilot L1–L3 in [REL_002_SUPPORT_CONTACTS.md](REL_002_SUPPORT_CONTACTS.md). Closes OBS escalation to SRE on-call. Not a national 24x7 desk (SUP-002 / SRE-001).

## Primary roster

| Tier | Primary | Backup | Hours | Page channel |
|---|---|---|---|---|
| L1 intake | hr-l1-primary@platform.sy | hr-l1-backup@platform.sy | Business (Sun–Thu 08:00–16:00) | Teams `#hr-l1-intake` |
| platform | sre-platform-primary@platform.sy | sre-platform-backup@platform.sy | On-call rotation (weekly) | PagerDuty `hr-platform` |
| payroll | payroll-oncall-primary@platform.sy | payroll-oncall-backup@platform.sy | On-call + business hours overlap | PagerDuty `hr-payroll` |
| sre | sre-primary@platform.sy | sre-backup@platform.sy | On-call rotation (weekly) | PagerDuty `hr-sre` |

**Rotation schedule:** weekly handover Sunday 08:00 Asia/Damascus; calendar in internal ops wiki (`/ops/oncall/hr`).  
**Escalation contacts:** see [REL_002_SUPPORT_CONTACTS.md](REL_002_SUPPORT_CONTACTS.md) for L2/L3 named leads.

<a id="platform"></a>

### platform

Escalation: L1 → platform primary → sre. Used by most app services in the catalog.

<a id="payroll"></a>

### payroll

Escalation: L1 → payroll primary → sre. Used by `payroll` service / J4 alerts.

<a id="sre"></a>

### sre

Escalation: platform/payroll → sre → incident commander (Sev1). Site-loss: OPS DR IC tree.

## Response test

Acknowledge within MTTA for the scenario severity. Measured by `scripts/rel003-incident-game-day.ps1` (synthetic timestamps allowed for CI; live page optional).

## Handover

Outgoing on-call posts open Sev1/Sev2 and known silences before rotation end. Template: `#hr-oncall-handover` channel, include PagerDuty shift ID and open incident links.
