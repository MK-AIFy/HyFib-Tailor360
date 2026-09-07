using System.ComponentModel.DataAnnotations;

namespace Tailor360.Web.Configuration;

/// <summary>
/// Which client builds this server will answer, and what it tells a client about itself.
/// </summary>
/// <remarks>
/// <para>
/// The handshake is the one in <c>docs/architecture/conventions.md</c> section 5.4: every request from
/// the progressive web application carries <c>X-Client-Version</c>, the server refuses one below
/// <see cref="MinimumClientVersion"/> with <c>426 Upgrade Required</c>, and the client turns that into
/// an update prompt rather than an error.
/// </para>
/// <para>
/// <b>The minimum is raised in the release after the change that requires it</b>, never in the same one.
/// A deployment replaces the assets a browser will fetch <em>next</em>; the tab already open still holds
/// the previous build, and raising the minimum in the same release refuses that tab before it has any
/// way to learn that a newer one exists. One release of lag is what turns "you are signed out and
/// nothing works" into "an update is ready".
/// </para>
/// </remarks>
public sealed class ClientCompatibilityOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "ClientCompatibility";

    /// <summary>
    /// The oldest client build this server answers, as a semantic version. Empty — the default — accepts
    /// every version, which is what a development machine and a first deployment want: nothing has been
    /// released yet, so nothing can be too old.
    /// </summary>
    [Required(AllowEmptyStrings = true)]
    public string MinimumClientVersion { get; set; } = string.Empty;
}
