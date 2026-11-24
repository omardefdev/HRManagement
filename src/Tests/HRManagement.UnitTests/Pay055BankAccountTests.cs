using System.Reflection;
using HRManagement.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using H = HRManagement.UnitTests.Pay055BankAccountHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-55 (§1): the employee's bank account. Every rule of the plan, and for every refusal: the Arabic message, the HTTP status its
/// failure kind stands for, and nothing changed.
/// </summary>
public sealed class Pay055BankAccountTests
{
    private static async Task<H> SeededAsync() => await new H().SeedAsync();

    // --- rule 1: no Active account without a second person and a reference ---

    [Fact]
    public async Task A_processor_registers_an_account_and_it_waits_for_a_second_person()
    {
        var h = await SeededAsync();

        var result = await h.RegisterAsync(" " + string.Join(' ', H.Iban1.ToLowerInvariant().Chunk(4).Select(c => new string(c))));

        Assert.True(result.Success, result.Error);
        Assert.Equal(BankAccountStatus.PendingVerification, result.Status);
        var account = await h.AccountAsync(result.AccountId!.Value);
        Assert.Equal(BankAccountStatus.PendingVerification, account.Status);
        Assert.Equal(H.Iban1, account.AccountNumber);          // normalised: no spaces, upper case
        Assert.Equal(BankAccountKind.Iban, account.AccountKind);
        Assert.Equal(H.Processor, account.CreatedBy);          // the registrar
        Assert.Equal(H.BankName, account.BankNameAr);
        Assert.Equal(H.HolderName, account.HolderNameAr);
        Assert.Null(account.VerifiedBy);
        Assert.Null(account.VerifiedAt);
        Assert.Null(account.SupersedesAccountId);
        Assert.Equal(h.Clock.Now.UtcDateTime, account.CreatedAt);
    }

    [Fact]
    public async Task Another_person_verifies_it_with_the_banks_reference_and_it_is_in_force()
    {
        var h = await SeededAsync();
        var registered = await h.RegisterAsync();

        var result = await h.VerifyAsync(registered.AccountId!.Value, "  " + H.Reference + " ");

        Assert.True(result.Success, result.Error);
        Assert.Equal(BankAccountStatus.Active, result.Status);
        Assert.Null(result.RetiredAccountId);
        var account = await h.AccountAsync(registered.AccountId.Value);
        Assert.Equal(BankAccountStatus.Active, account.Status);
        Assert.Equal(H.Approver, account.VerifiedBy);
        Assert.Equal(h.Clock.Now.UtcDateTime, account.VerifiedAt);
        Assert.Equal(H.Reference, account.VerificationReference);
        Assert.Equal(H.Processor, account.CreatedBy);
    }

