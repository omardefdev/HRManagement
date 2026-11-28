using System.ComponentModel.DataAnnotations;

namespace HR.Contracts.Domain.Enums;

/// <summary>PLAN-60 (G-SEP-01): who completes a clearance item, and when. Stored as text.</summary>
public enum SeparationStepCompletionMode
{
    /// <summary>HR completes it by hand, with evidence, before the execution.</summary>
    [Display(Name = "يدوي بدليل")]
    Manual,

    /// <summary>The service completes it itself (the decision, the contract's end at the execution).</summary>
    [Display(Name = "آلي")]
    System,

    /// <summary>The party's acknowledgement completes it, after the execution; never by hand.</summary>
    [Display(Name = "إقرار الطرف")]
    PartyAck
}
