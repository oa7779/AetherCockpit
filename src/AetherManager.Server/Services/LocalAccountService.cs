using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AetherManager.Server.Models;
using Microsoft.AspNetCore.Identity;

namespace AetherManager.Server.Services;

// The data root must have the same protected ACLs as the existing device/token store.
// Opaque sessions deliberately do not survive a server restart.
internal sealed class LocalAccountService(string root, TimeProvider clock) : IAccountService, IDisposable
{
    private readonly string _path = Path.Combine(root, "accounts.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PasswordHasher<Account> _hasher = CreateHasher();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Attempts> _attempts = [];
    private State _state = new();
    private FileStream? _ownership;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _dummyHash = CreateHasher().HashPassword(
        new Account(Guid.Empty, "", "", AccountRole.Reader, true, default), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    public async Task InitializeAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        FileStream? ownership = null;
        try
        {
            if (_ownership is not null) throw new InvalidOperationException("Account service already initialized.");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            ownership = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(_path))
            {
                _state = JsonSerializer.Deserialize<State>(await File.ReadAllBytesAsync(_path, token), Json)
                    ?? throw new InvalidDataException("Invalid local account store.");
                if (_state.Accounts.Count > 1000 || _state.Accounts.Select(x => x.Id).Distinct().Count() != _state.Accounts.Count ||
                    _state.Accounts.Select(x => x.Username.ToUpperInvariant()).Distinct().Count() != _state.Accounts.Count ||
                    _state.Accounts.Any(x => !Enum.IsDefined(x.Role) || string.IsNullOrEmpty(x.PasswordHash)) ||
                    (!_state.Bootstrapped && _state.Accounts.Count != 0) ||
                    (_state.Bootstrapped && !_state.Accounts.Any(x => x.Role == AccountRole.Administrator && !x.Disabled)))
                    throw new InvalidDataException("Invalid local account state.");
            }
            _ownership = ownership;
            ownership = null;
        }
        finally { ownership?.Dispose(); _gate.Release(); }
    }

    public Task<AccountResponse> BootstrapAsync(CreateAccountRequest request, CancellationToken token) =>
        ChangeAsync(state =>
        {
            if (state.Bootstrapped || state.Accounts.Count != 0)
                throw new InvalidOperationException("Local account bootstrap has already completed.");
            if (request.Role != AccountRole.Administrator) throw new ArgumentException("Bootstrap requires an administrator.");
            var account = NewAccount(request);
            state.Accounts.Add(account);
            state.Bootstrapped = true;
            Audit(state, "legacy-bootstrap", "Initial administrator created", account.Id);
            return Public(account);
        }, token);

    public async Task<(SessionResponse Response, string Cookie)?> LoginAsync(LoginRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 64 ||
            request.Password is null || request.Password.Length > 256) throw new ArgumentException("Invalid login.");
        await _gate.WaitAsync(token);
        try
        {
            Prune();
            var account = _state.Accounts.FirstOrDefault(x => x.Username.Equals(request.Username.Trim(), StringComparison.OrdinalIgnoreCase));
            var now = clock.GetUtcNow();
            if (account is not null && _attempts.TryGetValue(account.Id, out var attempts) && attempts.LockedUntil > now)
                return null;
            var result = _hasher.VerifyHashedPassword(account!, account?.PasswordHash ?? _dummyHash, request.Password);
            if (account is null || account.Disabled || result == PasswordVerificationResult.Failed)
            {
                if (account is not null)
                {
                    var old = _attempts.GetValueOrDefault(account.Id);
                    var reset = old is null || old.WindowStart < now.AddMinutes(-10);
                    var failures = reset ? 1 : old!.Failures + 1;
                    _attempts[account.Id] = new(failures, reset ? now : old!.WindowStart,
                        failures >= 5 ? now.AddMinutes(10) : now);
                }
                var denied = CopyState();
                Audit(denied, "unauthenticated", "Login denied", account?.Id);
                await SaveAsync(denied, token);
                return null;
            }
            _attempts.Remove(account.Id);
            if (_sessions.Count >= 1024 || _sessions.Values.Count(x => x.AccountId == account.Id) >= 5)
                throw new InvalidOperationException("Session limit reached; revoke sessions or wait for expiry.");
            var capability = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var session = new Session(Guid.NewGuid(), account.Id, now, now.AddMinutes(30),
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            var next = CopyState();
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
                next.Accounts[next.Accounts.FindIndex(x => x.Id == account.Id)] = account with
                    { PasswordHash = _hasher.HashPassword(account, request.Password) };
            Audit(next, Actor(account), "Login succeeded", account.Id);
            await SaveAsync(next, token);
            _sessions.Add(Hash(capability), session);
            return (Response(session, account), capability);
        }
        finally { _gate.Release(); }
    }

