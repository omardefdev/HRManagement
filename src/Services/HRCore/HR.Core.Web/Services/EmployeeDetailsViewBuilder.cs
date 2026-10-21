using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Services;

public interface IEmployeeDetailsViewBuilder
{
    Task<EmployeeDetailsViewModel?> BuildAsync(
        Guid employeeId,
        string? openModal = null,
        EmployeeDetailsModalState? modalState = null);
}

public class EmployeeDetailsViewBuilder(
    HRCoreDbContext context,
    IServiceRegimeProvider serviceRegimeProvider,
    ILegislationRegisterProvider legislationProvider,
    ServiceRegimeTransitionService transitionService) : IEmployeeDetailsViewBuilder
{
    public async Task<EmployeeDetailsViewModel?> BuildAsync(
        Guid employeeId,
        string? openModal = null,
        EmployeeDetailsModalState? modalState = null)
    {
        var employee = await context.Employees
            .Include(e => e.PrimaryOrgUnit)
            .FirstOrDefaultAsync(e => e.Id == employeeId);

        if (employee == null) return null;

        var employmentRecords = await context.EmploymentRecords
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.StartDate)
            .ToListAsync();

        var vm = new EmployeeDetailsViewModel
        {
            Employee = employee,
            Qualifications = await context.Qualifications.Where(q => q.EmployeeId == employeeId).ToListAsync(),
            Contacts = await context.ContactInfos.Where(c => c.EmployeeId == employeeId).ToListAsync(),
            Documents = await context.EmployeeDocuments.Where(d => d.EmployeeId == employeeId).ToListAsync(),
            EmploymentRecords = employmentRecords,
            Evaluations = await context.PerformanceEvaluations.Where(ev => ev.EmployeeId == employeeId).OrderByDescending(ev => ev.Year).ToListAsync(),
            StatusHistory = await context.EmploymentStatusHistories.Where(h => h.EmployeeId == employeeId).OrderByDescending(h => h.ChangedAt).ToListAsync(),
            Profile = await context.EmployeeProfiles.FirstOrDefaultAsync(p => p.EmployeeId == employeeId),
            RegimeTransitions = await transitionService.ListForEmployeeAsync(employeeId)
        };

        var currentRecord = employmentRecords.FirstOrDefault(r => r.EndDate is null)
                            ?? employmentRecords.FirstOrDefault();
        if (currentRecord?.LegalRegimeId is string regimeId)
        {
            var regime = await serviceRegimeProvider.GetByIdAsync(regimeId);
            vm.ServiceRegimeLabel = regime?.DisplayLabel ?? regimeId;
            if (regime?.ResolvedLegalSourceId is string legalSourceId)
            {
                var legalSource = await legislationProvider.GetByIdAsync(legalSourceId);
                vm.ServiceRegimeLegalSourceLabel = legalSource?.DisplayLabel ?? legalSourceId;
            }
        }

        vm.QualificationCreate = new QualificationCreateViewModel
        {
            EmployeeId = employeeId,
            EmployeeName = employee.FullName,
            ReturnTo = "details"
        };
        vm.ContactCreate = new ContactCreateViewModel
        {
            EmployeeId = employeeId,
            EmployeeName = employee.FullName,
            ReturnTo = "details"
        };
        vm.DocumentCreate = new DocumentCreateViewModel
        {
            EmployeeId = employeeId,
            EmployeeName = employee.FullName,
            ReturnTo = "details"
        };
        vm.EvaluationCreate = new EvaluationCreateViewModel
        {
            EmployeeId = employeeId,
            EmployeeName = employee.FullName,
            ReturnTo = "details"
        };

        if (modalState != null)
        {
            if (modalState.QualificationCreate != null)
            {
                modalState.QualificationCreate.ReturnTo = "details";
                vm.QualificationCreate = modalState.QualificationCreate;
            }
            if (modalState.ContactCreate != null)
            {
                modalState.ContactCreate.ReturnTo = "details";
                vm.ContactCreate = modalState.ContactCreate;
            }
            if (modalState.DocumentCreate != null)
            {
                modalState.DocumentCreate.ReturnTo = "details";
                vm.DocumentCreate = modalState.DocumentCreate;
            }
            if (modalState.EvaluationCreate != null)
            {
                modalState.EvaluationCreate.ReturnTo = "details";
                vm.EvaluationCreate = modalState.EvaluationCreate;
            }

            vm.ShowQualificationCreateModal = modalState.ShowQualificationCreateModal;
            vm.ShowContactCreateModal = modalState.ShowContactCreateModal;
            vm.ShowDocumentCreateModal = modalState.ShowDocumentCreateModal;
            vm.ShowEvaluationCreateModal = modalState.ShowEvaluationCreateModal;
        }
        else if (!string.IsNullOrEmpty(openModal))
        {
            switch (openModal.ToLowerInvariant())
            {
                case "qualification":
                    vm.ShowQualificationCreateModal = true;
                    break;
                case "contact":
                    vm.ShowContactCreateModal = true;
                    break;
                case "document":
                    vm.ShowDocumentCreateModal = true;
                    break;
                case "evaluation":
                    vm.ShowEvaluationCreateModal = true;
                    break;
            }
        }

        return vm;
    }
}
