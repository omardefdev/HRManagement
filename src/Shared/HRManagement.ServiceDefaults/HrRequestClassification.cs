using Microsoft.AspNetCore.Http;

namespace HRManagement.ServiceDefaults;

/// <summary>UX-004: distinguish browser MVC form posts from API/AJAX clients.</summary>
public static class HrRequestClassification
{
    public static bool WantsJsonResponse(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value ?? string.Empty;

        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(request.Headers.XRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            return true;

        var contentType = request.ContentType ?? string.Empty;
        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return true;

        if (request.Headers.TryGetValue("Accept", out var acceptValues))
        {
            foreach (var accept in acceptValues)
            {
                if (string.IsNullOrWhiteSpace(accept))
                    continue;

                if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                    && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (accept.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