    [Fact]
    public async Task The_registrar_cannot_verify_his_own_registration()
    {
        var h = await SeededAsync();
        var registered = await h.RegisterAsync(actor: H.Approver);     // a person who holds both policies registered it

        var result = await h.VerifyAsync(registered.AccountId!.Value, actor: H.Approver);

        Assert.False(result.Success);
        Assert.Equal(BankAccountFailure.SelfVerification, result.Failure);
        Assert.Equal(400, result.StatusCode);                           // the plan's AC-PAY-55a
        Assert.Equal(BankAccountMessages.SelfVerification, result.Error);
        Assert.Equal(BankAccountStatus.PendingVerification, result.Status);
        await AssertUnchangedPendingAsync(h, registered.AccountId.Value);

        // Another approver verifies the same registration.
        Assert.True((await h.VerifyAsync(registered.AccountId.Value, actor: H.OtherApprover)).Success);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_verification_without_the_banks_reference_is_refused(string? reference)
    {
        var h = await SeededAsync();
        var registered = await h.RegisterAsync();

        var result = await h.VerifyAsync(registered.AccountId!.Value, reference);

        Assert.Equal(BankAccountFailure.Invalid, result.Failure);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(BankAccountMessages.ReferenceRequired, result.Error);
        await AssertUnchangedPendingAsync(h, registered.AccountId.Value);
    }

    [Fact]
    public async Task A_reference_longer_than_its_column_is_refused()
    {
        var h = await SeededAsync();
        var registered = await h.RegisterAsync();

        var result = await h.VerifyAsync(registered.AccountId!.Value, new string('م', EmployeeBankAccount.ReferenceMaxLength + 1));

        Assert.Equal(BankAccountMessages.ReferenceTooLong, result.Error);
        Assert.Equal(400, result.StatusCode);
        await AssertUnchangedPendingAsync(h, registered.AccountId.Value);
    }

    [Fact]
    public async Task An_act_with_no_known_actor_is_refused_so_no_account_ever_lacks_a_registrar()
    {
        var h = await SeededAsync();

        var register = await h.RegisterAsync(actor: Guid.Empty);
        Assert.Equal(BankAccountFailure.Invalid, register.Failure);
        Assert.Equal(BankAccountMessages.UnknownActor, register.Error);
        Assert.Empty(await h.AccountsAsync());

        var pending = (await h.RegisterAsync()).AccountId!.Value;
        foreach (var refused in new[]
                 {
                     await h.VerifyAsync(pending, actor: Guid.Empty),
                     await h.RejectAsync(pending, actor: Guid.Empty),
                     await h.RetireAsync(pending, actor: Guid.Empty),
                 })
        {
            Assert.Equal(BankAccountFailure.Invalid, refused.Failure);
            Assert.Equal(BankAccountMessages.UnknownActor, refused.Error);
        }

        await AssertUnchangedPendingAsync(h, pending);
    }

    [Fact]
    public async Task An_account_moved_from_old_data_has_no_registrar_and_is_never_verified_only_rejected()
    {
        var h = await SeededAsync();
        Guid moved;
        await using (var db = h.Db())
        {
            // As migration PAY055 writes it: pending, with no registrar.
            var account = new EmployeeBankAccount
            {
                EmployeeId = H.Employee,
                AccountKind = BankAccountKind.Iban,
                AccountNumber = H.Iban1,
                BankNameAr = H.BankName,
                Status = BankAccountStatus.PendingVerification,
                CreatedBy = null,
            };
            db.Add(account);
            await db.SaveChangesAsync();
            moved = account.Id;
        }

        var verify = await h.VerifyAsync(moved);

        Assert.Equal(BankAccountFailure.Conflict, verify.Failure);
        Assert.Equal(BankAccountMessages.NoRegistrar, verify.Error);
        Assert.Equal(BankAccountStatus.PendingVerification, (await h.AccountAsync(moved)).Status);

        Assert.True((await h.RejectAsync(moved)).Success);
        Assert.Equal(BankAccountStatus.Rejected, (await h.AccountAsync(moved)).Status);
    }

    // --- rule 2: a change is a new request; the account in force stays in force until its replacement is verified ---

    [Fact]
    public async Task A_change_of_account_is_a_new_registration_and_the_account_in_force_stays_in_force_until_it_is_verified()
    {
        var h = await SeededAsync();
        var first = await h.ActiveAccountAsync(H.Iban1);

        var replacement = await h.RegisterAsync(H.Iban2);

        Assert.True(replacement.Success, replacement.Error);
        Assert.Equal(BankAccountStatus.Active, (await h.AccountAsync(first)).Status);
        var pending = await h.AccountAsync(replacement.AccountId!.Value);
        Assert.Equal(BankAccountStatus.PendingVerification, pending.Status);
        Assert.Equal(first, pending.SupersedesAccountId);

        h.Clock.Now = h.Clock.Now.AddHours(3);
        var verified = await h.VerifyAsync(replacement.AccountId.Value, actor: H.OtherApprover);

        Assert.True(verified.Success, verified.Error);
        Assert.Equal(first, verified.RetiredAccountId);
        var retired = await h.AccountAsync(first);
        Assert.Equal(BankAccountStatus.Retired, retired.Status);
        Assert.Equal(H.OtherApprover, retired.RetiredBy);
        Assert.Equal(h.Clock.Now.UtcDateTime, retired.RetiredAt);
        Assert.Equal(BankAccountMessages.ReplacedByVerifiedAccount, retired.RetirementReason);
        Assert.Equal(BankAccountStatus.Active, (await h.AccountAsync(replacement.AccountId.Value)).Status);
        Assert.Single(await h.AccountsAsync(), a => a.Status == BankAccountStatus.Active);
    }

    [Fact]
    public async Task A_rejected_replacement_leaves_the_account_in_force_untouched()
    {
        var h = await SeededAsync();
        var first = await h.ActiveAccountAsync(H.Iban1);
        var replacement = await h.RegisterAsync(H.Iban2);

        Assert.True((await h.RejectAsync(replacement.AccountId!.Value)).Success);

        var inForce = await h.AccountAsync(first);
        Assert.Equal(BankAccountStatus.Active, inForce.Status);
        Assert.Null(inForce.RetiredAt);
    }

    [Fact]
    public async Task A_second_pending_account_of_the_same_employee_is_a_conflict()
    {
        var h = await SeededAsync();
        Assert.True((await h.RegisterAsync(H.Iban1)).Success);

        var second = await h.RegisterAsync(H.Iban2);

        Assert.Equal(BankAccountFailure.Conflict, second.Failure);
        Assert.Equal(409, second.StatusCode);
        Assert.Equal(BankAccountMessages.PendingExists, second.Error);
        Assert.Single(await h.AccountsAsync());

        // Another employee is not concerned.
        Assert.True((await h.RegisterAsync(H.Iban2, employee: H.OtherEmployee)).Success);
    }

    [Fact]
    public async Task Registering_the_number_that_is_already_in_force_is_a_conflict()
    {
        var h = await SeededAsync();
        await h.ActiveAccountAsync(H.Iban1);

        var again = await h.RegisterAsync(H.Iban1.ToLowerInvariant());

        Assert.Equal(BankAccountFailure.Conflict, again.Failure);
        Assert.Equal(BankAccountMessages.AlreadyInForce, again.Error);
        Assert.Single(await h.AccountsAsync());
    }

    // --- rule 3: a bad number or an unknown bank is 400, and no row ---

    [Fact]
    public async Task A_wrong_check_digit_is_refused_and_no_row_is_written()
    {
        var h = await SeededAsync();
        var wrong = H.Iban1[..3] + (H.Iban1[3] == '9' ? '8' : (char)(H.Iban1[3] + 1)) + H.Iban1[4..];

        var result = await h.RegisterAsync(wrong);

        Assert.Equal(BankAccountFailure.Invalid, result.Failure);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(BankAccountMessages.IbanCheckDigits, result.Error);
        Assert.Null(result.AccountId);
        Assert.Empty(await h.AccountsAsync());
    }

    [Fact]
    public async Task With_no_bank_defined_a_local_number_is_refused_in_arabic_and_only_an_iban_is_accepted()
    {
        var h = await SeededAsync();                 // the production list: empty (D-PAY-11)

        var local = await h.RegisterAsync("001234567890", kind: BankAccountKind.Local, bankCode: "CBS");
        var noBank = await h.RegisterAsync("001234567890", kind: BankAccountKind.Local);

        Assert.Equal(400, local.StatusCode);
        Assert.Equal(BankAccountMessages.UnknownBank, local.Error);
        Assert.Equal(400, noBank.StatusCode);
        Assert.Equal(BankAccountMessages.BankRequired, noBank.Error);
        Assert.Empty(await h.AccountsAsync());

        Assert.True((await h.RegisterAsync(H.Iban1)).Success);
    }

    [Fact]
    public async Task A_local_number_of_a_defined_bank_is_accepted_and_takes_the_banks_name()
    {
        var h = (await SeededAsync()).WithBank();

        var result = await h.RegisterAsync(" 0012 3456 7890 ", kind: BankAccountKind.Local, bankCode: "cbs", bankName: "اسم كتبه المسجّل");

        Assert.True(result.Success, result.Error);
        var account = await h.AccountAsync(result.AccountId!.Value);
        Assert.Equal(BankAccountKind.Local, account.AccountKind);
        Assert.Equal("001234567890", account.AccountNumber);
        Assert.Equal("CBS", account.BankCode);
        Assert.Equal("المصرف التجاري السوري", account.BankNameAr);     // the definition's name, not the typed one

        var bad = await h.RegisterAsync("12345", employee: H.OtherEmployee, kind: BankAccountKind.Local, bankCode: "CBS");
        Assert.Equal(BankAccountMessages.LocalForm, bad.Error);
        Assert.Empty(await h.AccountsAsync(H.OtherEmployee));
    }

    [Fact]
    public async Task An_iban_with_no_bank_code_needs_the_banks_name()
    {
        var h = await SeededAsync();

        var result = await h.RegisterAsync(bankName: "  ");

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(BankAccountMessages.BankNameRequired, result.Error);
        Assert.Empty(await h.AccountsAsync());
    }

    [Fact]
    public async Task An_unknown_kind_is_refused()
    {
        var h = await SeededAsync();

        var result = await h.RegisterAsync(kind: (BankAccountKind)(-1));

        Assert.Equal(400, result.StatusCode);
        Assert.Equal(BankAccountMessages.KindUnknown, result.Error);
        Assert.Empty(await h.AccountsAsync());
    }

    // --- reject and retire ---

    [Fact]
    public async Task A_pending_account_is_rejected_with_a_reason_and_nothing_else_changes()
    {
        var h = await SeededAsync();
        var pending = (await h.RegisterAsync()).AccountId!.Value;

        var noReason = await h.RejectAsync(pending, reason: " ");
        Assert.Equal(400, noReason.StatusCode);
        Assert.Equal(BankAccountMessages.RejectReasonRequired, noReason.Error);
        await AssertUnchangedPendingAsync(h, pending);

        var result = await h.RejectAsync(pending, reason: " الرقم لا يطابق كتاب المصرف ");

        Assert.True(result.Success, result.Error);
        var account = await h.AccountAsync(pending);
        Assert.Equal(BankAccountStatus.Rejected, account.Status);
        Assert.Equal(H.Approver, account.RejectedBy);
        Assert.Equal("الرقم لا يطابق كتاب المصرف", account.RejectionReason);
        Assert.Equal(h.Clock.Now.UtcDateTime, account.RejectedAt);

        // A decision is not taken twice, and a rejected account is never verified.
        Assert.Equal(409, (await h.RejectAsync(pending)).StatusCode);
        var verify = await h.VerifyAsync(pending);
        Assert.Equal(409, verify.StatusCode);
        Assert.Equal(BankAccountMessages.NotPending, verify.Error);
        Assert.Equal(BankAccountStatus.Rejected, (await h.AccountAsync(pending)).Status);

        // The employee may be registered again.
        Assert.True((await h.RegisterAsync(H.Iban2)).Success);
    }

    [Fact]
    public async Task The_account_in_force_is_retired_with_a_reason()
    {
        var h = await SeededAsync();
        var active = await h.ActiveAccountAsync();

        var noReason = await h.RetireAsync(active, reason: null);
        Assert.Equal(400, noReason.StatusCode);
        Assert.Equal(BankAccountMessages.RetireReasonRequired, noReason.Error);
        Assert.Equal(BankAccountStatus.Active, (await h.AccountAsync(active)).Status);

        var tooLong = await h.RetireAsync(active, reason: new string('س', EmployeeBankAccount.ReasonMaxLength + 1));
        Assert.Equal(BankAccountMessages.ReasonTooLong, tooLong.Error);

        var result = await h.RetireAsync(active, reason: "أغلق الموظف حسابه");

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, result.HoldsPlaced);
        var account = await h.AccountAsync(active);
        Assert.Equal(BankAccountStatus.Retired, account.Status);
        Assert.Equal(H.Approver, account.RetiredBy);
        Assert.Equal("أغلق الموظف حسابه", account.RetirementReason);
        Assert.Empty(await h.HoldsAsync());

        var twice = await h.RetireAsync(active);
        Assert.Equal(409, twice.StatusCode);
        Assert.Equal(BankAccountMessages.NotActive, twice.Error);
    }

