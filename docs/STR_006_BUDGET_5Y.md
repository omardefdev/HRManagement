# STR-006 — Five-year budget stub

Amounts are **illustrative units** (not currency quotes). Full TCO workbook = **FIN-001**.

| category | Y1–Y5 amounts | funded | owner |
|---|---|---|---|
| dev | 100,90,80,70,60 | Yes | product |
| ops | 40,42,44,46,48 | Yes | finance |
| security | 25,26,27,28,29 | Yes | security |
| backup | 15,15,16,16,17 | Yes | platform |
| dr | 20,20,21,21,22 | Yes | platform |
| support | 30,30,32,32,34 | Yes | procurement |
| training | 8,8,9,9,10 | Yes | hr |

**Rule:** `mustFundCategories` = ops, security, backup. Dev-only budget fails validate (`forbidDevOnly`).

See [STR_006_TCO_APPROVAL.md](STR_006_TCO_APPROVAL.md).
