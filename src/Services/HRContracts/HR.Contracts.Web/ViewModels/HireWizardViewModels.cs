using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HR.Contracts.Web.ViewModels;

/// <summary>FUN-029: hire wizard step 2 — checklist plus the contract, decision and medical actions.</summary>
public class HireWizardStep2ViewModel
{
    public ServiceTransaction Txn { get; set; } = null!;
    public IReadOnlyList<HireChecklist> Checklist { get; set; } = [];
    public Dictionary<HireStep, string> Labels { get; set; } = new();

    public Contract? Contract { get; set; }

    /// <summary>F3: no contract type matched the hire's regime/employment type (intervention opened).</summary>
    public bool NeedsContractType { get; set; }

    public string? UnresolvedReason { get; set; }

    public SelectList? ContractTypes { get; set; }

    /// <summary>I3: the appointment decision linked to the transaction.</summary>
    public AdministrativeDecision? Decision { get; set; }

    public bool HasDecisionStep => Checklist.Any(c => c.Step == HireStep.AppointmentDecisionIssued);

    public HireChecklist? MedicalStep => Checklist.FirstOrDefault(c => c.Step == HireStep.PreEmploymentMedical);
}
