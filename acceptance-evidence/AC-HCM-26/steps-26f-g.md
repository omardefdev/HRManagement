1. Create, approve, define criteria and publish a requisition; register one HR application.
2. AC-26f (negative, UI): a second requisition on the same funded position is rejected.
3. AC-26f (negative, DB): a direct duplicate insert hits IX_RecruitmentRequisitions_Tenant_Position_Blocking.
4. AC-26g (positive): central-a cancels the published requisition with a reason → posting unpublished, application rejected, audit row.