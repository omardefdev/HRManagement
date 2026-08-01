using HR.Core.Domain.Services;
using HRManagement.SharedKernel.Enums;

namespace HRManagement.UnitTests;

public class MedicalEmploymentEffectRulesTests
{
    [Theory]
    [InlineData("Retirement", EmploymentStatus.Retired)]
    [InlineData("FitnessForDuty", EmploymentStatus.Active)]
    [InlineData("LimitedDuties", null)]
    public void ResolveStatusFromRecommendation_MapsExpected(string type, EmploymentStatus? expected) =>
        Assert.Equal(expected, MedicalEmploymentEffectRules.ResolveStatusFromRecommendation(type));

    [Theory]
    [InlineData("MEDICAL_RETIREMENT", EmploymentStatus.Retired)]
    [InlineData("RETURN_TO_WORK", EmploymentStatus.Active)]
    public void ResolveStatusFromMedicalRef_MapsExpected(string medicalRef, EmploymentStatus? expected) =>
        Assert.Equal(expected, MedicalEmploymentEffectRules.ResolveStatusFromMedicalRef(medicalRef));

    [Fact]
    public void RequiresLimitedDutiesRecord_TrueForLimitedDuties() =>
        Assert.True(MedicalEmploymentEffectRules.RequiresLimitedDutiesRecord("LimitedDuties"));

    [Fact]
    public void BuildReason_IncludesSourceSystem() =>
        Assert.Contains("HR.Health", MedicalEmploymentEffectRules.BuildReason("M1", "detail", "HR.Health"));
}
