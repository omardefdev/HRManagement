using System.Text;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Integration;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-58 (§2, D-PAY-9): the bank file is today's treasury CSV, built from the orders it is given (one sending), each to its pinned
/// account and for the amount the order pays, with a closing control line of the count and the total; the same orders make the same bytes.
/// </summary>
public sealed class Pay058BankFileBuilderTests
{
    private static readonly PayrollRun Run = new()
    {
        Id = Guid.Parse("58000000-0000-4000-8000-0000000000a1"),
        PeriodStart = new DateTime(2030, 3, 1, 0, 0, 0, DateTimeKind.Utc),
        PeriodEnd = new DateTime(2030, 3, 31, 0, 0, 0, DateTimeKind.Utc),
    };

    private static readonly PaymentBatch Batch = new() { Id = Guid.Parse("58000000-0000-4000-8000-0000000000b1"), PayrollRunId = Run.Id };

    private static PaymentBatchFileRow Row(int n, decimal amount, decimal net, string account) => new(
        new PaymentOrder
        {
            Id = Guid.Parse($"58000000-0000-4000-8000-0000000000{n:D2}"),
            EmployeeId = Guid.Parse($"58000000-0000-4000-8000-0000000001{n:D2}"),
            Amount = amount,
            BeneficiaryIban = account,
        },
        new SalaryLine { GrossPay = net + 100m, TotalDeductions = 100m, NetPay = net },
        $"موظف {n}");

    [Fact]
    public void The_file_carries_the_given_orders_their_accounts_and_amounts_and_a_control_line()
    {
        var rows = new[] { Row(2, 600m, 1000m, "0001-ACC-2"), Row(1, 2500.5m, 2500.5m, "0001-ACC-1") };

        var built = PaymentBatchFileBuilder.Build(Batch, Run, rows, sequence: 1, PaymentBatchFileKind.Initial);

        var lines = Encoding.UTF8.GetString(built.Content).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(PaymentBatchFileBuilder.Header, lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Equal((2, 3100.5m), (built.ControlCount, built.ControlTotal));
        Assert.Equal($"CONTROL,2,3100.50,{Batch.Id},1,Initial", lines[^1]);
        // Ordered by employee; the account is the order's pinned one; NetPay is the line's, Amount what the order pays.
        Assert.EndsWith(",0001-ACC-1,0.00,58000000-0000-4000-8000-000000000001,2500.50", lines[1]);
        Assert.Contains(",1000.00,0001-ACC-2,", lines[2], StringComparison.Ordinal);
        Assert.EndsWith(",600.00", lines[2]);
        Assert.Contains("موظف 1", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("﻿", Encoding.UTF8.GetString(built.Content), StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_orders_make_the_same_bytes_and_fingerprint_and_another_sending_differs()
    {
        var rows = new[] { Row(1, 100m, 100m, "0001-ACC-1") };
        var a = PaymentBatchFileBuilder.Build(Batch, Run, rows, 1, PaymentBatchFileKind.Initial);
        var b = PaymentBatchFileBuilder.Build(Batch, Run, rows, 1, PaymentBatchFileKind.Initial);
        var supplementary = PaymentBatchFileBuilder.Build(Batch, Run, rows, 2, PaymentBatchFileKind.HeldRelease);

        Assert.Equal(a.Content, b.Content);
        Assert.Equal(a.Sha256, b.Sha256);
        Assert.Equal(64, a.Sha256.Length);
        Assert.NotEqual(a.Sha256, supplementary.Sha256);
        Assert.EndsWith(",2,HeldRelease\n", Encoding.UTF8.GetString(supplementary.Content));
    }

    [Fact]
    public void An_empty_sending_is_a_control_line_of_zero()
    {
        var built = PaymentBatchFileBuilder.Build(Batch, Run, [], 1, PaymentBatchFileKind.Reissue);
        Assert.Equal((0, 0m), (built.ControlCount, built.ControlTotal));
    }
}
