using AppListener.Configuration;
using AppListener.Steam;
using Microsoft.Extensions.Logging;

namespace AppListener.Commands;

public static class AppInfoCommand
{
    public static async Task<int> RunAsync(uint appId, AppListenerConfig config, CancellationToken ct)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(o => o.SingleLine = true));
        var steam = new SteamSession(config.Steam, loggerFactory.CreateLogger<SteamSession>());

        try
        {
            await steam.ConnectAsync(ct).ConfigureAwait(false);

            var info = await steam.GetAppInfoAsync(appId, ct).ConfigureAwait(false);
            if (info == null)
            {
                Console.Error.WriteLine($"No PICS info available for app {appId}.");
                return 1;
            }

            Console.WriteLine($"{info.Name} (app {info.AppId})");
            foreach (var (branch, buildId) in info.BranchBuildIds.OrderBy(b => b.Key))
            {
                Console.WriteLine($"  {branch,-20} buildid {buildId}");
            }

            return 0;
        }
        finally
        {
            await steam.DisposeAsync().ConfigureAwait(false);
        }
    }
}
