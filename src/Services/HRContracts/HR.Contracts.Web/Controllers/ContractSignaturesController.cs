using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Web.ViewModels;

namespace HR.Contracts.Web.Controllers;

/// <summary>
/// Form-based contract signatures (NOT legal digital signatures).
/// These are UI/form capture signatures only — they do not carry
/// cryptographic non-repudiation or X.509 binding.
///
/// For legally binding digital signatures per SIG-001, use
/// <c>HR.Core.Web.Controllers.SignaturesController</c> with
/// <c>ISignatureService</c> and <c>SignatureRecord</c>.
/// </summary>
public class ContractSignaturesController : Controller
{
    private readonly ContractsDbContext _db;

    public ContractSignaturesController(ContractsDbContext db) => _db = db;

    public async Task<IActionResult> Create(Guid contractId)
    {
        var c = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == contractId);
        if (c == null) return NotFound();

        ViewBag.ContractId = contractId;
        return View(new ContractSignatureCreateViewModel
        {
            ContractId = contractId,
            SignedAt = DateTime.UtcNow
        });
    }

    /// <remarks>
    /// This action captures a form-based (non-legal) signature.
    /// To additionally create a legal <c>SignatureRecord</c>, the caller should
    /// invoke <c>ISignatureService.SignAsync</c> with the canonical payload hash
    /// of the contract and persist the result in the SignatureRecords table.
    /// </remarks>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ContractSignatureCreateViewModel vm)
    {
        if (!await _db.Contracts.AnyAsync(c => c.Id == vm.ContractId))
            return NotFound();

        var signerClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(signerClaim, out var signerId))
        {
            ModelState.AddModelError(string.Empty, "تعذر تحديد حساب الموقّع الحالي.");
            return View(vm);
        }

        if (!ModelState.IsValid) return View(vm);

        _db.ContractSignatures.Add(new ContractSignature
        {
            ContractId = vm.ContractId,
            SignedBy = signerId,
            SignedAt = vm.SignedAt,
            SignatureType = vm.SignatureType,
            SignatureData = string.IsNullOrWhiteSpace(vm.SignatureData) ? null : vm.SignatureData.Trim()
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم إضافة التوقيع";
        return RedirectToAction("Details", "Contracts", new { id = vm.ContractId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var s = await _db.ContractSignatures.FindAsync(id);
        if (s == null) return NotFound();

        var contractId = s.ContractId;
        _db.ContractSignatures.Remove(s);
        await _db.SaveChangesAsync();
        TempData["Success"] = "تم حذف التوقيع";
        return RedirectToAction("Details", "Contracts", new { id = contractId });
    }
}
