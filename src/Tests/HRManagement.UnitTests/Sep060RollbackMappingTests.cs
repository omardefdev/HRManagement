using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-60 (P-5's rollback reading; P6's review, finding 1): what PLAN-59's tag does with the four items SEP060 adds to every open record.
/// That tag maps <c>SeparationChecklists.Step</c> with <c>HasConversion&lt;string&gt;()</c> over its six steps, and EF refuses a name its
/// enum does not know. So after a rollback the previous tag cannot load the whole checklist of a record that holds one of the four
/// items: its separation wizard (steps 2 to 4) and its publication stop on such a record, which also keeps it from executing a record
/// outside the approval. Its reads that filter by step in SQL (completing a step, the payroll party's item), its separation list and its
/// record page do not load them and keep working; a record it creates has its six items only. <c>docs/OPS_VPS_SINGLE_HOST.md</c> states
/// this as what a rollback leaves behind (fix forward); <c>SeparationStatusReconciler</c> puts the records back in line when this tag runs
/// again. Sep060RollbackReadingTests proves the SQL half (its inserts and reads of the columns it knows).
/// </summary>
public sealed class Sep060RollbackMappingTests
{
    /// <summary>PLAN-59's <c>SeparationStep</c> (tag 8086ac8629af), in its order.</summary>
    private enum PreviousSeparationStep
    {
        ContractTerminated,
        PayrollSettled,
        CardRevoked,
        AccountDisabled,
        AssetsReturned,
        Archived
    }

    [Theory]
    [InlineData("DecisionIssued")]
    [InlineData("EmployeeNotified")]
    [InlineData("ItAccessRevoked")]
    [InlineData("HrCoreUpdated")]
    public void The_previous_tags_mapping_refuses_a_new_item_name(string stored)
    {
        var converter = new EnumToStringConverter<PreviousSeparationStep>();

        var refused = Assert.Throws<InvalidOperationException>(() => converter.ConvertFromProvider(stored));

        Assert.Contains(stored, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_previous_tags_own_names_still_read_as_themselves_at_the_same_place()
    {
        var converter = new EnumToStringConverter<PreviousSeparationStep>();
        foreach (var step in Enum.GetValues<PreviousSeparationStep>())
            Assert.Equal(step, converter.ConvertFromProvider(step.ToString()));
        // The current enum keeps every older name at its place, so a row the previous tag writes reads back as the same step.
        foreach (var step in Enum.GetValues<PreviousSeparationStep>())
            Assert.True(Enum.TryParse<HR.Contracts.Domain.Enums.SeparationStep>(step.ToString(), out var current) && (int)current == (int)step,
                step.ToString());
    }
}
