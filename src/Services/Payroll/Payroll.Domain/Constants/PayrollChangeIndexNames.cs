namespace Payroll.Domain.Constants;

/// <summary>PLAN-53: the names of the filtered unique indexes of the change requests, so a unique violation is told apart by name.</summary>
public static class PayrollChangeIndexNames
{
    /// <summary>D-53-3: one open salary change request (pending or scheduled) per employee and agency.</summary>
    public const string OneOpenPayProfileChange = "IX_PayProfileChangeRequests_OneOpen";

    /// <summary>One open reference change request per scale or grade.</summary>
    public const string OneOpenReferenceChange = "IX_PayrollReferenceChangeRequests_OneOpen";

    /// <summary>
    /// PLAN-55: one account in force and one account waiting for verification per employee and agency. One index over the status, so
    /// that a verification's two updates (the account in force retired, its replacement in force) are ordered by their values.
    /// </summary>
    public const string OneActiveOnePendingBankAccount = "IX_EmployeeBankAccounts_OneActiveOnePending";
}

/// <summary>PLAN-56: the names of the filtered unique indexes of a payroll run's life, so a unique violation is told apart by name.</summary>
public static class PayrollRunIndexNames
{
    /// <summary>
    /// One live regular run per agency and period (a reversed or cancelled run is not live). PLAN-56 limits it to regular runs: a
    /// correction has the period of its source, and the key of a final settlement is PLAN-64's.
    /// </summary>
    public const string LivePeriod = "IX_PayrollRuns_LivePeriod";

    /// <summary>One correction run not yet posted (nor reversed, nor cancelled) per source run.</summary>
    public const string OneOpenCorrectionPerSource = "IX_PayrollRuns_OneOpenCorrectionPerSource";

    /// <summary>One reversal request waiting for its decision per run.</summary>
    public const string OnePendingReversalRequest = "IX_PayrollReversalRequests_OnePending";

    /// <summary>One kept revision per rejected version of a run.</summary>
    public const string OneRevisionPerVersion = "IX_PayrollRunRevisions_Run_Revision";
}

/// <summary>PLAN-58: the names of the disbursement governance's unique indexes, so a refusal can be told from another database error.</summary>
public static class PayrollDisbursementIndexNames
{
    /// <summary>One bank file per sequence of a batch: two concurrent sendings make one file.</summary>
    public const string OneFilePerSequence = "IX_PaymentBatchFiles_Batch_Sequence";

    /// <summary>One pending four-eyes request per kind and target.</summary>
    public const string OnePendingDualControlRequest = "IX_PayrollDualControlRequests_OnePending";
}
