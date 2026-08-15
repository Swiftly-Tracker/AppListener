using AppListener.Commands;
using AppListener.Configuration;
using AppListener.GitHub;
using AppListener.Steam;
using AppListener.Watcher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public static class Entrypoint
{
    private const string ConfigPath = "config.toml";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "-help" or "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (args.Length > 0 && args[0] == "app_info")
        {
            if (args.Length < 2 || !uint.TryParse(args[1], out var appId))
            {
                Console.Error.WriteLine("usage: AppListener app_info <appid>");
                return 1;
            }

            var config = LoadConfigOrExit();
            if (config == null)
            {
                return 1;
            }

            return await AppInfoCommand.RunAsync(appId, config, CancellationToken.None).ConfigureAwait(false);
        }

        return await RunDaemonAsync().ConfigureAwait(false);
    }

    private static async Task<int> RunDaemonAsync()
    {
        var config = LoadConfigOrExit();
        if (config == null)
        {
            return 1;
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(config.Steam);
        builder.Services.AddSingleton<SteamSession>();
        builder.Services.AddSingleton<GitHubBuildIdClient>();
        builder.Services.AddHostedService<WatcherService>();

        var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static AppListenerConfig? LoadConfigOrExit()
    {
        try
        {
            return ConfigLoader.Load(ConfigPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return null;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            AppListener - watches Steam PICS for app build updates and dispatches GitHub workflows.

            usage:
              AppListener                 run the watcher daemon (reads ./config.toml)
              AppListener app_info <id>   print PICS branch/buildid info for a Steam app id
              AppListener -help           show this message
            """);
    }
}
