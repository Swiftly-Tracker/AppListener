namespace AppListener.Configuration;

public sealed record SteamConfig(string? Username, string? Password, bool RememberPassword)
{
    public bool IsAnonymous => string.IsNullOrEmpty(Username);
}

public sealed record WatchedApp(
    uint AppId,
    string Branch,
    string Repo,
    string DispatchRepo,
    string WorkflowId,
    string GitRef,
    string? Token);

public sealed record AppListenerConfig(
    SteamConfig Steam,
    string? DefaultGithubToken,
    IReadOnlyList<WatchedApp> Apps)
{
    public string ResolveGithubToken(WatchedApp app)
        => app.Token
            ?? DefaultGithubToken
            ?? throw new InvalidOperationException(
                $"App {app.AppId} has no GitHub token configured and there is no default '[github] token' set.");
}
