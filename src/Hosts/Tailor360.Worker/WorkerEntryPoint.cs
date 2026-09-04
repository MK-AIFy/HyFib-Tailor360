namespace Tailor360.Worker;

/// <summary>
/// Anchor type identifying this assembly to test hosts, for the same reason as the web host's marker:
/// several executables here use top-level statements, whose generated entry points would collide.
/// </summary>
public sealed class WorkerEntryPoint;
