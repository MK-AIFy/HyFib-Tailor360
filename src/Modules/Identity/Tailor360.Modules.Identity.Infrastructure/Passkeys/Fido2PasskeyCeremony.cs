using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Exceptions;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailor360.Modules.Identity.Application.Abstractions;
using Tailor360.Modules.Identity.Application.Options;
using Tailor360.Modules.Identity.Application.Passkeys;
using Tailor360.Modules.Identity.Infrastructure.Persistence;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Infrastructure.Passkeys;

/// <summary>
/// Runs the WebAuthn ceremonies with the FIDO2 library, and is the only type in the solution that
/// names it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Discoverable credentials are required.</b> A resident key is what lets someone sign in by
/// touching their key with no name typed at all, which is the whole reason this is worth having at a
/// counter; the assertion here offers no allowed-credential list, so a credential that is not
/// discoverable could never be chosen. Requiring it at registration is therefore honest — the
/// alternative is enrolling a key that appears to work and then cannot be used.
/// </para>
/// <para>
/// <b>Every failure reports the same thing.</b> The library distinguishes a wrong origin from a bad
/// signature from a failed attestation, and a caller learns none of it: the distinctions are useful to
/// an attacker probing a relying party and to nobody standing at a counter. The reason is logged, where
/// it is readable by an operator during an incident.
/// </para>
/// <para>
/// Attestation is not requested. Verifying which model of authenticator someone owns needs the FIDO
/// metadata service, adds an outbound dependency to the sign-in path, and answers a question this shop
/// does not have — it would matter if a policy said "only these certified keys", which is a decision
/// nobody has taken.
/// </para>
/// </remarks>
/// <param name="context">The module's context, for the cross-account uniqueness check.</param>
/// <param name="ceremonies">Where a started ceremony waits for its answer.</param>
/// <param name="options">The relying party.</param>
/// <param name="logger">Logger. Never receives a challenge, a signature or a public key.</param>
public sealed class Fido2PasskeyCeremony(
    IdentityDbContext context,
    PasskeyCeremonyStore ceremonies,
    IOptions<PasskeyOptions> options,
    ILogger<Fido2PasskeyCeremony> logger) : IPasskeyCeremony
{
    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    private readonly PasskeyOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public bool IsAvailable => _options.IsConfigured;

    /// <inheritdoc />
    public Result<PasskeyChallenge> BeginRegistration(
        PasskeyUserDescriptor user,
        IReadOnlyList<byte[]> excludeCredentialIds,
        Guid? boundSessionId)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(excludeCredentialIds);

        if (!IsAvailable)
        {
            return Result.Failure<PasskeyChallenge>(PasskeyErrors.Unavailable);
        }

        var created = Library().RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = user.UserId.ToByteArray(),
                Name = user.UserName,
                DisplayName = user.DisplayName,
            },
            ExcludeCredentials = [.. excludeCredentialIds.Select(id => new PublicKeyCredentialDescriptor(id))],
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required,
            },
            AttestationPreference = AttestationConveyancePreference.None,
        });

        return Store(created.ToJson(), boundSessionId);
    }

    /// <inheritdoc />
    public async Task<Result<PasskeyRegistration>> CompleteRegistrationAsync(
        string? ceremonyId,
        string? credentialJson,
        Guid? boundSessionId,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return Result.Failure<PasskeyRegistration>(PasskeyErrors.Unavailable);
        }

        if (ceremonies.Consume(ceremonyId, boundSessionId) is not { } optionsJson)
        {
            return Result.Failure<PasskeyRegistration>(PasskeyErrors.CeremonyNotValid);
        }

        if (Read<AuthenticatorAttestationRawResponse>(credentialJson) is not { } response)
        {
            return Result.Failure<PasskeyRegistration>(PasskeyErrors.ResponseNotReadable);
        }

        try
        {
            var registered = await Library().MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = response,
                    OriginalOptions = CredentialCreateOptions.FromJson(optionsJson),
                    IsCredentialIdUniqueToUserCallback = IsCredentialUnusedAsync,
                },
                cancellationToken);

            return Result.Success(new PasskeyRegistration(
                registered.Id,
                registered.PublicKey,
                registered.AaGuid,
                registered.SignCount,
                Describe(registered.Transports),
                registered.IsBackupEligible,
                registered.IsBackedUp));
        }
        catch (Fido2VerificationException exception)
        {
            IdentityPasskeyLog.RegistrationRejected(logger, exception.Code);
            return Result.Failure<PasskeyRegistration>(PasskeyErrors.VerificationFailed);
        }
    }

    /// <inheritdoc />
    public Result<PasskeyChallenge> BeginAssertion(IReadOnlyList<byte[]> allowCredentialIds, Guid? boundSessionId)
    {
        ArgumentNullException.ThrowIfNull(allowCredentialIds);

        if (!IsAvailable)
        {
            return Result.Failure<PasskeyChallenge>(PasskeyErrors.Unavailable);
        }

        var assertion = Library().GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [.. allowCredentialIds.Select(id => new PublicKeyCredentialDescriptor(id))],
            UserVerification = UserVerificationRequirement.Required,
        });

        return Store(assertion.ToJson(), boundSessionId);
    }

    /// <inheritdoc />
    public Result<byte[]> ReadAssertedCredentialId(string? credentialJson)
    {
        var response = Read<AuthenticatorAssertionRawResponse>(credentialJson);

        return response?.RawId is { Length: > 0 } rawId
            ? Result.Success(rawId)
            : Result.Failure<byte[]>(PasskeyErrors.ResponseNotReadable);
    }

    /// <inheritdoc />
    public async Task<Result<PasskeyAssertion>> CompleteAssertionAsync(
        string? ceremonyId,
        string? credentialJson,
        StoredPasskey stored,
        Guid? boundSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!IsAvailable)
        {
            return Result.Failure<PasskeyAssertion>(PasskeyErrors.Unavailable);
        }

        if (ceremonies.Consume(ceremonyId, boundSessionId) is not { } optionsJson)
        {
            return Result.Failure<PasskeyAssertion>(PasskeyErrors.CeremonyNotValid);
        }

        if (Read<AuthenticatorAssertionRawResponse>(credentialJson) is not { } response)
        {
            return Result.Failure<PasskeyAssertion>(PasskeyErrors.ResponseNotReadable);
        }

        var expectedHandle = stored.UserHandle.ToByteArray();

        try
        {
            var verified = await Library().MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = response,
                    OriginalOptions = AssertionOptions.FromJson(optionsJson),
                    StoredPublicKey = stored.PublicKey,
                    StoredSignatureCounter = (uint)stored.SignatureCounter,
                    IsUserHandleOwnerOfCredentialIdCallback = (parameters, _) =>
                        Task.FromResult(parameters.UserHandle.AsSpan().SequenceEqual(expectedHandle)),
                },
                cancellationToken);

            return Result.Success(new PasskeyAssertion(
                verified.CredentialId, verified.SignCount, verified.IsBackedUp));
        }
        catch (Fido2VerificationException exception)
        {
            IdentityPasskeyLog.AssertionRejected(logger, exception.Code);
            return Result.Failure<PasskeyAssertion>(PasskeyErrors.VerificationFailed);
        }
    }

    private Result<PasskeyChallenge> Store(string optionsJson, Guid? boundSessionId)
    {
        var (ceremonyId, expiresAt) = ceremonies.Start(
            optionsJson, boundSessionId, _options.ChallengeLifetime);

        return Result.Success(new PasskeyChallenge(ceremonyId, optionsJson, expiresAt));
    }

    private Fido2 Library() => new(new Fido2Configuration
    {
        ServerDomain = _options.RelyingPartyId,
        ServerName = _options.RelyingPartyName,
        Origins = new HashSet<string>(_options.Origins, StringComparer.Ordinal),
        ChallengeSize = _options.ChallengeBytes,
        Timeout = (uint)_options.ChallengeLifetime.TotalMilliseconds,
    });

    private async Task<bool> IsCredentialUnusedAsync(
        IsCredentialIdUniqueToUserParams parameters,
        CancellationToken cancellationToken)
    {
        var credentialId = parameters.CredentialId;

        return !await context.PasskeyCredentials
            .AnyAsync(passkey => passkey.CredentialId == credentialId, cancellationToken);
    }

    private static TResponse? Read<TResponse>(string? json)
        where TResponse : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(json, ResponseJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Describe(AuthenticatorTransport[]? transports)
        => transports is { Length: > 0 }
            ? string.Join(',', transports.Select(transport => transport.ToString().ToLowerInvariant()))
            : null;
}

/// <summary>
/// Source-generated log messages for the passkey ceremonies. They carry the library's failure code and
/// nothing else — no challenge, no signature, no credential identifier.
/// </summary>
internal static partial class IdentityPasskeyLog
{
    [LoggerMessage(EventId = 2340, Level = LogLevel.Information,
        Message = "A passkey registration did not verify ({Reason}). The caller was told only that it failed.")]
    public static partial void RegistrationRejected(ILogger logger, Fido2ErrorCode reason);

    [LoggerMessage(EventId = 2341, Level = LogLevel.Information,
        Message = "A passkey assertion did not verify ({Reason}). The caller was told only that it failed.")]
    public static partial void AssertionRejected(ILogger logger, Fido2ErrorCode reason);
}
