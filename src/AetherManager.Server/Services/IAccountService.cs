using AetherManager.Server.Models;

namespace AetherManager.Server.Services;

internal interface IAccountService
{
    Task InitializeAsync(CancellationToken token);
    Task<AccountResponse> BootstrapAsync(CreateAccountRequest request, CancellationToken token);
    Task<(SessionResponse Response, string Cookie)?> LoginAsync(LoginRequest request, CancellationToken token);
    Task<SessionResponse?> ValidateAsync(string? cookie, CancellationToken token);
    Task RevokeAsync(Guid sessionId, string actor, CancellationToken token);
    Task<AccountResponse[]> GetAccountsAsync(CancellationToken token);
    Task<AccountResponse> CreateAsync(CreateAccountRequest request, string actor, CancellationToken token);
    Task<AccountResponse> UpdateAsync(Guid id, UpdateAccountRequest request, string actor, CancellationToken token);
    Task<SessionMetadataResponse[]> GetSessionsAsync(CancellationToken token);
    Task<AccountAuditResponse[]> GetAuditAsync(CancellationToken token);
    Task RecordAdministrativeEventAsync(string actor, string message, CancellationToken token);
}
