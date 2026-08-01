using System.Data.Common;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// Sets PostgreSQL session variable <c>app.current_tenant</c> for RLS policies (TEN-002).
/// </summary>
public sealed class NpgsqlTenantSessionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public const string SessionVariable = "app.current_tenant";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetTenant(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetTenantAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void SetTenant(DbConnection connection)
    {
        if (connection is not NpgsqlConnection npgsql || !tenantContext.TenantId.HasValue)
            return;

        using var cmd = npgsql.CreateCommand();
        cmd.CommandText = $"SELECT set_config('{SessionVariable}', @tenant, false)";
        cmd.Parameters.AddWithValue("tenant", tenantContext.TenantId.Value.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    private async Task SetTenantAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is not NpgsqlConnection npgsql || !tenantContext.TenantId.HasValue)
            return;

        await using var cmd = npgsql.CreateCommand();
        cmd.CommandText = $"SELECT set_config('{SessionVariable}', @tenant, false)";
        cmd.Parameters.AddWithValue("tenant", tenantContext.TenantId.Value.ToString("D"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
