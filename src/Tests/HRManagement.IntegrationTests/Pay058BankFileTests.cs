using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Integration;
using Payroll.Infrastructure.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58 (§2, P-6): one bank file per sending, built from the orders sent in it, stored with its control count and total, and never
/// built twice: a second request for the same sending makes no second file; the orders released later go in a supplementary file of
/// their own; a reissue (D-PAY-19) is a new sequence that supersedes the file before it.
/// </summary>
[Collection("IntegrationSerial")]
public sealed class Pay058BankFileTests
{
    private static readonly Guid Sender = Pay056RunScope.Poster;

    private static async Task<(Guid RunId, Guid BatchId, Guid Held, Guid Paid)> PostedWithOneHeldAsync(Pay056RunScope scope)
    {
        var paid = await scope.EmployeeAsync();
        var held = await scope.EmployeeAsync();
        foreach (var employee in new[] { paid, held })
            scope.Db.Add(Pay055TestAccounts.Active(employee));
        await scope.Db.SaveChangesAsync();
        var runId = await scope.PostedRunAsync();
        scope.Db.PaymentHolds.Add(new PaymentHold
        {
            EmployeeId = held,
            PayrollRunId = runId,
            HoldType = PaymentHoldType.Manual,
            Reason = "مراجعة يدوية",
            Status = PaymentHoldStatus.Active,
        });
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        var batchId = await scope.Db.PaymentBatches.Where(b => b.PayrollRunId == runId).Select(b => b.Id).SingleAsync();
        return (runId, batchId, held, paid);
    }

    private static async Task<List<PaymentBatchFile>> FilesAsync(Pay056RunScope scope, Guid batchId)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.PaymentBatchFiles.AsNoTracking().Where(f => f.PaymentBatchId == batchId).OrderBy(f => f.Sequence).ToListAsync();
    }

    private static string[] DataRows(PaymentBatchFile file) =>
        Encoding.UTF8.GetString(file.Content).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1).Where(r => !r.StartsWith(PaymentBatchFileBuilder.ControlTag, StringComparison.Ordinal)).ToArray();

    [Fact]
    public async Task A_sending_stores_one_file_of_its_own_orders_and_the_same_sending_asked_again_makes_none()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var (_, batchId, held, paid) = await PostedWithOneHeldAsync(scope);

        var sent = await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Sender));
        Assert.True(sent.Success, sent.Error);

        var file = Assert.Single(await FilesAsync(scope, batchId));
        Assert.Equal(sent.Id, file.Id);
        Assert.Equal((1, PaymentBatchFileKind.Initial, Sender), (file.Sequence, file.Kind, file.GeneratedBy));
        var orders = await scope.Db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == batchId).ToListAsync();
        var paidOrder = orders.Single(o => o.EmployeeId == paid);
        Assert.Equal((1, paidOrder.Amount), (file.ControlCount, file.ControlTotal));
        Assert.Equal(file.Id, paidOrder.PaymentBatchFileId);
        var row = Assert.Single(DataRows(file));
        Assert.Contains(Pay055TestAccounts.Iban(paid), row, StringComparison.Ordinal);
        Assert.DoesNotContain(held.ToString(), Encoding.UTF8.GetString(file.Content), StringComparison.OrdinalIgnoreCase);
        Assert.Null(orders.Single(o => o.EmployeeId == held).PaymentBatchFileId);
        Assert.Equal(file.Sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file.Content)).ToLowerInvariant());

        var again = await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Sender));
        Assert.Equal(RunActOutcome.Conflict, again.Outcome);
        Assert.Single(await FilesAsync(scope, batchId));
    }

    [Fact]
    public async Task A_released_order_is_sent_alone_in_a_supplementary_file_and_a_reissue_supersedes_its_file()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var (_, batchId, held, _) = await PostedWithOneHeldAsync(scope);
        Assert.True((await scope.Mediator.Send(new ProcessPaymentBatchCommand(batchId, Sender))).Success);

        // The hold is released (two people; the decision is the four-eyes service's from P3 on): the order leaves by submit-held.
        var hold = await scope.Db.PaymentHolds.SingleAsync(h => h.EmployeeId == held);
        hold.Status = PaymentHoldStatus.Released;
        await scope.Db.SaveChangesAsync();

        var supplementary = await scope.Mediator.Send(new SubmitHeldPaymentOrdersCommand(batchId, Sender));
        Assert.True(supplementary.Success, supplementary.Error);
        var files = await FilesAsync(scope, batchId);
        Assert.Equal(new[] { (1, PaymentBatchFileKind.Initial), (2, PaymentBatchFileKind.HeldRelease) }, files.Select(f => (f.Sequence, f.Kind)));
        Assert.Contains(Pay055TestAccounts.Iban(held), Assert.Single(DataRows(files[1])), StringComparison.Ordinal);
        Assert.Equal(1, files[1].ControlCount);

        // D-PAY-19: a reissue of the first file is a new sequence with the same orders; the first is superseded and cannot be reissued.
        var writer = scope.Services.GetRequiredService<PaymentBatchFileWriter>();
        var (reissued, error) = await writer.ReissueAsync(files[0].Id, "تلف الملف عند القناة", Pay056RunScope.Reverser, Pay056RunScope.OtherApprover, CancellationToken.None);
        Assert.Null(error);
        await scope.Db.SaveChangesAsync();
        files = await FilesAsync(scope, batchId);
        Assert.Equal(3, files.Count);
        var reissue = files.Single(f => f.Id == reissued);
        Assert.Equal((3, PaymentBatchFileKind.Reissue, "تلف الملف عند القناة", (Guid?)Pay056RunScope.OtherApprover),
            (reissue.Sequence, reissue.Kind, reissue.ReissueReason, reissue.ReissueApprovedBy));
        Assert.Equal(reissued, files[0].SupersededById);
        Assert.Equal(DataRows(files[0]), DataRows(reissue));
        Assert.Equal(PaymentBatchFileWriter.AlreadySuperseded,
            (await writer.ReissueAsync(files[0].Id, "مرة ثانية", Pay056RunScope.Reverser, Pay056RunScope.OtherApprover, CancellationToken.None)).Error);
    }
}