    [Fact]
    public async Task Only_the_account_in_force_is_retired_and_only_a_pending_one_is_verified()
    {
        var h = await SeededAsync();
        var pending = (await h.RegisterAsync()).AccountId!.Value;

        var retirePending = await h.RetireAsync(pending);
        Assert.Equal(409, retirePending.StatusCode);
        Assert.Equal(BankAccountMessages.NotActive, retirePending.Error);
        await AssertUnchangedPendingAsync(h, pending);

        Assert.True((await h.VerifyAsync(pending)).Success);
        var verifyActive = await h.VerifyAsync(pending, actor: H.OtherApprover);
        Assert.Equal(409, verifyActive.StatusCode);
        Assert.Equal(BankAccountMessages.NotPending, verifyActive.Error);
        Assert.Equal(H.Approver, (await h.AccountAsync(pending)).VerifiedBy);   // the first verifier stays
    }

    // --- rule 5: retiring an account a run line pinned and that is not yet disbursed ---

    [Fact]
    public async Task Retiring_an_account_pinned_to_a_line_not_yet_disbursed_holds_the_employee_in_that_run()
    {
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync();
        var (run, _) = await h.RunLineAsync(account);

        var result = await h.RetireAsync(account, reason: "أغلق الموظف حسابه");

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.HoldsPlaced);
        var hold = Assert.Single(await h.HoldsAsync());
        Assert.Equal(H.Employee, hold.EmployeeId);
        Assert.Equal(run, hold.PayrollRunId);
        Assert.Equal(PaymentHoldType.Manual, hold.HoldType);
        Assert.Equal(PaymentHoldStatus.Active, hold.Status);
        Assert.StartsWith(EmployeeBankAccount.RetiredHoldRule, hold.Reason);
        Assert.StartsWith("ACCOUNT-RETIRED", hold.Reason);
        Assert.Contains("أغلق الموظف حسابه", hold.Reason);
        Assert.Equal(H.Approver, hold.CreatedBy);
        Assert.DoesNotContain(H.Iban1, hold.Reason);
        Assert.DoesNotContain(H.Iban1[^4..], hold.Reason);
    }

    [Fact]
    public async Task The_verification_of_a_replacement_places_the_same_hold_a_persons_retirement_places()
    {
        var h = await SeededAsync();
        var first = await h.ActiveAccountAsync(H.Iban1);
        var (run, _) = await h.RunLineAsync(first);
        var replacement = await h.RegisterAsync(H.Iban2);

        var verified = await h.VerifyAsync(replacement.AccountId!.Value, actor: H.OtherApprover);

        Assert.True(verified.Success, verified.Error);
        Assert.Equal(1, verified.HoldsPlaced);
        var hold = Assert.Single(await h.HoldsAsync());
        Assert.Equal(run, hold.PayrollRunId);
        Assert.Equal(H.Employee, hold.EmployeeId);
        Assert.StartsWith("ACCOUNT-RETIRED", hold.Reason);
        Assert.Contains(BankAccountMessages.ReplacedByVerifiedAccount, hold.Reason);
        Assert.Equal(H.OtherApprover, hold.CreatedBy);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Draft, 1)]
    [InlineData(PaymentOrderStatus.Rejected, 1)]
    [InlineData(PaymentOrderStatus.Submitted, 0)]
    [InlineData(PaymentOrderStatus.Acknowledged, 0)]
    [InlineData(PaymentOrderStatus.Settled, 0)]
    [InlineData(PaymentOrderStatus.PartiallySettled, 0)]
    public async Task A_line_is_disbursed_once_its_order_left_for_the_channel(PaymentOrderStatus order, int expectedHolds)
    {
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync();
        await h.RunLineAsync(account, order: order);

        var result = await h.RetireAsync(account);

        Assert.Equal(expectedHolds, result.HoldsPlaced);
        Assert.Equal(expectedHolds, (await h.HoldsAsync()).Count);
    }

    [Fact]
    public async Task Each_run_that_pinned_the_account_gets_its_own_hold_and_a_reversed_or_cancelled_run_none()
    {
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync();
        var (october, _) = await h.RunLineAsync(account, PayrollRunStatus.Posted, month: 10);
        var (september, _) = await h.RunLineAsync(account, PayrollRunStatus.Calculated, month: 9);
        await h.RunLineAsync(account, PayrollRunStatus.Reversed, month: 8);
        await h.RunLineAsync(account, PayrollRunStatus.Cancelled, month: 5);
        await h.RunLineAsync(accountId: null, month: 7);                       // a line with no pinned account
        await h.RunLineAsync(account, order: PaymentOrderStatus.Settled, month: 6);

        var result = await h.RetireAsync(account);

        Assert.Equal(2, result.HoldsPlaced);
        Assert.Equal(new[] { october, september }.Order(), (await h.HoldsAsync()).Select(x => x.PayrollRunId!.Value).Order());
    }

    [Fact]
    public async Task A_retirement_between_the_snapshot_and_the_writing_of_the_line_holds_the_run()
    {
        // The run pins the account on its snapshot first and writes the line later (a long, paused or resumed calculation).
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync();
        var calculating = await h.RunSnapshotAsync(account, month: 11);
        await h.RunSnapshotAsync(account, PayrollRunStatus.Cancelled, month: 4);                     // pays nothing
        await h.RunSnapshotAsync(accountId: null, month: 3);                                         // pinned nothing
        await h.RunSnapshotAsync(account, month: 2, employee: H.OtherEmployee);                      // another employee's snapshot row

        var result = await h.RetireAsync(account);

        Assert.Equal(1, result.HoldsPlaced);
        var hold = Assert.Single(await h.HoldsAsync());
        Assert.Equal((H.Employee, (Guid?)calculating), (hold.EmployeeId, hold.PayrollRunId));
        Assert.StartsWith("ACCOUNT-RETIRED", hold.Reason);
    }

    [Fact]
    public async Task A_run_whose_line_is_written_is_judged_by_its_line_not_by_an_older_snapshot()
    {
        var h = await SeededAsync();
        var first = await h.ActiveAccountAsync(H.Iban1);
        var replacement = await h.RegisterAsync(H.Iban2);
        Assert.True((await h.VerifyAsync(replacement.AccountId!.Value)).Success);
        // The run was calculated again after the replacement: its line pins the new account; an older job's snapshot still names the first.
        var (run, _) = await h.RunLineAsync(replacement.AccountId.Value, month: 10);
        await using (var db = h.Db())
        {
            var job = new PayrollRunJob { PayrollRunId = run, Status = PayrollRunJobStatus.Cancelled };
            job.InputSnapshots.Add(new PayrollRunInputSnapshot { EmployeeId = H.Employee, ProfileVersion = "v", BankAccountId = first });
            db.Add(job);
            await db.SaveChangesAsync();
        }

        // Retiring the replacement holds the run (its line); the first account, already retired, placed nothing through the old snapshot.
        Assert.Empty(await h.HoldsAsync());
        Assert.Equal(1, (await h.RetireAsync(replacement.AccountId.Value)).HoldsPlaced);
    }

    [Theory]
    [InlineData(PaymentBatchStatus.Submitted, 0)]
    [InlineData(PaymentBatchStatus.Acked, 0)]
    [InlineData(PaymentBatchStatus.Settled, 0)]
    [InlineData(PaymentBatchStatus.Processed, 0)]
    [InlineData(PaymentBatchStatus.Rejected, 1)]
    [InlineData(PaymentBatchStatus.Returned, 1)]
    [InlineData(PaymentBatchStatus.Failed, 1)]
    [InlineData(PaymentBatchStatus.Reversed, 1)]
    public async Task A_line_whose_batch_the_channel_sent_back_is_not_disbursed_and_is_held(PaymentBatchStatus batch, int expectedHolds)
    {
        // The bank returns a batch because an account is closed: its orders stay «settled», and the batch may be submitted again.
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync();
        var (_, line) = await h.RunLineAsync(account, order: PaymentOrderStatus.Settled);
        await h.SetBatchOfLineAsync(line, batch);

        var result = await h.RetireAsync(account);

        Assert.Equal(expectedHolds, result.HoldsPlaced);
    }

    [Fact]
    public async Task The_holds_reason_says_what_a_release_means_and_names_no_number_and_no_id()
    {
        var h = await SeededAsync();
        var account = await h.ActiveAccountAsync();
        var (run, line) = await h.RunLineAsync(account);

        Assert.Equal(1, (await h.RetireAsync(account, reason: "أغلق الموظف حسابه")).HoldsPlaced);

        var reason = Assert.Single(await h.HoldsAsync()).Reason;
        Assert.Contains("الإفراج عن هذا الحجز يعني الصرف إلى الحساب المحال نفسه", reason, StringComparison.Ordinal);
        foreach (var id in new[] { account, run, line, H.Employee })
            Assert.DoesNotContain(id.ToString("D"), reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(H.Iban1, reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_line_pinned_to_another_account_is_not_held()
    {
        var h = await SeededAsync();
        var first = await h.ActiveAccountAsync(H.Iban1);
        var replacement = await h.RegisterAsync(H.Iban2);
        Assert.True((await h.VerifyAsync(replacement.AccountId!.Value)).Success);   // first retired, no line: no hold
        await h.RunLineAsync(replacement.AccountId.Value, month: 11);                // the next month pinned the new account
        await h.RunLineAsync(first, order: PaymentOrderStatus.Settled, month: 10);

        Assert.Empty(await h.HoldsAsync());
    }

    [Fact]
    public async Task A_run_that_already_holds_the_employee_for_a_retired_account_gets_no_second_hold()
    {
        var h = await SeededAsync();
        var first = await h.ActiveAccountAsync(H.Iban1);
        var (run, _) = await h.RunLineAsync(first);
        Assert.Equal(1, (await h.RetireAsync(first)).HoldsPlaced);

        // The same run's line is then pinned to a second account (a recalculation), which is retired too: the run's hold is still there.
        var second = await h.ActiveAccountAsync(H.Iban2);
        await using (var db = h.Db())
        {
            var line = await db.SalaryLines.SingleAsync(l => l.PayrollRunId == run);
            line.BankAccountId = second;
            await db.SaveChangesAsync();
        }

        var result = await h.RetireAsync(second);

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, result.HoldsPlaced);
        Assert.Single(await h.HoldsAsync());
    }

    // --- the scope: an employee or an account outside it is 404 ---

    [Fact]
    public async Task Outside_the_callers_branch_the_employee_is_not_found_and_nothing_changes()
    {
        var h = await SeededAsync();
        var foreign = H.Restricted(H.ForeignUnit);
        var active = await h.ActiveAccountAsync(H.Iban1);
        var pending = (await h.RegisterAsync(H.Iban2)).AccountId!.Value;

        var register = await h.RegisterAsync(H.Iban2, scope: foreign);
        Assert.Equal(BankAccountFailure.NotFound, register.Failure);
        Assert.Equal(404, register.StatusCode);
        Assert.Equal(BankAccountMessages.EmployeeNotFound, register.Error);

        foreach (var refused in new[]
                 {
                     await h.VerifyAsync(pending, scope: foreign),
                     await h.RejectAsync(pending, scope: foreign),
                     await h.RetireAsync(active, scope: foreign),
                 })
        {
            Assert.Equal(BankAccountFailure.NotFound, refused.Failure);
            Assert.Equal(404, refused.StatusCode);
            Assert.Null(refused.AccountId);          // nothing of the account is revealed
            Assert.Null(refused.Status);
        }

        Assert.Equal(BankAccountStatus.Active, (await h.AccountAsync(active)).Status);
        await AssertUnchangedPendingAsync(h, pending);
    }

    [Fact]
    public async Task Inside_the_callers_branch_and_for_a_central_user_the_acts_are_allowed()
    {
        var h = await SeededAsync();
        var own = H.Restricted(H.Unit);

        var registered = await h.RegisterAsync(scope: own);
        Assert.True(registered.Success, registered.Error);
        Assert.True((await h.VerifyAsync(registered.AccountId!.Value, scope: own)).Success);

        // A central user (no scope) reaches the employee of any unit.
        Assert.True((await h.RegisterAsync(H.Iban2, employee: H.OtherEmployee, scope: null)).Success);
    }

    [Fact]
    public async Task An_employee_with_no_unit_is_central_and_a_restricted_caller_never_reaches_him()
    {
        var h = await SeededAsync();
        var central = Guid.NewGuid();
        var inactiveOnly = Guid.NewGuid();
        await using (var db = h.Db())
        {
            db.AddRange(H.Profile(central, unit: null), H.Profile(inactiveOnly, H.Unit, active: false));
            await db.SaveChangesAsync();
        }

        Assert.Equal(404, (await h.RegisterAsync(employee: central, scope: H.Restricted(H.Unit))).StatusCode);
        Assert.Equal(404, (await h.RegisterAsync(employee: inactiveOnly, scope: H.Restricted(H.Unit))).StatusCode);
        Assert.True((await h.RegisterAsync(employee: central)).Success);
        Assert.True((await h.RegisterAsync(H.Iban2, employee: inactiveOnly)).Success);
    }

    [Fact]
    public async Task An_employee_payroll_does_not_know_is_not_found()
    {
        var h = await SeededAsync();

        var result = await h.RegisterAsync(employee: Guid.NewGuid());
        var empty = await h.RegisterAsync(employee: Guid.Empty);

        Assert.Equal(404, result.StatusCode);
        Assert.Equal(404, empty.StatusCode);
    }

    [Fact]
    public async Task An_account_is_reached_only_through_its_own_employee()
    {
        var h = await SeededAsync();
        var pending = (await h.RegisterAsync()).AccountId!.Value;

        var result = await h.VerifyAsync(pending, employee: H.OtherEmployee);

        Assert.Equal(404, result.StatusCode);
        Assert.Equal(BankAccountMessages.AccountNotFound, result.Error);
        await AssertUnchangedPendingAsync(h, pending);
        Assert.Equal(404, (await h.VerifyAsync(Guid.NewGuid())).StatusCode);
    }

    // --- the number never leaves ---

    [Fact]
    public void The_number_and_the_holders_name_are_redacted_in_the_audit_log()
    {
        foreach (var property in new[] { nameof(EmployeeBankAccount.AccountNumber), nameof(EmployeeBankAccount.HolderNameAr) })
        {
            Assert.NotNull(typeof(EmployeeBankAccount).GetProperty(property)!.GetCustomAttribute<AuditRedactAttribute>());
        }
    }

    [Fact]
    public async Task No_answer_carries_the_number()
    {
        var h = await SeededAsync();
        var wrong = H.Iban1[..3] + (H.Iban1[3] == '9' ? '8' : (char)(H.Iban1[3] + 1)) + H.Iban1[4..];
        var active = await h.ActiveAccountAsync(H.Iban1);
        var pending = await h.RegisterAsync(H.Iban2);

        var answers = new[]
        {
            await h.RegisterAsync(wrong, employee: H.OtherEmployee),
            await h.RegisterAsync(H.Iban1),                                  // already in force
            await h.RegisterAsync(H.Iban2),                                  // a pending one exists
            await h.VerifyAsync(pending.AccountId!.Value, actor: H.Processor),
            await h.VerifyAsync(pending.AccountId.Value, reference: null),
            await h.RetireAsync(active, reason: null),
        };

        Assert.All(answers, a =>
        {
            Assert.False(a.Success);
            Assert.DoesNotContain(H.Iban1, a.Error);
            Assert.DoesNotContain(H.Iban2, a.Error);
            Assert.DoesNotContain(wrong, a.Error);
        });
        Assert.Equal("••••••••••••••••••••••••" + H.Iban1[^4..], (await h.AccountAsync(active)).MaskedNumber);
    }

    [Fact]
    public void Every_message_is_arabic()
    {
        var messages = typeof(BankAccountMessages).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.True(messages.Count >= 20);
        Assert.All(messages, m => Assert.Contains(m, c => c is >= '؀' and <= 'ۿ'));
    }

    private static async Task AssertUnchangedPendingAsync(H h, Guid accountId)
    {
        var account = await h.AccountAsync(accountId);
        Assert.Equal(BankAccountStatus.PendingVerification, account.Status);
        Assert.Null(account.VerifiedBy);
        Assert.Null(account.VerifiedAt);
        Assert.Null(account.VerificationReference);
        Assert.Null(account.RejectedBy);
        Assert.Null(account.RejectionReason);
        Assert.Null(account.RetiredAt);
    }
}
