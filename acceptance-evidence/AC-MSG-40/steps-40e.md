1. IStartSeparationSaga for an employee: the saga dispatches the parties.
2. admin_db and auth_directory_db: the OutboxMessage sequence advanced, so the acknowledgement was written in the consumer's transaction.
The prod-like hire journey (Category=JourneyProdLike) and AcHcm29 run next to this scenario; a failed commit leaving no acknowledgement is proved in Msg040OutboxOrderingIntegrationTests.