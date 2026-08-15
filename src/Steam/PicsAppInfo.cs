namespace AppListener.Steam;

public sealed record PicsAppInfo(uint AppId, string Name, IReadOnlyDictionary<string, uint> BranchBuildIds);
