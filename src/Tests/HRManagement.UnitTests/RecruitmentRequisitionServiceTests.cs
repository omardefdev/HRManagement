using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

public class RecruitmentRequisitionServiceTests
{
    private readonly RecruitmentRequisitionService _service = new(new EstablishmentService());

    [Fact]
    public void DefaultJobTitle_Uses_Catalog_TitleAr()
    {
        var catalog = new JobCatalogEntry { TitleAr = "محلل نظم" };
        Assert.Equal("محلل نظم", RecruitmentRequisitionService.DefaultJobTitle(catalog));
    }

    [Fact]
    public void DefaultJobTitle_Returns_Empty_When_Catalog_Missing()
    {
        Assert.Equal(string.Empty, RecruitmentRequisitionService.DefaultJobTitle(null));
    }

    [Fact]
    public void ValidateCreate_Rejects_Occupied_Position()
    {
        var position = new FundedPosition { Status = FundedPositionStatus.Occupied };
        var error = _service.ValidateCreate(position, hasActiveRequisitionOnPosition: false);
        Assert.NotNull(error);
        Assert.Contains("مشغول", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCreate_Rejects_Second_Active_Requisition()
    {
        var position = new FundedPosition { Status = FundedPositionStatus.Vacant };
        var error = _service.ValidateCreate(position, hasActiveRequisitionOnPosition: true);
        Assert.NotNull(error);
        Assert.Contains("نشط", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCreate_Allows_Vacant_Position_Without_Active_Requisition()
    {
        var position = new FundedPosition { Status = FundedPositionStatus.Vacant };
        Assert.Null(_service.ValidateCreate(position, hasActiveRequisitionOnPosition: false));
    }

    [Fact]
    public void ValidateEdit_Rejects_NonDraft()
    {
        Assert.NotNull(_service.ValidateEdit(RequisitionStatus.Approved));
        Assert.Null(_service.ValidateEdit(RequisitionStatus.Draft));
    }

    [Fact]
    public void MapToPublicJobPosting_Copies_Requisition_And_Position_Fields()
    {
        var org = new OrgUnit { NameAr = "دائرة الموارد" };
        var position = new FundedPosition
        {
            GradeCode = "G5",
            OrgUnit = org,
            JobCatalogEntry = new JobCatalogEntry { DescriptionAr = "خبرة في التحليل" }
        };
        var req = new RecruitmentRequisition
        {
            Id = Guid.NewGuid(),
            JobTitle = "محلل",
            JustificationNotes = "توسعة",
            TenantId = Guid.NewGuid()
        };

        var posting = RecruitmentRequisitionService.MapToPublicJobPosting(req, position);
        Assert.Equal("محلل", posting.Title);
        Assert.Equal(req.Id, posting.RequisitionId);
        Assert.Equal("دائرة الموارد", posting.Entity);
        Assert.True(posting.IsPublished);
    }
}
