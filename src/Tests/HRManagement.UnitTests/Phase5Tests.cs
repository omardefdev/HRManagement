using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Domain.Services;
using Records.Archive.Domain.Entities;
using Records.Archive.Domain.Enums;
using Records.Archive.Domain.Services;
using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Domain.Services;
using HRManagement.MessageContracts.Events;

namespace HRManagement.UnitTests;

public class Phase5Tests
{
    #region CardIssuanceWorkflow

    [Fact]
    public void CanProcess_Pending_ReturnsTrue() =>
        Assert.True(CardIssuanceWorkflow.CanProcess(CardRequestStatus.Pending));

    [Fact]
    public void CanProcess_Processed_ReturnsFalse() =>
        Assert.False(CardIssuanceWorkflow.CanProcess(CardRequestStatus.Processed));

    [Fact]
    public void CanProcess_Rejected_ReturnsFalse() =>
        Assert.False(CardIssuanceWorkflow.CanProcess(CardRequestStatus.Rejected));

    [Fact]
    public void CanRevoke_Active_ReturnsTrue() =>
        Assert.True(CardIssuanceWorkflow.CanRevoke(CardStatus.Active));

    [Fact]
    public void CanRevoke_Revoked_ReturnsFalse() =>
        Assert.False(CardIssuanceWorkflow.CanRevoke(CardStatus.Revoked));

    [Fact]
    public void CanRevoke_Lost_ReturnsTrue() =>
        Assert.True(CardIssuanceWorkflow.CanRevoke(CardStatus.Lost));

    [Fact]
    public void CanRenew_Active_ReturnsTrue() =>
        Assert.True(CardIssuanceWorkflow.CanRenew(CardStatus.Active, null));

