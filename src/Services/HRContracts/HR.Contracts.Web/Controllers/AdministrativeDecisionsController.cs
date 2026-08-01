using System.Security.Claims;

using HR.Contracts.Application.Features.AdministrativeDecisions;

using HR.Contracts.Application.Models;

using HR.Contracts.Infrastructure.Data;

using HRManagement.ServiceDefaults;

using Microsoft.AspNetCore.Mvc;

using HR.Contracts.Web.ViewModels;

using MediatR;

using Microsoft.EntityFrameworkCore;



namespace HR.Contracts.Web.Controllers;



public class AdministrativeDecisionsController : Controller

{

    private readonly IMediator _mediator;

    private readonly ContractsDbContext _db;

    private readonly IFileReferenceClient _fileRefs;



    public AdministrativeDecisionsController(

        IMediator mediator,

        ContractsDbContext db,

        IFileReferenceClient fileRefs)

    {

        _mediator = mediator;

        _db = db;

        _fileRefs = fileRefs;

    }



    public async Task<IActionResult> Index()

    {

        var items = await _mediator.Send(new GetAdministrativeDecisionsQuery());

        return View(items);

    }



    public IActionResult Create() => View(new AdministrativeDecisionViewModel());



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Create(AdministrativeDecisionViewModel vm)

    {

        if (!ModelState.IsValid) return View(vm);

        vm.IssuedBy = ServerAssignedIdentity.ResolveActorName(User);
        var id = await _mediator.Send(new CreateAdministrativeDecisionCommand(Map(vm)));

        if (vm.FileId.HasValue)

        {

            await _fileRefs.RegisterReferenceAsync(

                vm.FileId.Value,

                FileReferenceClientExtensions.BuildHolderKey(

                    HrFileServices.HrContracts, nameof(Domain.Entities.AdministrativeDecision), id, nameof(Domain.Entities.AdministrativeDecision.FileId)));

        }

        TempData["Success"] = "تم إضافة القرار";

        return RedirectToAction(nameof(Index));

    }



    public async Task<IActionResult> Edit(Guid id)

    {

        var e = await _mediator.Send(new GetAdministrativeDecisionByIdQuery(id));

        if (e == null) return NotFound();



        return View(new AdministrativeDecisionViewModel

        {

            Id = e.Id,

            Subject = e.Subject,

            DecisionNumber = e.DecisionNumber,

            IssuedBy = e.IssuedBy,

            IssuedDate = e.IssuedDate,

            RelatedEmployeeId = e.RelatedEmployeeId,

            FileId = e.FileId

        });

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Edit(Guid id, AdministrativeDecisionViewModel vm)

    {

        if (vm.Id != id) return BadRequest();

        if (!ModelState.IsValid) return View(vm);



        var entity = await _db.AdministrativeDecisions.FindAsync(id);

        var oldFileId = entity?.FileId;



        var updated = await _mediator.Send(new UpdateAdministrativeDecisionCommand(id, Map(vm)));

        if (!updated) return NotFound();



        if (entity is not null)

        {

            await EntityFileReferenceHelper.ApplyFileIdChangeAsync(

                _fileRefs,

                HrFileServices.HrContracts,

                nameof(Domain.Entities.AdministrativeDecision),

                id,

                nameof(Domain.Entities.AdministrativeDecision.FileId),

                oldFileId,

                vm.FileId);

        }



        TempData["Success"] = "تم التحديث";

        return RedirectToAction(nameof(Index));

    }



    [HttpPost]

    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Delete(Guid id)

    {

        var entity = await _db.AdministrativeDecisions.FindAsync(id);

        if (entity is not null)

        {

            await EntityFileReferenceHelper.ReleaseEntityFileAsync(

                _fileRefs,

                HrFileServices.HrContracts,

                nameof(Domain.Entities.AdministrativeDecision),

                entity.Id,

                nameof(Domain.Entities.AdministrativeDecision.FileId),

                entity.FileId);

        }



        var deleted = await _mediator.Send(new DeleteAdministrativeDecisionCommand(id));

        if (!deleted) return NotFound();

        TempData["Success"] = "تم الحذف";

        return RedirectToAction(nameof(Index));

    }



    private static AdministrativeDecisionDto Map(AdministrativeDecisionViewModel vm) =>

        new(vm.Id, vm.Subject, vm.DecisionNumber, vm.IssuedBy, vm.IssuedDate, vm.RelatedEmployeeId, vm.FileId);

}

