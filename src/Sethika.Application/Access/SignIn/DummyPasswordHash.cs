using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Sethika.Domain.Access;

namespace Sethika.Application.Access.SignIn;

/// <summary>
/// A password hash that belongs to no account, verified against when a login matches none.
/// </summary>
/// <remarks>
/// Verifying a password is the slow part of sign-in - 210 000 PBKDF2 iterations. Skipping it for an
/// unknown login makes that answer arrive a couple of hundred milliseconds sooner than a wrong
/// password, and a stopwatch then tells anyone which phone numbers have accounts, however carefully
/// the response bodies match.
///
/// Made once per process by the configured hasher, so it carries the same iteration count as real
/// hashes and costs the same to verify. The password inside is random and discarded, so nothing
/// verifies against it.
/// </remarks>
public sealed class DummyPasswordHash
{
    /// <summary>Hashes a random, discarded password with <paramref name="hasher"/>.</summary>
    public DummyPasswordHash(IPasswordHasher<User> hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        Value = hasher.HashPassword(new User(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    /// <summary>The hash, in Identity's format.</summary>
    public string Value { get; }
}
