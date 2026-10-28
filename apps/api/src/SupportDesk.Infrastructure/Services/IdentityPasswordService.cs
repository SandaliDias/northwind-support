using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using SupportDesk.Application.Abstractions;
using SupportDesk.Domain.Aggregates.Agents;
namespace SupportDesk.Infrastructure.Services;

/// <summary>Identity V3 PBKDF2 includes a random per-password salt and versioned work factor.</summary>
public sealed class IdentityPasswordService : IPasswordService
{
    private readonly PasswordHasher<Agent> _hasher = new();
    private readonly Agent _dummy = new("Unknown account", "unknown@invalid.local", 1, DateTime.UnixEpoch);
    private readonly string _dummyHash;

    public IdentityPasswordService() =>
        _dummyHash = _hasher.HashPassword(_dummy, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public string Hash(Agent agent, string password) => _hasher.HashPassword(agent, password);

    public PasswordCheck Verify(Agent? agent, string password)
    {
        PasswordVerificationResult result;
        try { result = _hasher.VerifyHashedPassword(agent ?? _dummy, agent?.PasswordHash ?? _dummyHash, password); }
        catch (FormatException) { return PasswordCheck.Failed; }
        if (agent?.PasswordHash is null) return PasswordCheck.Failed;
        return result switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.RehashNeeded,
            _ => PasswordCheck.Failed
        };
    }
}
