using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Services;
using Microsoft.Extensions.Caching.Memory;

namespace HRManagement.UnitTests;

internal static class WorkflowInboxTestSupport
{
    internal static EstablishmentScopeService CreateEstablishmentScopeService(HRCoreDbContext db) =>
        new(db, CreateOrgScopeService(db));

    internal static OrgScopeService CreateOrgScopeService(HRCoreDbContext db) =>
        new(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions())));
}
