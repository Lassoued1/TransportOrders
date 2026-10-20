using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TransportOrders.IntegrationTests;

// TestServer has no connection-level Negotiate support. Replace only the handler in tests.
public sealed class TestWindowsAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var name = Request.Headers["Test-User"].ToString();
        if (string.IsNullOrEmpty(name)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        claims.AddRange(Request.Headers["Test-Groups"].ToString().Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(group => new Claim(ClaimTypes.Role, group)));
        return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
    }
}

public static class TestWindowsAuthenticationSetup
{
    public static void UseTestWindowsAuthentication(this IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services => services.PostConfigure<AuthenticationOptions>(options =>
            options.SchemeMap[NegotiateDefaults.AuthenticationScheme].HandlerType = typeof(TestWindowsAuthentication)));
}
