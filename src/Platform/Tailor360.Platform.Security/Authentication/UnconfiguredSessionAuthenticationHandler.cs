using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tailor360.Platform.Security.Authentication;

/// <summary>
/// The placeholder session handler registered until issue #23 delivers real sessions. It authenticates
/// nobody, which keeps the request pipeline complete and correctly ordered without creating a period in
/// which a half-built scheme could accept something. Every protected endpoint therefore answers 401
/// until sessions exist, which is the intended behaviour for the scaffold.
/// </summary>
/// <param name="options">Scheme options.</param>
/// <param name="logger">Logger factory.</param>
/// <param name="encoder">URL encoder.</param>
public sealed class UnconfiguredSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(AuthenticateResult.NoResult());
}
