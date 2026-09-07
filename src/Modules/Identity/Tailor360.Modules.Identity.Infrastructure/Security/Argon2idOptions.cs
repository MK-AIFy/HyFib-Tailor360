using System.ComponentModel.DataAnnotations;

namespace Tailor360.Modules.Identity.Infrastructure.Security;

/// <summary>
/// The Argon2id work factors. They are configuration rather than constants because the right values
/// depend on the machine: the point of a memory-hard function is to cost an attacker's hardware more
/// than it costs this server, and a shop running on a small virtual machine cannot afford what a large
/// one can.
/// </summary>
/// <remarks>
/// The defaults are the OWASP minimum for Argon2id — 19 MiB of memory, two passes, one lane — which a
/// modern server computes in a few tens of milliseconds. Raising them later needs no migration: each
/// stored hash carries the parameters it was made with, so the verifier reports that a rehash is due
/// and the next successful sign-in quietly upgrades that account.
/// </remarks>
public sealed class Argon2idOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Identity:PasswordHashing";

    /// <summary>Memory used per hash, in kibibytes.</summary>
    [Range(8192, 1_048_576)]
    public int MemoryKibibytes { get; set; } = 19_456;

    /// <summary>How many passes are made over that memory.</summary>
    [Range(1, 10)]
    public int Iterations { get; set; } = 2;

    /// <summary>How many lanes are computed in parallel.</summary>
    [Range(1, 16)]
    public int DegreeOfParallelism { get; set; } = 1;

    /// <summary>Salt length in bytes.</summary>
    [Range(16, 64)]
    public int SaltLength { get; set; } = 16;

    /// <summary>Derived key length in bytes.</summary>
    [Range(16, 64)]
    public int HashLength { get; set; } = 32;
}
