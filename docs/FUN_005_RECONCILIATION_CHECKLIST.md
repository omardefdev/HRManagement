# قائمة مصالحة FUN-005 (هوية / أصول / IT)

| # | فحص | أين |
|---|---|---|
| 1 | CardChecklist خطوات Requested→Revoked | IDCard |
| 2 | MarkDelivered: DeliveredTo + إثبات (نص أو ReceiptFileId) | IssuedCardsController |
| 3 | soft-cancel CardRequest (لا hard delete) | CardRequestsController |
| 4 | SeparationPartyAck يبطل Active/Lost؛ SeparationCompleted شبكة أمان | IDCard consumers |
| 5 | UrgentRevoke + RevokeSlaHours (48) | IssuedCard |
| 6 | تعيين أصل: حائز مفتوح واحد + فهرس فريد | AdminLogistics |
| 7 | Lost / IsPrivileged + evidence | AssetsController |
| 8 | Transfer → Status=InStorage | TransferExecutedConsumer |
| 9 | ProvisioningStatus Failed + RetryProvision | SupportTickets |
| 10 | فصل يفصل UserDevice.AssignedTo | IT Separation/Party |
| 11 | Auth Retry حساب = SyncFromKeycloak | AuthUsersController |

## حدود

لا saga جديد؛ لا طابعات فعلية؛ لا مخزون وطني كامل؛ لا إعادة بناء IAM-004.