    [Fact]
    public void CanRenew_Expired_ReturnsTrue() =>
        Assert.True(CardIssuanceWorkflow.CanRenew(CardStatus.Expired, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10))));

    [Fact]
    public void CanRenew_Revoked_ReturnsFalse() =>
        Assert.False(CardIssuanceWorkflow.CanRenew(CardStatus.Revoked, null));

    [Fact]
    public void IsExpired_FutureDate_ReturnsFalse() =>
        Assert.False(CardIssuanceWorkflow.IsExpired(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))));

    [Fact]
    public void IsExpired_PastDate_ReturnsTrue() =>
        Assert.True(CardIssuanceWorkflow.IsExpired(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));

    [Fact]
    public void IsExpired_Null_ReturnsFalse() =>
        Assert.False(CardIssuanceWorkflow.IsExpired(null));

    #endregion

    #region ArchiveRetentionRules

    [Fact]
    public void IsRetentionExpired_WithinPeriod_ReturnsFalse()
    {
        var item = new ArchiveItem { CreatedAt = DateTime.UtcNow.AddYears(-2), RetentionPeriodYears = 5 };
        Assert.False(ArchiveRetentionRules.IsRetentionExpired(item));
    }

    [Fact]
    public void IsRetentionExpired_AfterPeriod_ReturnsTrue()
    {
        var item = new ArchiveItem { CreatedAt = DateTime.UtcNow.AddYears(-10), RetentionPeriodYears = 5 };
        Assert.True(ArchiveRetentionRules.IsRetentionExpired(item));
    }

    [Fact]
    public void IsRetentionExpired_NullPeriod_ReturnsFalse()
    {
        var item = new ArchiveItem { CreatedAt = DateTime.UtcNow.AddYears(-20), RetentionPeriodYears = null };
        Assert.False(ArchiveRetentionRules.IsRetentionExpired(item));
    }

    [Fact]
    public void CanDispose_ActiveLoans_ReturnsFalse()
    {
        var item = new ArchiveItem { CreatedAt = DateTime.UtcNow.AddYears(-10), RetentionPeriodYears = 5 };
        Assert.False(ArchiveRetentionRules.CanDispose(item, hasActiveLoans: true));
    }

    [Fact]
    public void CanDispose_NoLoansExpired_ReturnsTrue()
    {
        var item = new ArchiveItem
        {
            CreatedAt = DateTime.UtcNow.AddYears(-10),
            RetentionPeriodYears = 5,
            DisposeApprovedAt = DateTime.UtcNow
        };
        Assert.True(ArchiveRetentionRules.CanDispose(item, hasActiveLoans: false));
    }

    [Fact]
    public void CanDispose_Hold_ReturnsFalse()
    {
        var item = new ArchiveItem
        {
            CreatedAt = DateTime.UtcNow.AddYears(-10),
            RetentionPeriodYears = 5,
            IsOnLegalHold = true,
            DisposeApprovedAt = DateTime.UtcNow
        };
        Assert.False(ArchiveRetentionRules.CanDispose(item, hasActiveLoans: false));
    }

    [Fact]
    public void CanDispose_NoLoansNotExpired_ReturnsFalse()
    {
        var item = new ArchiveItem { CreatedAt = DateTime.UtcNow.AddYears(-2), RetentionPeriodYears = 5 };
        Assert.False(ArchiveRetentionRules.CanDispose(item, hasActiveLoans: false));
    }

    [Fact]
    public void IsOverdue_PastDueNotReturned_ReturnsTrue()
    {
        var loan = new LoanRecord { DueDate = DateTime.UtcNow.AddDays(-5), ReturnedDate = null };
        Assert.True(ArchiveRetentionRules.IsOverdue(loan));
    }

    [Fact]
    public void IsOverdue_FutureDue_ReturnsFalse()
    {
        var loan = new LoanRecord { DueDate = DateTime.UtcNow.AddDays(5), ReturnedDate = null };
        Assert.False(ArchiveRetentionRules.IsOverdue(loan));
    }

    [Fact]
    public void IsOverdue_Returned_ReturnsFalse()
    {
        var loan = new LoanRecord { DueDate = DateTime.UtcNow.AddDays(-5), ReturnedDate = DateTime.UtcNow };
        Assert.False(ArchiveRetentionRules.IsOverdue(loan));
    }

    #endregion

    #region SupplyOrderWorkflowRules

    [Fact]
    public void CanApprove_Pending_ReturnsTrue() =>
        Assert.True(SupplyOrderWorkflowRules.CanApprove(SupplyOrderStatus.Pending));

    [Fact]
    public void CanApprove_Approved_ReturnsFalse() =>
        Assert.False(SupplyOrderWorkflowRules.CanApprove(SupplyOrderStatus.Approved));

    [Fact]
    public void CanApprove_order_rejects_requester_as_approver()
    {
        var requesterId = Guid.NewGuid();
        var order = new SupplyOrder { Status = SupplyOrderStatus.Pending, RequestedBy = requesterId };
        Assert.True(SupplyOrderWorkflowRules.CanApprove(order, Guid.NewGuid()));
        Assert.False(SupplyOrderWorkflowRules.CanApprove(order, requesterId));
    }

    [Fact]
    public void CanFulfill_Approved_with_content_ReturnsTrue()
    {
        var order = new SupplyOrder { Status = SupplyOrderStatus.Approved, Items = "أجهزة" };
        Assert.True(SupplyOrderWorkflowRules.CanFulfill(order));
    }

    [Fact]
    public void CanFulfill_Approved_without_content_ReturnsFalse()
    {
        var order = new SupplyOrder { Status = SupplyOrderStatus.Approved };
        Assert.False(SupplyOrderWorkflowRules.CanFulfill(order));
    }

    [Fact]
    public void CanFulfill_Pending_ReturnsFalse()
    {
        var order = new SupplyOrder { Status = SupplyOrderStatus.Pending, Items = "x" };
        Assert.False(SupplyOrderWorkflowRules.CanFulfill(order));
    }

    [Fact]
    public void CanEditContent_Pending_and_Approved_only()
    {
        Assert.True(SupplyOrderWorkflowRules.CanEditContent(SupplyOrderStatus.Pending));
        Assert.True(SupplyOrderWorkflowRules.CanEditContent(SupplyOrderStatus.Approved));
        Assert.False(SupplyOrderWorkflowRules.CanEditContent(SupplyOrderStatus.Fulfilled));
        Assert.False(SupplyOrderWorkflowRules.CanEditContent(SupplyOrderStatus.Closed));
    }

    [Fact]
    public void CanClose_when_fully_received_or_text_order_with_receipt()
    {
        var withLines = new SupplyOrder
        {
            Status = SupplyOrderStatus.Fulfilled,
            Lines = [new SupplyOrderLine { QtyRequested = 2, QtyReceived = 2 }]
        };
        Assert.True(SupplyOrderWorkflowRules.CanClose(withLines, 1));

        var textOnly = new SupplyOrder { Status = SupplyOrderStatus.Fulfilled, Items = "مواد مكتبية" };
        Assert.True(SupplyOrderWorkflowRules.CanClose(textOnly, 1));
        Assert.False(SupplyOrderWorkflowRules.CanClose(textOnly, 0));
    }

    [Fact]
    public void SupplyOrderStatus_includes_Closed() =>
        Assert.True(Enum.IsDefined(typeof(SupplyOrderStatus), SupplyOrderStatus.Closed));

    [Fact]
    public void CanCancel_Pending_ReturnsTrue() =>
        Assert.True(SupplyOrderWorkflowRules.CanCancel(SupplyOrderStatus.Pending));

    [Fact]
    public void CanCancel_Approved_ReturnsTrue() =>
        Assert.True(SupplyOrderWorkflowRules.CanCancel(SupplyOrderStatus.Approved));

    [Fact]
    public void CanCancel_Fulfilled_ReturnsFalse() =>
        Assert.False(SupplyOrderWorkflowRules.CanCancel(SupplyOrderStatus.Fulfilled));

    [Fact]
    public void CanCancel_Cancelled_ReturnsFalse() =>
        Assert.False(SupplyOrderWorkflowRules.CanCancel(SupplyOrderStatus.Cancelled));

    #endregion

    #region IDCard Entity Properties

    [Fact]
    public void IssuedCard_AccessPermissions_InitializesEmpty()
    {
        var card = new IssuedCard();
        Assert.NotNull(card.AccessPermissions);
        Assert.Empty(card.AccessPermissions);
    }

    [Fact]
    public void IssuedCard_DefaultProperties()
    {
        var card = new IssuedCard { EmployeeId = Guid.NewGuid(), Status = CardStatus.Active };
        Assert.Equal(CardStatus.Active, card.Status);
        Assert.Null(card.ExpiresAt);
        Assert.Null(card.DeliveryRecordId);
    }

    [Fact]
    public void CardRequest_DefaultStatus_Pending()
    {
        var req = new CardRequest { Status = CardRequestStatus.Pending };
        Assert.Equal(CardRequestStatus.Pending, req.Status);
    }

    #endregion

    #region Records.Archive Entity Properties

    [Fact]
    public void ArchiveItem_Collections_InitializeEmpty()
    {
        var item = new ArchiveItem();
        Assert.NotNull(item.PhysicalFiles);
        Assert.NotNull(item.DigitalFiles);
        Assert.NotNull(item.LoanRecords);
        Assert.NotNull(item.IndexEntries);
    }

    [Fact]
    public void LoanRecord_DefaultStatus()
    {
        var loan = new LoanRecord { Status = LoanStatus.OnLoan };
        Assert.Equal(LoanStatus.OnLoan, loan.Status);
        Assert.Null(loan.ReturnedDate);
    }

    [Fact]
    public void ArchiveItem_FormatEnum()
    {
        var item = new ArchiveItem { Format = ArchiveFormat.Digital };
        Assert.Equal(ArchiveFormat.Digital, item.Format);
    }

    #endregion

    #region Admin.Logistics Entity Properties

    [Fact]
    public void Asset_Collections_InitializeEmpty()
    {
        var asset = new Asset();
        Assert.NotNull(asset.Assignments);
        Assert.NotNull(asset.MaintenanceRecords);
    }

    [Fact]
    public void SupplyOrder_StatusEnum()
    {
        var order = new SupplyOrder { Status = SupplyOrderStatus.Pending };
        Assert.Equal(SupplyOrderStatus.Pending, order.Status);
        Assert.Null(order.FulfilledAt);
    }

    [Fact]
    public void AssetAssignment_FK_Navigation()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "T-001", Name = "Laptop" };
        var assignment = new AssetAssignment { AssetId = asset.Id, Asset = asset };
        Assert.Equal(asset.Id, assignment.AssetId);
        Assert.Same(asset, assignment.Asset);
    }

    #endregion

    #region Event SchemaVersion

    [Fact]
    public void ICardIssued_SchemaVersion_Is2()
    {
        ICardIssued msg = new CardIssuedStub();
        Assert.Equal(2, msg.SchemaVersion);
    }

    [Fact]
    public void ITransactionArchived_SchemaVersion_Is1()
    {
        ITransactionArchived msg = new TransactionArchivedStub();
        Assert.Equal(1, msg.SchemaVersion);
    }

    private class CardIssuedStub : ICardIssued
    {
        public Guid EmployeeId { get; init; }
        public Guid IssuedCardId { get; init; }
        public string SerialNumber { get; init; } = "TEST";
        public DateTime IssuedAt { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
    }

    private class TransactionArchivedStub : ITransactionArchived
    {
        public Guid TransactionId { get; init; }
        public Guid EmployeeId { get; init; }
        public DateTime ArchivedAt { get; init; } = DateTime.UtcNow;
        public string ArchiveRef { get; init; } = "REF";
        public string SourceSystem { get; init; } = "SYS";
        public string? CorrelationId { get; init; }
    }

    #endregion

    #region FK Navigations Across Services

    [Fact]
    public void IssuedCard_CardDesign_Navigation()
    {
        var design = new CardDesign { Id = Guid.NewGuid(), Name = "Standard" };
        var card = new IssuedCard { CardDesignId = design.Id, CardDesign = design };
        Assert.Same(design, card.CardDesign);
    }

    [Fact]
    public void AccessPermission_IssuedCard_FK()
    {
        var card = new IssuedCard { Id = Guid.NewGuid(), SerialNumber = "S1" };
        var perm = new AccessPermission { IssuedCardId = card.Id, IssuedCard = card, Resource = "Door1" };
        Assert.Equal(card.Id, perm.IssuedCardId);
    }

    [Fact]
    public void DigitalFileRecord_ArchiveItem_FK()
    {
        var item = new ArchiveItem { Id = Guid.NewGuid(), Title = "T", ReferenceNumber = "R" };
        var file = new DigitalFileRecord { ArchiveItemId = item.Id, ArchiveItem = item, FileId = Guid.NewGuid() };
        Assert.Equal(item.Id, file.ArchiveItemId);
    }

    [Fact]
    public void MaintenanceRecord_Asset_FK()
    {
        var asset = new Asset { Id = Guid.NewGuid(), TagNumber = "T-002", Name = "Printer" };
        var rec = new MaintenanceRecord { AssetId = asset.Id, Asset = asset };
        Assert.Equal(asset.Id, rec.AssetId);
        Assert.Same(asset, rec.Asset);
    }

    [Fact]
    public void ReceiptRecord_SupplyOrder_FK()
    {
        var order = new SupplyOrder { Id = Guid.NewGuid(), Status = SupplyOrderStatus.Fulfilled };
        var receipt = new ReceiptRecord { SupplyOrderId = order.Id, SupplyOrder = order };
        Assert.Equal(order.Id, receipt.SupplyOrderId);
    }

    #endregion
}
