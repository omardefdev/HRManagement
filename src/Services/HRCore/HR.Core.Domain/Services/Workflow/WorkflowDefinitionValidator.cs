using HRManagement.SharedKernel.Workflow;

namespace HR.Core.Domain.Services.Workflow;

/// <summary>The part of a definition the validator reads, so it needs no entity and no database.</summary>
public sealed record WorkflowDefinitionDraft(
    string Code,
    string Name,
    string DomainEntityType,
    IReadOnlyList<WorkflowStepSnapshot> Steps,
    IReadOnlyList<string> GuardrailRoles);

/// <summary>
/// PLAN-43 (G-WF-04): the rules a definition passes before it is submitted and again when it is published. It returns every error
/// at once, in Arabic, for the person who wrote the definition. The role catalog and the known entity types are passed in: they
/// live in layers this one does not reference.
/// </summary>
public static class WorkflowDefinitionValidator
{
    /// <summary>The operators <see cref="WorkflowCondition.Evaluate"/> understands; any other one is silently false there.</summary>
    public static readonly IReadOnlySet<string> KnownOperators = new HashSet<string>(StringComparer.Ordinal)
    {
        "Equals", "NotEquals", "GreaterThan", "LessThan", "GreaterThanOrEqual", "Contains", "In"
    };

    public static IReadOnlyList<string> Validate(
        WorkflowDefinitionDraft definition,
        IReadOnlySet<string> knownRoles,
        IReadOnlySet<string> knownEntityTypes)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Code))
            errors.Add("رمز التعريف مطلوب.");
        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("اسم التعريف مطلوب.");

        var knownType = !string.IsNullOrWhiteSpace(definition.DomainEntityType)
            && knownEntityTypes.Contains(definition.DomainEntityType);
        if (!knownType)
            errors.Add($"نوع الكيان «{definition.DomainEntityType}» غير معروف.");

        ValidateLevels(definition.Steps, errors);
        foreach (var step in definition.Steps.OrderBy(s => s.Level))
            ValidateStep(step, knownRoles, errors);

        foreach (var role in definition.GuardrailRoles)
        {
            if (!knownRoles.Contains(role))
                errors.Add($"دور الحماية «{role}» غير موجود في كتالوج الأدوار.");
            else if (!HasRole(definition.Steps, role))
                errors.Add($"دور الحماية «{role}» لا تحمله أي خطوة.");
        }

        if (knownType)
            ValidateGuardrail(definition, WorkflowGuardrails.For(definition.DomainEntityType), errors);

        return errors;
    }

    private static void ValidateLevels(IReadOnlyList<WorkflowStepSnapshot> steps, List<string> errors)
    {
        if (steps.Count == 0)
        {
            errors.Add("لا يمكن قبول تعريف بدون خطوات.");
            return;
        }

        var levels = steps.Select(s => s.Level).ToList();
        if (levels.Distinct().Count() != levels.Count)
            errors.Add("مستويات الخطوات يجب أن تكون فريدة.");

        var expected = Enumerable.Range(1, levels.Count);
        if (!levels.Distinct().Order().SequenceEqual(expected) && levels.Distinct().Count() == levels.Count)
            errors.Add("مستويات الخطوات يجب أن تكون متصلة وتبدأ من 1.");
    }

    private static void ValidateStep(WorkflowStepSnapshot step, IReadOnlySet<string> knownRoles, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(step.Name))
            errors.Add($"الخطوة {step.Level} بلا اسم.");

        if (string.IsNullOrWhiteSpace(step.RoleRequired))
            errors.Add($"الخطوة {step.Level} بلا دور.");
        else if (!knownRoles.Contains(step.RoleRequired))
            errors.Add($"دور الخطوة {step.Level} «{step.RoleRequired}» غير موجود في كتالوج الأدوار.");

        if (step.Condition is null)
            return;

        if (string.IsNullOrWhiteSpace(step.Condition.Field))
            errors.Add($"شرط الخطوة {step.Level} بلا حقل.");
        if (!KnownOperators.Contains(step.Condition.Operator))
            errors.Add($"شرط الخطوة {step.Level} يستعمل مشغّلاً غير معروف «{step.Condition.Operator}».");
    }

    private static void ValidateGuardrail(WorkflowDefinitionDraft definition, WorkflowGuardrail guardrail, List<string> errors)
    {
        var minSteps = guardrail.MinStepsFor(definition.Code);
        if (definition.Steps.Count < minSteps)
            errors.Add($"هذا النوع يتطلب {minSteps} خطوات على الأقل.");

        if (guardrail.LastStepRole is { } lastRole
            && definition.Steps.Count > 0
            && !string.Equals(definition.Steps.MaxBy(s => s.Level)!.RoleRequired, lastRole, StringComparison.OrdinalIgnoreCase))
            errors.Add($"الخطوة الأخيرة لهذا النوع يجب أن تكون بدور «{lastRole}».");

        foreach (var role in guardrail.RequiredRoles ?? [])
        {
            if (!HasRole(definition.Steps, role))
                errors.Add($"هذا النوع يتطلب خطوة بدور «{role}».");
        }
    }

    private static bool HasRole(IReadOnlyList<WorkflowStepSnapshot> steps, string role) =>
        steps.Any(s => string.Equals(s.RoleRequired, role, StringComparison.OrdinalIgnoreCase));
}
