# FUN-005 — Receipt Lineage (Track 3 completion)

## Policy

| Rule | Behavior |
|------|----------|
| New assets | Must have `OriginReceiptRecordId` **or** explicit legacy exception |
| Legacy / gift | `IsLegacyEntry=true` + `LegacyEntryReason` (≥ 5 chars), no receipt FK |
| Grandfathered | Existing demo assets without origin remain valid for audit filter only |
| Origin immutability | Cannot clear `OriginReceiptRecordId` after assignment via Edit |

## Supply chain

```text
SupplyOrder (Approved) → Fulfill → ReceiptRecords/CreateFromOrder → inventory + line QtyReceived
→ Receipt Details wizard → Assets/CreateFromReceipt → Edit to complete
```

## UAT matrix

| ID | Scenario | Steps | Expected |
|----|----------|-------|----------|
| R1 | Full chain | Approve order → Fulfill → register receipt → create asset from capitalizable line | Asset shows receipt + supply order links; inventory qty increased |
| R2 | Legacy entry | Assets → Create → «أصل قديم/هبة» → reason ≥ 5 chars | Asset saved without receipt; badge on Details/Index |
| R3 | Reject bare create | Attempt legacy without reason or new asset without receipt | Validation error |
| R4 | Lineage display | Open linked asset Details + Assets Index «مصدر التوريد» | Receipt link or «قديم/هبة»; filter «بدون مصدر» shows grandfathered only |
| R5 | Receipt counter | Receipt Details after creating assets | Shows «N / M أصول مُنشأة» on capitalizable lines |

## Demo data

After re-seed (`DemoLogisticsSeeder`):

- Fulfilled orders include `SupplyOrderLine` rows (~⅓ capitalizable)
- Receipts link to inventory items where fulfilled
- Assets 0–71: `OriginReceiptRecordId` set
- Assets 120–139: `IsLegacyEntry` samples
- Remaining: grandfathered (no origin, not legacy)

## Out of scope (v1)

- Receipt approval workflow
- National ERP integration
- Hard cap on assets per line (soft warning via remaining qty only)
