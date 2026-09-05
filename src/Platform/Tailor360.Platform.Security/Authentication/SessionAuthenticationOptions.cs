using System.ComponentModel.DataAnnotations;

namespace Tailor360.Platform.Security.Authentication;

/// <summary>
/// How long a session lives and how often its inactivity deadline is written back. The defaults are
/// the ones issue #23 sets: thirty minutes of inactivity, twelve hours in total, with the client warned
/// two minutes before the inactivity deadline.
/// </summary>
/// <remarks>
/// The two timeouts do different jobs and neither replaces the other. The inactivity timeout ends a
/// session someone walked away from — a counter tablet in a shop is the case that matters. The
/// absolute lifetime ends a session however busy it is, which is what bounds the value of a stolen
/// cookie: without it, a ticket that is used steadily never expires at all.
/// </remarks>
public sealed class SessionAuthenticationOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Security:Session";

    /// <summary>How long a session may sit unused before it ends.</summary>
    [Range(typeof(TimeSpan), "00:05:00", "08:00:00")]
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>The longest a session may live, however active it is.</summary>
    [Range(typeof(TimeSpan), "00:30:00", "1.00:00:00")]
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// How long before the inactivity deadline the client is told the session is about to end, so that
    /// the warning dialog appears in time for someone to answer it (WCAG 2.2.1).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:30", "00:10:00")]
    public TimeSpan ExpiryWarningLead { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The shortest interval between writes that slide the inactivity deadline. A progressive web
    /// application issues several requests per interaction, and writing a row on every one of them
    /// would make the session table the busiest table in the system for no benefit; the deadline only
    /// has to be accurate to within this interval.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan SlidingWriteInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// True when the combination can behave as described: the inactivity timeout has to fit inside the
    /// absolute lifetime, the warning has to arrive before the deadline it warns about, and the
    /// write-back interval has to be shorter than the timeout it maintains.
    /// </summary>
    public bool IsUsable
        => IdleTimeout <= AbsoluteLifetime
            && ExpiryWarningLead < IdleTimeout
            && SlidingWriteInterval < IdleTimeout;
}