    public async Task<SessionResponse?> ValidateAsync(string? cookie, CancellationToken token)
    {
        if (cookie is null || cookie.Length != 64) return null;
        await _gate.WaitAsync(token);
        try
        {
            Prune();
            if (!_sessions.TryGetValue(Hash(cookie), out var session)) return null;
            var account = _state.Accounts.FirstOrDefault(x => x.Id == session.AccountId && !x.Disabled);
            return account is null ? null : Response(session, account);
        }
        finally { _gate.Release(); }
    }

    public async Task RevokeAsync(Guid sessionId, string actor, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var key = _sessions.FirstOrDefault(x => x.Value.Id == sessionId).Key;
            if (key is null) return;
            var next = CopyState();
            Audit(next, actor, "Session revoked", _sessions[key].AccountId);
            await SaveAsync(next, token);
            _sessions.Remove(key);
        }
        finally { _gate.Release(); }
    }

    public Task<AccountResponse[]> GetAccountsAsync(CancellationToken token) =>
        ReadAsync(() => _state.Accounts.Select(Public).ToArray(), token);

    public Task<AccountResponse> CreateAsync(CreateAccountRequest request, string actor, CancellationToken token) =>
        ChangeAsync(state =>
        {
            if (!state.Bootstrapped) throw new InvalidOperationException("Bootstrap must complete first.");
            if (state.Accounts.Count >= 1000) throw new InvalidOperationException("Account limit reached.");
            var account = NewAccount(request);
            if (state.Accounts.Any(x => x.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Username already exists.");
            state.Accounts.Add(account);
            Audit(state, actor, "Account created", account.Id);
            return Public(account);
        }, token);

    public async Task<AccountResponse> UpdateAsync(Guid id, UpdateAccountRequest request, string actor, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!Enum.IsDefined(request.Role)) throw new ArgumentException("Invalid role.");
            var next = CopyState();
            var index = next.Accounts.FindIndex(x => x.Id == id);
            if (index < 0) throw new KeyNotFoundException();
            var account = next.Accounts[index];
            if (account.Role == AccountRole.Administrator && !account.Disabled &&
                (request.Disabled || request.Role != AccountRole.Administrator) &&
                next.Accounts.Count(x => x.Role == AccountRole.Administrator && !x.Disabled) == 1)
                throw new InvalidOperationException("The last enabled administrator cannot be disabled or demoted.");
            if (request.Password is not null) ValidatePassword(request.Password);
            account = account with { Role = request.Role, Disabled = request.Disabled,
                PasswordHash = request.Password is null ? account.PasswordHash : _hasher.HashPassword(account, request.Password) };
            next.Accounts[index] = account;
            Audit(next, actor, $"Account changed: role={request.Role}, disabled={request.Disabled}, passwordReset={request.Password is not null}", id);
            await SaveAsync(next, token);
            foreach (var key in _sessions.Where(x => x.Value.AccountId == id).Select(x => x.Key).ToArray()) _sessions.Remove(key);
            _attempts.Remove(id);
            return Public(account);
        }
        finally { _gate.Release(); }
    }

    public Task<SessionMetadataResponse[]> GetSessionsAsync(CancellationToken token) =>
        ReadAsync(() => { Prune(); return _sessions.Values.Select(x => new SessionMetadataResponse(x.Id, x.AccountId, x.CreatedAt, x.ExpiresAt)).ToArray(); }, token);
    public Task<AccountAuditResponse[]> GetAuditAsync(CancellationToken token) =>
        ReadAsync(() => _state.Audit.TakeLast(500).Reverse().ToArray(), token);
    public Task RecordAdministrativeEventAsync(string actor, string message, CancellationToken token) =>
        ChangeAsync(state => { Audit(state, actor, message, null); return true; }, token);

    private Account NewAccount(CreateAccountRequest request)
    {
        if (request.Username is null || request.Username.Trim().Length is < 3 or > 64 ||
            !request.Username.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') || !Enum.IsDefined(request.Role))
            throw new ArgumentException("Username must be 3-64 ASCII letters, digits, dot, underscore or hyphen.");
        ValidatePassword(request.Password);
        var account = new Account(Guid.NewGuid(), request.Username.Trim(), "", request.Role, false, clock.GetUtcNow());
        return account with { PasswordHash = _hasher.HashPassword(account, request.Password) };
    }

    private static void ValidatePassword(string password)
    {
        if (password is null || password.Length is < 14 or > 256 || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Use a unique password or passphrase of 14-256 characters.");
    }
    private static PasswordHasher<Account> CreateHasher() => new(new Microsoft.Extensions.Options.OptionsWrapper<PasswordHasherOptions>(
        new PasswordHasherOptions { IterationCount = 210000 }));
    private void Prune()
    {
        foreach (var key in _sessions.Where(x => x.Value.ExpiresAt <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) _sessions.Remove(key);
    }
    private void Audit(State state, string actor, string message, Guid? id)
    {
        if (state.Audit.Count >= 100000) throw new InvalidOperationException("Account audit store is full.");
        state.Audit.Add(new(clock.GetUtcNow(), actor, message, id));
    }
    internal static string Actor(AccountResponse account) => $"account:{account.Id}:{account.Username}";
    private static string Actor(Account account) => $"account:{account.Id}:{account.Username}";
    private static AccountResponse Public(Account account) => new(account.Id, account.Username, account.Role, account.Disabled, account.CreatedAt);
    private static SessionResponse Response(Session session, Account account) => new(session.Id, Public(account), session.ExpiresAt, session.Csrf);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private State CopyState() => new() { Bootstrapped = _state.Bootstrapped, Accounts = [.. _state.Accounts], Audit = [.. _state.Audit] };
    private async Task<T> ReadAsync<T>(Func<T> read, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { return read(); }
        finally { _gate.Release(); }
    }
    private async Task<T> ChangeAsync<T>(Func<State, T> change, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { var next = CopyState(); var result = change(next); await SaveAsync(next, token); return result; }
        finally { _gate.Release(); }
    }
    private async Task SaveAsync(State state, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var staging = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(state, Json), token);
                stream.Flush(true);
            }
            File.Move(staging, _path, true);
            _state = state;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    public void Dispose() { _ownership?.Dispose(); _gate.Dispose(); }
    internal sealed record Account(Guid Id, string Username, string PasswordHash, AccountRole Role, bool Disabled, DateTimeOffset CreatedAt);
    private sealed record Session(Guid Id, Guid AccountId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string Csrf);
    private sealed record Attempts(int Failures, DateTimeOffset WindowStart, DateTimeOffset LockedUntil);
    internal sealed class State
    {
        public bool Bootstrapped { get; set; }
        public List<Account> Accounts { get; init; } = [];
        public List<AccountAuditResponse> Audit { get; init; } = [];
    }
}
