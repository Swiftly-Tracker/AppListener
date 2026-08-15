using Tomlyn;
using Tomlyn.Model;

namespace AppListener.Configuration;

public static class ConfigLoader
{
    public static AppListenerConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Config file not found, it should be at '{path}'.", path);
        }

        var text = File.ReadAllText(path);
        var model = TomlSerializer.Deserialize<TomlTable>(text)
            ?? throw new InvalidOperationException($"Config file '{path}' is empty or invalid.");

        var steam = ReadSteamConfig(model);
        var defaultGithubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? ReadNestedString(model, "github", "token");

        var apps = ReadApps(model);

        var config = new AppListenerConfig(steam, defaultGithubToken, apps);
        Validate(config);
        return config;
    }

    private static SteamConfig ReadSteamConfig(TomlTable model)
    {
        var username = Environment.GetEnvironmentVariable("STEAM_USERNAME")
            ?? ReadNestedString(model, "steam", "username");
        var password = Environment.GetEnvironmentVariable("STEAM_PASSWORD")
            ?? ReadNestedString(model, "steam", "password");
        var remember = ReadNestedBool(model, "steam", "remember_password") ?? false;

        return new SteamConfig(username, password, remember);
    }

    private static List<WatchedApp> ReadApps(TomlTable model)
    {
        var apps = new List<WatchedApp>();

        if (!model.TryGetValue("apps", out var appsValue) || appsValue is not TomlTable appsTable)
        {
            return apps;
        }

        foreach (var (key, value) in appsTable)
        {
            if (!uint.TryParse(key, out var appId))
            {
                throw new InvalidOperationException($"App table key '{key}' under [apps] is not a valid Steam app id.");
            }

            if (value is not TomlTable appTable)
            {
                throw new InvalidOperationException($"[apps.{key}] must be a table.");
            }

            var repo = GetString(appTable, "repo")
                ?? throw new InvalidOperationException($"App {key} is missing 'repo'.");
            var workflowId = GetString(appTable, "workflow_id")
                ?? throw new InvalidOperationException($"App {key} is missing 'workflow_id'.");
            var branch = GetString(appTable, "branch") ?? "public";
            var dispatchRepo = GetString(appTable, "dispatch_repo") ?? repo;
            var gitRef = GetString(appTable, "git_ref") ?? "main";
            var token = GetString(appTable, "token");

            apps.Add(new WatchedApp(appId, branch, repo, dispatchRepo, workflowId, gitRef, token));
        }

        return apps;
    }

    private static void Validate(AppListenerConfig config)
    {
        var envLogin = Environment.GetEnvironmentVariable("STEAM_USERNAME") != null
            && Environment.GetEnvironmentVariable("STEAM_PASSWORD") != null;

        if (!config.Steam.IsAnonymous && string.IsNullOrEmpty(config.Steam.Password) && !envLogin)
        {
            throw new InvalidOperationException("A Steam username was set but no password was provided.");
        }

        if (config.Apps.Count == 0)
        {
            throw new InvalidOperationException("Config is missing an [apps.<id>] entry to watch.");
        }

        foreach (var app in config.Apps)
        {
            _ = config.ResolveGithubToken(app);
        }
    }

    private static string? ReadNestedString(TomlTable model, string table, string key)
        => model.TryGetValue(table, out var value) && value is TomlTable nested
            ? GetString(nested, key)
            : null;

    private static bool? ReadNestedBool(TomlTable model, string table, string key)
        => model.TryGetValue(table, out var value) && value is TomlTable nested
            ? GetBool(nested, key)
            : null;

    private static string? GetString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as string : null;

    private static bool? GetBool(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is bool b ? b : null;
}
