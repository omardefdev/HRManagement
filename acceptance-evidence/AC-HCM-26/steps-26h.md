1. Publish two requisitions (positions P1, P2), each with one HR application.
2. Test-only precondition: insert an Issued offer on P2's application.
3. AC-26h (negative): freeze P2 via FundedPositions/Freeze → submit → central-a2 + a-management-approver approve → apply fails; P2 stays Vacant, requisition stays Published.
4. AC-26h (positive): freeze P1 the same way → P1 Frozen, requisition Cancelled («تجميد المنصب: …»), application Rejected.