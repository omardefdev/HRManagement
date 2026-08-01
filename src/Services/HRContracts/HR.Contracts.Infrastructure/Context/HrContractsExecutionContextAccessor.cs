using HR.Contracts.Application.Abstractions;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Http;

namespace HR.Contracts.Infrastructure.Context;

public class HrContractsExecutionContextAccessor(IHttpContextAccessor httpContextAccessor) : IExecutionContextAccessor
{
    public string? CorrelationId
    {
        get
        {
            var flow = HrContractsExecutionContextFlow.GetCorrelationId();
            if (!string.IsNullOrWhiteSpace(flow))
            {
                return flow;
            }

            return CorrelationIdFlow.Get()
                   ?? httpContextAccessor.HttpContext?.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
                   ?? httpContextAccessor.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        }
    }

    public string SourceSystem =>
        HrContractsExecutionContextFlow.GetSourceSystem()
        ?? "HR.Contracts";
}
