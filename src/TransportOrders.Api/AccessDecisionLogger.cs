using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace TransportOrders.Api;

public sealed class AccessDecisionLogger(ILogger<AccessDecisionLogger> logger) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (!result.Succeeded)
            logger.LogWarning("Operator access denied: {User} {Method} {Path}; authenticated {Authenticated}",
                context.User.Identity?.Name ?? "anonymous", context.Request.Method, context.Request.Path,
                context.User.Identity?.IsAuthenticated == true);
        await _default.HandleAsync(next, context, policy, result);
    }
}
