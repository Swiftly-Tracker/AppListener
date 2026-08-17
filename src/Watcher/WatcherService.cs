using AppListener.Configuration;
using AppListener.GitHub;
using AppListener.Steam;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AppListener.Watcher;

public sealed class WatcherService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly SteamSession _steam;
    private readonly GitHubBuildIdClient _github;
    private readonly AppListenerConfig _config;
    private readonly ILogger<WatcherService> _logger;
    private readonly Dictionary<uint, uint> _lastDispatchedBuildId = [];

    public WatcherService(SteamSession steam, GitHubBuildIdClient github, AppListenerConfig config, ILogger<WatcherService> logger)
    {
        _steam = steam;
        _github = github;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _steam.ConnectAsync(stoppingToken).ConfigureAwait(false);

            var byAppId = _config.Apps.ToDictionary(app => app.AppId);

            foreach (var app in _config.Apps)
            {
                await CheckAppAsync(app, stoppingToken).ConfigureAwait(false);
            }

            await foreach (var appId in _steam.WatchForChangesAsync(PollInterval, stoppingToken))
            {
                if (byAppId.TryGetValue(appId, out var app))
                {
                    await CheckAppAsync(app, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Shutdown requested.");
        }
        finally
        {
            await _steam.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task CheckAppAsync(WatchedApp app, CancellationToken ct)
    {
        try
        {
            var info = await _steam.GetAppInfoAsync(app.AppId, ct).ConfigureAwait(false);
            if (info == null || !info.BranchBuildIds.TryGetValue(app.Branch, out var steamBuildId))
            {
                _logger.LogWarning("App {AppId} branch '{Branch}' is not available.", app.AppId, app.Branch);
                return;
            }

            var token = _config.ResolveGithubToken(app);
            var githubBuildId = await _github.GetLatestBuildIdAsync(app.Repo, app.GitRef, token, ct).ConfigureAwait(false);

            if (githubBuildId == null)
            {
                _logger.LogWarning("Could not resolve the latest tracked BuildID for {Repo}.", app.Repo);
                return;
            }

            if (steamBuildId == githubBuildId)
            {
                _logger.LogInformation("App {AppId} ({Name}) is up to date at BuildID {BuildId}.", app.AppId, info.Name, steamBuildId);
                return;
            }

            if (_lastDispatchedBuildId.TryGetValue(app.AppId, out var lastDispatched) && lastDispatched == steamBuildId)
            {
                _logger.LogDebug("Already dispatched app {AppId} for BuildID {BuildId}; waiting on {Repo}.", app.AppId, steamBuildId, app.Repo);
                return;
            }

            _logger.LogInformation(
                "App {AppId} ({Name}) is out of date: Steam has BuildID {SteamBuildId}, {Repo} has {GithubBuildId}.",
                app.AppId, info.Name, steamBuildId, app.Repo, githubBuildId);

            if (await _github.IsWorkflowRunningAsync(app.DispatchRepo, app.WorkflowId, token, ct).ConfigureAwait(false))
            {
                _logger.LogInformation(
                    "Workflow {WorkflowId} on {Repo} is already running; skipping dispatch.",
                    app.WorkflowId, app.DispatchRepo);
                return;
            }

            if (await _github.DispatchWorkflowAsync(app.DispatchRepo, app.WorkflowId, app.GitRef, token, ct).ConfigureAwait(false))
            {
                _lastDispatchedBuildId[app.AppId] = steamBuildId;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to check app {AppId} for updates.", app.AppId);
        }
    }
}
