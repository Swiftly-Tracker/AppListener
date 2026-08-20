using AppListener.Configuration;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.Authentication;

namespace AppListener.Steam;

public sealed class SteamSession : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly SteamConfig _config;
    private readonly ILogger<SteamSession> _logger;
    private readonly SteamClient _client;
    private readonly CallbackManager _manager;
    private readonly SteamUser _user;
    private readonly SteamApps _apps;

    private readonly List<IDisposable> _subscriptions = [];
    private readonly CancellationTokenSource _pumpShutdown = new();

    private Task? _pumpTask;
    private TaskCompletionSource? _connected;
    private TaskCompletionSource<SteamUser.LoggedOnCallback>? _loggedOn;

    private bool _disposed;

    public SteamSession(SteamConfig config, ILogger<SteamSession> logger)
    {
        _config = config;
        _logger = logger;

        _client = new SteamClient();
        _manager = new CallbackManager(_client);

        _user = _client.GetHandler<SteamUser>() ?? throw new InvalidOperationException("SteamKit is missing its SteamUser handler.");
        _apps = _client.GetHandler<SteamApps>() ?? throw new InvalidOperationException("SteamKit is missing its SteamApps handler.");

        _subscriptions.Add(_manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected));
        _subscriptions.Add(_manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected));
        _subscriptions.Add(_manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn));
    }

    public bool IsLoggedOn { get; private set; }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _pumpTask ??= Task.Factory.StartNew(PumpCallbacks, TaskCreationOptions.LongRunning).Unwrap();

        _connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _loggedOn = new TaskCompletionSource<SteamUser.LoggedOnCallback>(TaskCreationOptions.RunContinuationsAsynchronously);

        _logger.LogInformation("Connecting to Steam...");
        _client.Connect();

        await _connected.Task.WaitAsync(ConnectTimeout, ct).ConfigureAwait(false);

        await LogOnAsync(ct).ConfigureAwait(false);
    }

    public async Task<PicsAppInfo?> GetAppInfoAsync(uint appId, CancellationToken ct)
    {
        var tokens = await _apps.PICSGetAccessTokens(appId, null).ToTask().WaitAsync(ConnectTimeout, ct).ConfigureAwait(false);

        var request = new SteamApps.PICSRequest(appId);
        if (tokens.AppTokens.TryGetValue(appId, out var accessToken))
        {
            request.AccessToken = accessToken;
        }

        var result = await _apps.PICSGetProductInfo(request, package: null).ToTask().WaitAsync(ConnectTimeout, ct).ConfigureAwait(false);

        if (result.Failed || result.Results == null)
        {
            return null;
        }

        foreach (var response in result.Results)
        {
            if (!response.Apps.TryGetValue(appId, out var app))
            {
                continue;
            }

            var keyValues = app.KeyValues;
            var branches = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

            foreach (var branch in keyValues["depots"]["branches"].Children)
            {
                branches[branch.Name ?? string.Empty] = branch["buildid"].AsUnsignedInteger();
            }

            var name = keyValues["common"]["name"].AsString() ?? $"app {appId}";
            return new PicsAppInfo(appId, name, branches);
        }

        return null;
    }

    private async Task LogOnAsync(CancellationToken ct)
    {
        if (_config.IsAnonymous)
        {
            _user.LogOnAnonymous();
        }
        else
        {
            var authSession = await _client.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
            {
                Username = _config.Username!,
                Password = _config.Password!,
                IsPersistentSession = _config.RememberPassword,
                Authenticator = new UserConsoleAuthenticator(),
            }).ConfigureAwait(false);

            var poll = await authSession.PollingWaitForResultAsync().ConfigureAwait(false);

            _user.LogOn(new SteamUser.LogOnDetails
            {
                Username = poll.AccountName,
                AccessToken = poll.RefreshToken,
                ShouldRememberPassword = _config.RememberPassword,
            });
        }

        var callback = await _loggedOn!.Task.WaitAsync(ConnectTimeout, ct).ConfigureAwait(false);

        if (callback.Result != EResult.OK)
        {
            throw new InvalidOperationException($"Steam logon failed: {callback.Result} / {callback.ExtendedResult}.");
        }

        IsLoggedOn = true;
        _logger.LogInformation("Logged into Steam as {Account}.", _config.IsAnonymous ? "<anonymous>" : _config.Username);
    }

    private async Task PumpCallbacks()
    {
        while (!_pumpShutdown.IsCancellationRequested)
        {
            try
            {
                _manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(200));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Steam callback pump error.");
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void OnConnected(SteamClient.ConnectedCallback callback) => _connected?.TrySetResult();

    private void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        IsLoggedOn = false;

        _connected?.TrySetException(new IOException("Steam closed the connection."));
        _loggedOn?.TrySetException(new IOException("Steam closed the connection during logon."));

        if (!callback.UserInitiated)
        {
            _logger.LogWarning("Disconnected from Steam.");
        }
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback) => _loggedOn?.TrySetResult(callback);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsLoggedOn = false;

        if (_client.IsConnected)
        {
            _user.LogOff();
            _client.Disconnect();
        }

        await _pumpShutdown.CancelAsync().ConfigureAwait(false);

        if (_pumpTask != null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _pumpShutdown.Dispose();
    }
}
