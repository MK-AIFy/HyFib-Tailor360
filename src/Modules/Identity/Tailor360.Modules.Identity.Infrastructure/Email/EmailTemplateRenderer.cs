using System.Text;
using Tailor360.Platform.Abstractions.Ports;
using Tailor360.Platform.Abstractions.Results;

namespace Tailor360.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Renders a message from the reviewed catalogue by substituting <c>{placeholder}</c> names.
/// </summary>
/// <remarks>
/// The substitution is deliberately simple and deliberately strict. Simple, because a template language
/// with conditionals in it becomes a place where message logic hides; strict, because a placeholder
/// left unsubstituted is a bug that reaches a person's inbox looking like a scam. A template whose
/// placeholders are not all supplied fails to render and nothing is sent.
/// <para>
/// Substitution runs once over the template. A value is never re-scanned for placeholders, so a
/// display name that happens to contain <c>{link}</c> stays those six characters and cannot pull the
/// recovery link into a field it does not belong in.
/// </para>
/// </remarks>
public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    /// <summary>The failure a template key nobody has written reports.</summary>
    public static Error UnknownTemplate { get; } = Error.NotFound(
        "identity.email-template-unknown",
        "No message of that kind is defined in this deployment.");

    /// <summary>The failure an unsubstituted placeholder reports.</summary>
    public static Error PlaceholderNotSupplied { get; } = Error.Validation(
        "identity.email-placeholder-not-supplied",
        "The message could not be completed because a value it needs was not supplied.",
        "values");

    /// <inheritdoc />
    public Result<EmailMessage> Render(EmailTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!EmailTemplateCatalogue.Templates.TryGetValue(request.TemplateKey, out var byLocale))
        {
            return Result.Failure<EmailMessage>(UnknownTemplate);
        }

        var template = Resolve(byLocale, request.Locale);
        var values = request.Values ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var subject = Substitute(template.Subject, values);
        var body = Substitute(template.Body, values);

        if (subject is null || body is null)
        {
            return Result.Failure<EmailMessage>(PlaceholderNotSupplied);
        }

        return Result.Success(new EmailMessage(
            request.To, subject, body, HtmlBody: null, Tag: request.TemplateKey));
    }

    /// <summary>
    /// Picks the wording: the exact language tag, then the language without its region, then the
    /// fallback. Falling back is right even when it feels wrong — a message in the wrong language still
    /// reaches the person and tells them their password changed, and one that was never sent does not.
    /// </summary>
    private static EmailTemplate Resolve(
        IReadOnlyDictionary<string, EmailTemplate> byLocale,
        string? locale)
    {
        if (!string.IsNullOrWhiteSpace(locale))
        {
            if (byLocale.TryGetValue(locale, out var exact))
            {
                return exact;
            }

            var separator = locale.IndexOf('-', StringComparison.Ordinal);
            if (separator > 0 && byLocale.TryGetValue(locale[..separator], out var language))
            {
                return language;
            }
        }

        return byLocale[EmailTemplateCatalogue.FallbackLocale];
    }

    /// <summary>
    /// Replaces every <c>{name}</c> with its supplied value, once, left to right. Returns
    /// <see langword="null"/> when a placeholder has no value, which is what stops a half-written
    /// message being sent.
    /// </summary>
    private static string? Substitute(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = new StringBuilder(template.Length + 64);
        var position = 0;

        while (position < template.Length)
        {
            var open = template.IndexOf('{', position);
            if (open < 0)
            {
                rendered.Append(template, position, template.Length - position);
                break;
            }

            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                rendered.Append(template, position, template.Length - position);
                break;
            }

            rendered.Append(template, position, open - position);

            var name = template[(open + 1)..close];
            if (!values.TryGetValue(name, out var value))
            {
                return null;
            }

            rendered.Append(value);
            position = close + 1;
        }

        return rendered.ToString();
    }

    /// <summary>The language tags this renderer has wording for, for a configuration report.</summary>
    public static IReadOnlyCollection<string> LocalesFor(string templateKey)
        => EmailTemplateCatalogue.Templates.TryGetValue(templateKey, out var byLocale)
            ? [.. byLocale.Keys.OrderBy(key => key, StringComparer.Ordinal)]
            : [];

    /// <summary>The template keys this deployment can render.</summary>
    public static IReadOnlyCollection<string> TemplateKeys
        => [.. EmailTemplateCatalogue.Templates.Keys.OrderBy(key => key, StringComparer.Ordinal)];
}
