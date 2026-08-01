using HRManagement.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults.Repositories;

public class EfUnitOfWork<TContext> : IUnitOfWork where TContext : DbContext
{
    private readonly TContext _context;

    public EfUnitOfWork(TContext context) => _context = context;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);

    public void Dispose() => _context.Dispose();
}
