using Admin.Logistics.Domain.Entities;

namespace Admin.Logistics.Web.Services;

public sealed class VendorIndexFilter : AdminLogisticsPagedFilter
{
    public string? Q { get; init; }
    public string? Country { get; init; }
    public bool HasContactOnly { get; init; }
    public bool MissingContactOnly { get; init; }

    public IQueryable<Vendor> Apply(IQueryable<Vendor> query)
    {
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(x =>
                x.Name.Contains(term)
                || (x.ContactInfo != null && x.ContactInfo.Contains(term))
                || (x.Country != null && x.Country.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(Country))
            query = query.Where(x => x.Country == Country.Trim());

        if (HasContactOnly)
            query = query.Where(x => x.ContactInfo != null && x.ContactInfo != "");

        if (MissingContactOnly)
            query = query.Where(x => x.ContactInfo == null || x.ContactInfo == "");

        return query;
    }
}
