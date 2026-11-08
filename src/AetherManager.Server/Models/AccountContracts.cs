using System.Text.Json.Serialization;

namespace AetherManager.Server.Models;

/// <summary>Supported local management roles.</summary>
public enum AccountRole
{
    /// <summary>Manage users, policies, enrollment and all confirmed catalog operations.</summary>
    Administrator,
    /// <summary>Read fleet data and queue fixed read-only diagnostics.</summary>
    Operator,
    /// <summary>Read fleet, reports, jobs and audit without mutations.</summary>
    Reader
}
/// <summary>Creates a local account; passwords are never returned.</summary>
public sealed record CreateAccountRequest([property: JsonRequired] string Username, [property: JsonRequired] string Password,
    [property: JsonRequired] AccountRole Role);
/// <summary>Updates an account and revokes its current sessions.</summary>
public sealed record UpdateAccountRequest([property: JsonRequired] AccountRole Role, [property: JsonRequired] bool Disabled, string? Password = null);
/// <summary>Authenticates a local account over HTTPS.</summary>
public sealed record LoginRequest([property: JsonRequired] string Username, [property: JsonRequired] string Password);
/// <summary>Public account information without credential material.</summary>
public sealed record AccountResponse(Guid Id, string Username, AccountRole Role, bool Disabled, DateTimeOffset CreatedAt);
/// <summary>Current signed-in identity, absolute expiry and anti-forgery token.</summary>
public sealed record SessionResponse(Guid SessionId, AccountResponse Account, DateTimeOffset ExpiresAt, string CsrfToken);
/// <summary>Revocable session metadata; no authentication capability is exposed.</summary>
public sealed record SessionMetadataResponse(Guid Id, Guid AccountId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
/// <summary>Account lifecycle and authentication security events.</summary>
public sealed record AccountAuditResponse(DateTimeOffset At, string Actor, string Event, Guid? AccountId);
