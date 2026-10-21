using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Entities.Seeding;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Seeding;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Localization;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Seeding;

/// <summary>Canonical multi-agency Acceptance v2 seed for HR.Core.</summary>
public static class AcceptanceDataSeeder
{
    private const int JobCatalogSize = 40;

    public static async Task SeedAsync(
        HRCoreDbContext db,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        await ValidateExistingPackAsync(db, ct);
        await AcceptanceSeedReceiptStore.EnsureTableAsync(db, ct);
        await LegislationRegisterSeeder.SeedAsync(db, ct);

        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational())
            transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            foreach (var agency in AcceptanceSeedCatalog.Agencies)
            {
                if (!await AcceptanceSeedReceiptStore.ShouldApplyAsync(db, agency.Id, nameof(HRCoreDbContext), ct))
                    continue;
                await SeedAgencyAsync(db, agency, logger, ct);
                await AcceptanceSeedReceiptStore.WriteAppliedAsync(db, agency, nameof(HRCoreDbContext), ct);
            }

            await SeedAssignmentAndQuarantineFixturesAsync(db, ct);

            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
            TenantIdFlow.Set(null);
        }
    }

    private static async Task ValidateExistingPackAsync(HRCoreDbContext db, CancellationToken ct)
    {
        var receipts = await db.SeedPackApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .Where(x => x.PackId == AcceptanceSeedCatalog.PackId)
            .ToListAsync(ct);

        if (receipts.Count == AcceptanceSeedCatalog.Agencies.Count
            && receipts.All(r => r.ReleaseDigest == AcceptanceSeedPackFingerprint.Value))
            return;

        var tenantIds = AcceptanceSeedCatalog.Agencies.Select(a => a.Id).ToArray();
        var existingAcceptanceEmployees = await db.Employees
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .CountAsync(e => tenantIds.Contains(e.TenantId), ct);

        if (existingAcceptanceEmployees > 0 || receipts.Count > 0)
        {
            throw new InvalidOperationException(
                "Acceptance seed data belongs to an older or incomplete pack. " +
                "Run scripts/acceptance-reset.ps1 before applying Acceptance v2.");
        }
    }

    private static async Task SeedAgencyAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        ILogger? logger,
        CancellationToken ct)
    {
        var alreadyApplied = await db.SeedPackApplications
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .AnyAsync(x => x.TenantId == agency.Id
                && x.PackId == AcceptanceSeedCatalog.PackId
                && x.ReleaseDigest == AcceptanceSeedPackFingerprint.Value, ct);
        if (alreadyApplied)
            return;

        logger?.LogInformation(
            "Seeding {Agency} with {EmployeeCount} employees and {OrgUnitCount} org units.",
            agency.Code,
            agency.EmployeeCount,
            agency.OrgUnitCount);

        using var frame = AcceptanceSeedExecution.Push(agency);
        await EnsureTenantAsync(db, agency, ct);
        await MdmReferenceSeeder.CopySubsetAsync(
            db,
            TenantDefaults.DefaultTenantId,
            agency.Id,
            [nameof(CatalogKind.Grade), nameof(CatalogKind.JobClassification)],
            ct);
        await SeedOrgUnitsAsync(db, agency, ct);
        var jobCatalog = await SeedJobCatalogAsync(db, agency, ct);
        await SeedEmployeesAsync(db, agency, jobCatalog, ct);
        await AssignOrgUnitHeadsAsync(db, agency, ct);
        await SeedFundedPositionsAsync(db, agency, jobCatalog, ct);
        await SeedRecruitmentRequisitionsAsync(db, agency, ct);
        await SeedInternalChannelAsync(db, agency, ct);
        await SeedRecruitmentApplicationsAsync(db, agency, jobCatalog, ct);
        await SeedSelectionPipelineAsync(db, agency, ct);
        await SeedAcceptancePolicyAsync(db, agency, ct);

        db.SeedPackApplications.Add(new SeedPackApplication
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "seed-pack-receipt"),
            TenantId = agency.Id,
            PackId = AcceptanceSeedCatalog.PackId,
            ReleaseDigest = AcceptanceSeedPackFingerprint.Value,
            RollbackRef = "scripts/acceptance-reset.ps1",
            AppliedAt = AcceptanceSeedCatalog.ReferenceDate,
            CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
            UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static async Task EnsureTenantAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        if (await db.Tenants
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .AnyAsync(t => t.Id == agency.Id, ct))
            return;

        db.Tenants.Add(new Tenant
        {
            Id = agency.Id,
            Code = agency.Code,
            NameAr = agency.NameAr,
            NameEn = agency.NameEn,
            IsActive = true,
            CreatedAt = AcceptanceSeedCatalog.ReferenceDate
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedOrgUnitsAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        foreach (var source in agency.OrgUnits.OrderBy(o => o.SortOrder))
        {
            db.OrgUnits.Add(new OrgUnit
            {
                Id = source.Id,
                TenantId = source.TenantId,
                Code = source.Code,
                NameAr = source.NameAr,
                NameEn = source.NameEn,
                ParentId = source.ParentId,
                MaterializedPath = source.MaterializedPath,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddYears(-4),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        await db.SaveChangesAsync(ct);

        foreach (var source in agency.OrgUnits)
        {
            db.OrgUnitVersions.Add(new OrgUnitVersion
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"org-version-current-{source.Id:N}"),
                TenantId = agency.Id,
                OrgUnitId = source.Id,
                ValidFrom = AcceptanceSeedCatalog.ReferenceDate.AddYears(-4),
                Name = source.NameAr,
                NameEn = source.NameEn,
                ParentId = source.ParentId,
                OperationType = OrgOperationType.Create,
                ApprovedBy = "لجنة الهيكل التنظيمي - بيانات قبول",
                DecisionReference = $"ACC-ORG-{source.SortOrder + 1:D3}",
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddYears(-4),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<IReadOnlyList<JobCatalogEntry>> SeedJobCatalogAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var jobs = new List<JobCatalogEntry>(JobCatalogSize);
        for (var i = 0; i < JobCatalogSize; i++)
        {
            var title = DemoNameBank.JobTitle(i);
            jobs.Add(new JobCatalogEntry
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"job-{i:D2}"),
                TenantId = agency.Id,
                JobCode = $"JOB-{i + 1:D3}",
                TitleAr = title,
                TitleEn = $"Acceptance job {i + 1:D2}",
                DescriptionAr = $"وصف وظيفي اصطناعي معتمد لأغراض اختبار وظيفة {title}.",
                GradeCode = $"G{1 + i % 10}",
                OccupationalGroup = (i % 4) switch
                {
                    0 => "إداري",
                    1 => "مالي",
                    2 => "تقني",
                    _ => "خدمات مساندة"
                },
                IsActive = i % 17 != 0,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddYears(-3),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        db.JobCatalogEntries.AddRange(jobs);
        await db.SaveChangesAsync(ct);
        return jobs;
    }

    private static async Task SeedEmployeesAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        IReadOnlyList<JobCatalogEntry> jobs,
        CancellationToken ct)
    {
        const int batchSize = 250;
        for (var offset = 0; offset < agency.Employees.Count; offset += batchSize)
        {
            foreach (var source in agency.Employees.Skip(offset).Take(batchSize))
            {
                var contactId = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"contact-{source.Index:D5}");
                var employmentRecordId = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"employment-{source.Index:D5}");
                var status = MapEmploymentStatus(source.State);
                var job = jobs[source.Index % jobs.Count];

                db.Employees.Add(new Employee
                {
                    Id = source.Id,
                    TenantId = agency.Id,
                    NationalId = source.NationalId,
                    NationalIdHash = PiiMask.Sha256Hex(source.NationalId),
                    FirstName = source.FirstName,
                    MiddleName = source.MiddleName,
                    LastName = source.LastName,
                    FullName = source.FullName,
                    FirstNameNormalized = ArabicTextNormalizer.Normalize(source.FirstName),
                    MiddleNameNormalized = source.MiddleName is null ? null : ArabicTextNormalizer.Normalize(source.MiddleName),
                    LastNameNormalized = ArabicTextNormalizer.Normalize(source.LastName),
                    FullNameNormalized = ArabicTextNormalizer.Normalize(source.FullName),
                    NormalizationVersion = 1,
                    BirthDate = source.BirthDate,
                    Gender = source.IsFemale ? Gender.Female : Gender.Male,
                    MaritalStatus = source.Index % 5 == 0 ? MaritalStatus.Single : MaritalStatus.Married,
                    PrimaryContactId = contactId,
                    CurrentStatus = status,
                    PrimaryOrgUnitId = source.OrgUnitId,
                    HireDate = source.HireDate,
                    Cadre = source.IsSupervisor ? EmployeeCadre.Supervisory : EmployeeCadre.Executive,
                    CreatedAt = source.HireDate,
                    UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                });

                db.EmployeeProfiles.Add(new EmployeeProfile
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"profile-{source.Index:D5}"),
                    TenantId = agency.Id,
                    EmployeeId = source.Id,
                    Nationality = "سوري",
                    PlaceOfBirth = DemoNameBank.Governorate(source.Index + agency.Ordinal),
                    NationalIdIssuePlace = DemoNameBank.Governorate(source.Index + agency.Ordinal * 3),
                    ResidentialAddress = source.Address,
                    PermanentAddress = source.Address,
                    EmergencyContact = $"جهة اتصال اصطناعية - {source.Phone}",
                    Notes = "بيانات قبول اصطناعية - غير مخصصة للإنتاج",
                    CreatedAt = source.HireDate,
                    UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                });

                db.ContactInfos.Add(new ContactInfo
                {
                    Id = contactId,
                    TenantId = agency.Id,
                    EmployeeId = source.Id,
                    Type = ContactType.Email,
                    Value = source.Email,
                    IsPrimary = true,
                    CreatedAt = source.HireDate,
                    UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                });
                db.ContactInfos.Add(new ContactInfo
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"phone-{source.Index:D5}"),
                    TenantId = agency.Id,
                    EmployeeId = source.Id,
                    Type = ContactType.Phone,
                    Value = source.Phone,
                    IsPrimary = false,
                    CreatedAt = source.HireDate,
                    UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                });

                db.EmploymentRecords.Add(new EmploymentRecord
                {
                    Id = employmentRecordId,
                    TenantId = agency.Id,
                    EmployeeId = source.Id,
                    PositionTitle = source.PositionTitle,
                    PositionCode = job.JobCode,
                    OrgUnitId = source.OrgUnitId,
                    StartDate = source.HireDate,
                    EndDate = source.HasCurrentContract ? null : AcceptanceSeedCatalog.ReferenceDate.AddDays(-(30 + source.Index % 300)),
                    EmploymentType = source.Index % 11 == 0 ? EmploymentType.Temporary : EmploymentType.Permanent,
                    Status = source.State.ToString().ToLowerInvariant(),
                    ApprovedBy = agency.EmployeeAt(0).Id,
                    Notes = source.State == AcceptanceEmployeeState.Probation
                        ? "موظف ضمن فترة التجربة"
                        : "سجل وظيفي اصطناعي لحزمة القبول",
                    JobClassificationCode = job.JobCode,
                    CreatedAt = source.HireDate,
                    UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                });

                db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"status-{source.Index:D5}"),
                    TenantId = agency.Id,
                    EmployeeId = source.Id,
                    OldStatus = null,
                    NewStatus = status.ToString(),
                    ChangedBy = agency.EmployeeAt(0).Id,
                    ChangedAt = source.State is AcceptanceEmployeeState.Terminated or AcceptanceEmployeeState.Retired
                        ? AcceptanceSeedCatalog.ReferenceDate.AddDays(-(30 + source.Index % 300))
                        : source.HireDate,
                    Reason = $"حالة قبول: {source.State}",
                    EmploymentRecordId = employmentRecordId,
                    CreatedAt = source.HireDate,
                    UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                });

                if (source.Index % 3 == 0)
                {
                    db.Qualifications.Add(new Qualification
                    {
                        Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"qualification-{source.Index:D5}"),
                        TenantId = agency.Id,
                        EmployeeId = source.Id,
                        QualificationCode = $"Q-{1 + source.Index % 8:D2}",
                        Title = (source.Index % 4) switch
                        {
                            0 => "إجازة في الاقتصاد",
                            1 => "إجازة في الحقوق",
                            2 => "هندسة معلوماتية",
                            _ => "معهد متوسط إداري"
                        },
                        Provider = "جهة تعليمية سورية - بيانات اصطناعية",
                        CompletionDate = source.HireDate.AddYears(-1),
                        CreatedAt = source.HireDate,
                        UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                    });
                }
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private static async Task AssignOrgUnitHeadsAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var units = await db.OrgUnits
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
            .Where(o => o.TenantId == agency.Id)
            .ToListAsync(ct);

        foreach (var unit in units)
        {
            var source = agency.OrgUnits.First(o => o.Id == unit.Id);
            var head = agency.Employees.FirstOrDefault(e => e.SiteCode == source.SiteCode && e.IsSupervisor)
                ?? agency.Employees.First(e => e.SiteCode == source.SiteCode);
            unit.HeadEmployeeId = head.Id;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRecruitmentApplicationsAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        IReadOnlyList<JobCatalogEntry> jobs,
        CancellationToken ct)
    {
        string[] firstNames = ["نور", "كرم", "رنا", "يامن", "لمى", "سامر"];
        string[] lastNames = ["الحسن", "اليوسف", "خليل", "العباس", "إبراهيم"];
        var statuses = Enum.GetValues<RecruitmentApplicationStatus>();
        var requisitions = await db.RecruitmentRequisitions
            .Where(r => r.TenantId == agency.Id)
            .OrderBy(r => r.RequisitionNumber)
            .ToListAsync(ct);

        for (var i = 0; i < agency.Volume.RecruitmentApplications; i++)
        {
            var status = statuses[i % statuses.Length];
            var requisition = requisitions.Count > 0 ? requisitions[i % requisitions.Count] : null;
            db.RecruitmentApplications.Add(new RecruitmentApplication
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"recruitment-{i:D3}"),
                TenantId = agency.Id,
                ApplicantFullName = $"{firstNames[(i + agency.Ordinal) % firstNames.Length]} {lastNames[(i * 2 + agency.Ordinal) % lastNames.Length]}",
                NationalId = $"AC-{agency.Ordinal}-{i + 1:D6}",
                NationalIdHash = PiiMask.Sha256Hex($"AC-{agency.Ordinal}-{i + 1:D6}"),
                RequisitionId = requisition?.Id,
                AppliedPosition = requisition?.JobTitle ?? jobs[(i * 3 + agency.Ordinal) % jobs.Count].TitleAr,
                AppliedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-(60 + i * 3 + agency.Ordinal)),
                Status = status,
                Notes = "طلب توظيف اصطناعي ضمن حزمة Acceptance v2",
                HiredEmployeeId = status == RecruitmentApplicationStatus.Hired
                    ? agency.EmployeeAt(i).Id
                    : null,
                ReviewedAt = status == RecruitmentApplicationStatus.Submitted
                    ? null
                    : AcceptanceSeedCatalog.ReferenceDate.AddDays(-(30 + i)),
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-(60 + i * 3 + agency.Ordinal)),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static async Task SeedFundedPositionsAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        IReadOnlyList<JobCatalogEntry> jobs,
        CancellationToken ct)
    {
        var parentUnitIds = agency.OrgUnits
            .Where(unit => unit.ParentId.HasValue)
            .Select(unit => unit.ParentId!.Value)
            .ToHashSet();
        var leafUnits = agency.OrgUnits.Where(unit => !parentUnitIds.Contains(unit.Id)).ToArray();

        foreach (var source in agency.Employees)
        {
            var job = jobs[source.Index % jobs.Count];
            db.FundedPositions.Add(new FundedPosition
            {
                Id = source.Id == AcceptanceSeedIds.EmpBr1Id
                    ? AcceptanceSeedIds.Br1FundedPositionId
                    : AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"position-{source.Index:D5}"),
                TenantId = agency.Id,
                PositionCode = $"POS-{source.Index + 1:D5}",
                GradeCode = job.GradeCode,
                FTE = 1m,
                BudgetYear = AcceptanceSeedCatalog.ReferenceDate.Year,
                Status = source.HasCurrentContract ? FundedPositionStatus.Occupied : FundedPositionStatus.Vacant,
                OrgUnitId = source.OrgUnitId,
                JobCatalogEntryId = job.Id,
                OccupantEmployeeId = source.HasCurrentContract ? source.Id : null,
                CostCenter = $"CC-{source.SiteCode}-{1 + source.Index % 20:D2}",
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddYears(-1),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        var vacancyCount = Math.Max(3, agency.EmployeeCount / 20);
        for (var i = 0; i < vacancyCount; i++)
        {
            var job = jobs[(i + 7) % jobs.Count];
            db.FundedPositions.Add(new FundedPosition
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"vacancy-{i:D4}"),
                TenantId = agency.Id,
                PositionCode = $"VAC-{i + 1:D4}",
                GradeCode = job.GradeCode,
                FTE = i % 9 == 0 ? 0.5m : 1m,
                BudgetYear = AcceptanceSeedCatalog.ReferenceDate.Year,
                Status = i % 11 == 0 ? FundedPositionStatus.Frozen : FundedPositionStatus.Vacant,
                OrgUnitId = leafUnits[i % leafUnits.Length].Id,
                JobCatalogEntryId = job.Id,
                CostCenter = $"CC-VAC-{i % 12:D2}",
                FreezeReason = i % 11 == 0 ? "تجميد اختباري ضمن خطة القوى العاملة" : null,
                FreezeDate = i % 11 == 0 ? AcceptanceSeedCatalog.ReferenceDate.AddMonths(-2) : null,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddYears(-1),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedRecruitmentRequisitionsAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        if (await db.RecruitmentRequisitions.AnyAsync(r => r.TenantId == agency.Id, ct))
            return;

        var vacant = await db.FundedPositions
            .Include(p => p.JobCatalogEntry)
            .Where(p => p.TenantId == agency.Id
                && p.Status == FundedPositionStatus.Vacant
                && p.PositionCode.StartsWith("VAC-"))
            .OrderBy(p => p.PositionCode)
            .Take(2)
            .ToListAsync(ct);

        for (var i = 0; i < vacant.Count; i++)
        {
            var position = vacant[i];
            var isPrimary = agency.Code == AcceptanceSeedIds.AgencyACode && i == 0;
            var requisition = new RecruitmentRequisition
            {
                Id = isPrimary
                    ? AcceptanceSeedIds.Br1RecruitmentRequisitionId
                    : AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"requisition-{i:D4}"),
                TenantId = agency.Id,
                RequisitionNumber = $"REQ-{AcceptanceSeedCatalog.ReferenceDate.Year}-{agency.Code[^1]}{(i + 1):D4}",
                FundedPositionId = position.Id,
                JobTitle = position.JobCatalogEntry?.TitleAr ?? position.GradeCode,
                Status = isPrimary ? RequisitionStatus.Published : RequisitionStatus.Draft,
                RequestedDate = AcceptanceSeedCatalog.ReferenceDate.AddDays(-(i + 3)),
                ApprovedDate = isPrimary ? AcceptanceSeedCatalog.ReferenceDate.AddDays(-2) : null,
                PublishedAt = isPrimary ? AcceptanceSeedCatalog.ReferenceDate.AddDays(-1) : null,
                JustificationNotes = "طلب شاغر ثابت لاختبارات القبول.",
                VacancyCount = 1,
                LegalSourceId = isPrimary ? HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004 : null,
                PublishingChannel = isPrimary ? RecruitmentPublishingChannel.Both : RecruitmentPublishingChannel.PublicOnly,
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddMonths(-1),
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            };
            db.RecruitmentRequisitions.Add(requisition);

            if (isPrimary)
            {
                var posting = RecruitmentRequisitionService.MapToPublicJobPosting(requisition, position);
                posting.Id = AcceptanceSeedIds.Br1PublishedJobPostingId;
                db.PublicJobPostings.Add(posting);
                requisition.PublicJobPosting = posting;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static async Task SeedInternalChannelAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        if (agency.Code != AcceptanceSeedIds.AgencyACode)
            return;

        var requisition = await db.RecruitmentRequisitions
            .Include(r => r.FundedPosition)
            .FirstOrDefaultAsync(r => r.Id == AcceptanceSeedIds.Br1RecruitmentRequisitionId, ct);
        if (requisition is null || requisition.InternalVacancyId.HasValue)
            return;

        var position = requisition.FundedPosition
            ?? await db.FundedPositions.FirstAsync(p => p.Id == requisition.FundedPositionId, ct);

        var vacancy = RecruitmentRequisitionService.MapToInternalVacancy(requisition, position);
        vacancy.Id = AcceptanceSeedIds.Br1InternalVacancyId;
        db.InternalVacancies.Add(vacancy);
        requisition.InternalVacancy = vacancy;
        requisition.InternalVacancyId = vacancy.Id;

        db.InternalApplications.Add(new InternalApplication
        {
            Id = AcceptanceSeedIds.Br1InternalApplicationId,
            TenantId = agency.Id,
            InternalVacancyId = vacancy.Id,
            EmployeeId = AcceptanceSeedIds.EmpD1Id,
            RequisitionId = requisition.Id,
            AppliedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-5),
            Status = InternalApplicationStatus.UnderReview,
            Motivation = "ترشّح داخلي لاختبارات القبول",
            CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-5),
            UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
        });

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static async Task SeedSelectionPipelineAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var publishedReqs = await db.RecruitmentRequisitions
            .Where(r => r.TenantId == agency.Id && r.Status == RequisitionStatus.Published)
            .ToListAsync(ct);
        if (publishedReqs.Count == 0)
            return;

        foreach (var req in publishedReqs)
        {
            if (await db.RecruitmentAssessmentCriteria.AnyAsync(c => c.RequisitionId == req.Id, ct))
                continue;

            db.RecruitmentAssessmentCriteria.Add(new RecruitmentAssessmentCriteria
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"criteria-{req.Id:N}"),
                TenantId = agency.Id,
                RequisitionId = req.Id,
                VersionNumber = 1,
                Title = "معايير التقييم v1",
                CriteriaJson = """[{"name":"المقابلة","weight":40},{"name":"الخبرة","weight":60}]""",
                EffectiveFrom = AcceptanceSeedCatalog.ReferenceDate.AddMonths(-1),
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            });
        }

        await db.SaveChangesAsync(ct);

        var apps = await db.RecruitmentApplications
            .Where(a => a.TenantId == agency.Id && a.RequisitionId != null)
            .OrderBy(a => a.AppliedAt)
            .Take(6)
            .ToListAsync(ct);

        if (apps.Count == 0)
            return;

        var criteria = await db.RecruitmentAssessmentCriteria
            .Where(c => c.TenantId == agency.Id)
            .ToDictionaryAsync(c => c.RequisitionId, ct);

        for (var i = 0; i < apps.Count; i++)
        {
            var app = apps[i];
            app.ConflictOfInterestDeclared = true;
            app.ConflictOfInterestDeclaredAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-10);
            criteria.TryGetValue(app.RequisitionId!.Value, out var crit);

            switch (i % 6)
            {
                case 0:
                    app.Status = RecruitmentApplicationStatus.UnderReview;
                    app.ScreenedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-8);
                    break;
                case 1:
                    app.Status = RecruitmentApplicationStatus.Shortlisted;
                    app.ReviewedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-7);
                    break;
                case 2:
                    app.Status = RecruitmentApplicationStatus.InterviewScheduled;
                    app.ReviewedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-6);
                    break;
                case 3:
                    app.Status = RecruitmentApplicationStatus.Assessed;
                    app.AssessmentScore = 78m + i;
                    app.CriteriaVersionId = crit?.Id;
                    app.ReviewedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-5);
                    break;
                case 4:
                    app.Status = RecruitmentApplicationStatus.Ranked;
                    app.AssessmentScore = 85m + i;
                    app.CriteriaVersionId = crit?.Id;
                    app.Rank = 1 + i / 6;
                    app.RankedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-4);
                    break;
                default:
                    app.Status = RecruitmentApplicationStatus.OfferAccepted;
                    app.AssessmentScore = 92m;
                    app.CriteriaVersionId = crit?.Id;
                    app.Rank = 1;
                    app.RankedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-3);
                    app.ReviewedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-2);
                    {
                        var positionId = await db.RecruitmentRequisitions
                            .Where(r => r.Id == app.RequisitionId)
                            .Select(r => r.FundedPositionId)
                            .FirstAsync(ct);
                        var offer = new OfferLetter
                        {
                            Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, $"offer-{app.Id:N}"),
                            TenantId = agency.Id,
                            RecruitmentApplicationId = app.Id,
                            FundedPositionId = positionId,
                            OfferNumber = $"OFF-{AcceptanceSeedCatalog.ReferenceDate.Year}-{agency.Code[^1]}{i + 1:D3}",
                            Status = OfferStatus.Accepted,
                            IssuedDate = AcceptanceSeedCatalog.ReferenceDate.AddDays(-2),
                            AcceptedDate = AcceptanceSeedCatalog.ReferenceDate.AddDays(-1),
                            ExpiryDate = AcceptanceSeedCatalog.ReferenceDate.AddDays(30),
                            OfferedSalary = 250000m,
                            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004,
                            ProposedStartDate = AcceptanceSeedCatalog.ReferenceDate.AddDays(14),
                            CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
                            UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
                        };
                        db.OfferLetters.Add(offer);
                        app.ActiveOfferLetter = offer;
                    }
                    break;
            }
        }

        var issuedApp = apps.FirstOrDefault(a => a.Status == RecruitmentApplicationStatus.Ranked);
        if (issuedApp?.RequisitionId is Guid reqId)
        {
            var positionId = await db.RecruitmentRequisitions
                .Where(r => r.Id == reqId)
                .Select(r => r.FundedPositionId)
                .FirstAsync(ct);
            var issuedOffer = new OfferLetter
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "offer-issued"),
                TenantId = agency.Id,
                RecruitmentApplicationId = issuedApp.Id,
                FundedPositionId = positionId,
                OfferNumber = $"OFF-{AcceptanceSeedCatalog.ReferenceDate.Year}-{agency.Code[^1]}-ISS",
                Status = OfferStatus.Issued,
                IssuedDate = AcceptanceSeedCatalog.ReferenceDate,
                ExpiryDate = AcceptanceSeedCatalog.ReferenceDate.AddDays(21),
                OfferedSalary = 220000m,
                LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004,
                ProposedStartDate = AcceptanceSeedCatalog.ReferenceDate.AddDays(21),
                CreatedAt = AcceptanceSeedCatalog.ReferenceDate,
                UpdatedAt = AcceptanceSeedCatalog.ReferenceDate
            };
            db.OfferLetters.Add(issuedOffer);
            issuedApp.ActiveOfferLetter = issuedOffer;
            issuedApp.Status = RecruitmentApplicationStatus.OfferExtended;
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static async Task SeedAssignmentAndQuarantineFixturesAsync(
        HRCoreDbContext db,
        CancellationToken ct)
    {
        if (!await db.TenantAssignmentRules.AnyAsync(r => r.TargetTable == "Employees" && r.DiscoveryMethod == "AcceptanceV2", ct))
        {
            foreach (var agency in AcceptanceSeedCatalog.Agencies)
            {
                db.TenantAssignmentRules.Add(new TenantAssignmentRule
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "tenant-assignment-rule"),
                    TargetTable = "Employees",
                    DiscoveryMethod = "AcceptanceV2",
                    SqlCondition = $"employee_number LIKE '{agency.Code[^1]}-%'",
                    TargetTenantId = agency.Id,
                    CreatedAt = AcceptanceSeedCatalog.ReferenceDate
                });
            }
        }

        if (!await db.TenantQuarantineEntries.AnyAsync(q => q.ServiceName == "AcceptanceV2", ct))
        {
            db.TenantQuarantineEntries.AddRange(
                new TenantQuarantineEntry
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid("PLATFORM", "quarantine-open"),
                    ServiceName = "AcceptanceV2",
                    TableName = "LegacyEmployees",
                    RecordId = AcceptanceSeedCatalog.DeterministicGuid("PLATFORM", "ambiguous-record"),
                    Reason = "سجل اصطناعي متعدد المطابقات بين جهتين",
                    CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-3)
                },
                new TenantQuarantineEntry
                {
                    Id = AcceptanceSeedCatalog.DeterministicGuid("PLATFORM", "quarantine-resolved"),
                    ServiceName = "AcceptanceV2",
                    TableName = "LegacyEmployees",
                    RecordId = AcceptanceSeedCatalog.DeterministicGuid("PLATFORM", "resolved-record"),
                    Reason = "غياب رمز الجهة في السجل المصدر",
                    Resolution = "تمت المطابقة يدوياً مع سجل الوحدة التنظيمية",
                    AssignedTenantId = AcceptanceSeedCatalog.AgencyAId,
                    ResolvedByUserId = AcceptanceSeedCatalog.LegacyEmployeeBr1Id,
                    CreatedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-5),
                    ResolvedAt = AcceptanceSeedCatalog.ReferenceDate.AddDays(-2)
                });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAcceptancePolicyAsync(
        HRCoreDbContext db,
        AcceptanceAgency agency,
        CancellationToken ct)
    {
        var templateId = agency.Ordinal == 1
            ? AcceptanceSeedIds.PolicyTemplateId
            : AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "policy-template-assets");
        if (await db.PolicyTemplates
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.SeedInitialization)
                .AnyAsync(t => t.Id == templateId, ct))
            return;

        var v1Id = agency.Ordinal == 1
            ? AcceptanceSeedIds.PolicyVersion1Id
            : AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "policy-assets-v1");
        var v2Id = agency.Ordinal == 1
            ? AcceptanceSeedIds.PolicyVersion2Id
            : AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "policy-assets-v2");
        var v3Id = agency.Ordinal == 1
            ? AcceptanceSeedIds.PolicyVersion3DraftId
            : AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "policy-assets-v3");

        db.PolicyTemplates.Add(new PolicyTemplate
        {
            Id = templateId,
            TenantId = agency.Id,
            Domain = PolicyDomains.AssetsCustody,
            Name = $"سياسة عهدة - {agency.Code}",
            SubjectType = "Asset",
            Scope = PolicyTemplateScope.TenantOverride,
            Description = "قالب قبول بإصدارات مؤرشفة ومنشورة ومسودة"
        });
        db.PolicyTemplateVersions.AddRange(
            new PolicyTemplateVersion
            {
                Id = v1Id,
                TenantId = agency.Id,
                PolicyTemplateId = templateId,
                VersionNumber = 1,
                Status = PolicyVersionStatus.Archived,
                EffectiveFrom = AcceptanceSeedCatalog.ReferenceDate.AddYears(-2),
                EffectiveTo = AcceptanceSeedCatalog.ReferenceDate.AddYears(-1)
            },
            new PolicyTemplateVersion
            {
                Id = v2Id,
                TenantId = agency.Id,
                PolicyTemplateId = templateId,
                VersionNumber = 2,
                Status = PolicyVersionStatus.Published,
                EffectiveFrom = AcceptanceSeedCatalog.ReferenceDate.AddYears(-1)
            },
            new PolicyTemplateVersion
            {
                Id = v3Id,
                TenantId = agency.Id,
                PolicyTemplateId = templateId,
                VersionNumber = 3,
                Status = PolicyVersionStatus.Draft,
                CreatedBy = agency.EmployeeAt(0).Id
            });

        db.PolicyRules.AddRange(
            new PolicyRule
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "policy-rule-v2"),
                TenantId = agency.Id,
                PolicyTemplateVersionId = v2Id,
                RuleKey = "ACC-R-PRIV-EVIDENCE",
                Name = "الأصل المميز يتطلب دليلاً",
                Priority = 10,
                WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""",
                ThenJson = """[{"effect":"RequireEvidence"}]"""
            },
            new PolicyRule
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "policy-rule-v2-workflow"),
                TenantId = agency.Id,
                PolicyTemplateVersionId = v2Id,
                RuleKey = "ACC-R-PRIV-WORKFLOW",
                Name = "الأصل المميز يتطلب سير اعتماد",
                Priority = 20,
                WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""",
                ThenJson = """[{"effect":"TriggerWorkflow","params":{"workflowCode":"ASSET-ASSIGN-APPROVAL"}}]"""
            },
            new PolicyRule
            {
                Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "policy-rule-v3"),
                TenantId = agency.Id,
                PolicyTemplateVersionId = v3Id,
                RuleKey = "ACC-R-PRIV-DENY",
                Name = "مسودة رفض الأصل المميز",
                Priority = 5,
                WhenJson = """{"all":[{"field":"isPrivileged","operator":"Equals","value":"true"}]}""",
                ThenJson = """[{"effect":"Deny","params":{"reason":"acceptance-v3-deny"}}]"""
            });

        db.PolicyPublications.Add(new PolicyPublication
        {
            Id = AcceptanceSeedCatalog.DeterministicGuid(agency.Code, "policy-publication-v2"),
            TenantId = agency.Id,
            PolicyTemplateVersionId = v2Id,
            PublishedAt = AcceptanceSeedCatalog.ReferenceDate.AddYears(-1),
            EvidenceRef = $"{AcceptanceSeedCatalog.PackId}:{agency.Code}"
        });

        await db.SaveChangesAsync(ct);
    }

    private static EmploymentStatus MapEmploymentStatus(AcceptanceEmployeeState state) => state switch
    {
        AcceptanceEmployeeState.OnLeave => EmploymentStatus.OnLeave,
        AcceptanceEmployeeState.Seconded => EmploymentStatus.Seconded,
        AcceptanceEmployeeState.Terminated => EmploymentStatus.Terminated,
        AcceptanceEmployeeState.Retired => EmploymentStatus.Retired,
        _ => EmploymentStatus.Active
    };
}
