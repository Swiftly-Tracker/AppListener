using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace AppListener.GitHub;

public sealed class GitHubBuildIdClient
{
    private readonly HttpClient _http;
    private readonly ILogger<GitHubBuildIdClient> _logger;

    public GitHubBuildIdClient(ILogger<GitHubBuildIdClient> logger)
    {
        _logger = logger;
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppListener", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<uint?> GetLatestBuildIdAsync(string repo, string branch, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{repo}/commits/{branch}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to read latest commit for {Repo}@{Branch}: {Status}", repo, branch, response.StatusCode);
            return null;
        }

        var commit = await response.Content.ReadFromJsonAsync<GitHubCommitResponse>(ct).ConfigureAwait(false);
        var message = commit?.Commit?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("Commit for {Repo}@{Branch} has no message.", repo, branch);
            return null;
        }

        var firstLine = message.Split('\n', 2)[0];
        var buildIdText = firstLine.Split('-', 2)[0].Trim();

        if (!uint.TryParse(buildIdText, out var buildId))
        {
            _logger.LogWarning(
                "Could not parse a BuildID from {Repo}@{Branch}'s latest commit message: '{Message}'.",
                repo, branch, firstLine);
            return null;
        }

        return buildId;
    }

    public async Task<bool> DispatchWorkflowAsync(string dispatchRepo, string workflowId, string workflowRef, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"repos/{dispatchRepo}/actions/workflows/{workflowId}/dispatches")
        {
            Content = JsonContent.Create(new { @ref = workflowRef }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Failed to dispatch workflow {WorkflowId} on {Repo}: {Status} {Reason}",
                workflowId, dispatchRepo, response.StatusCode, response.ReasonPhrase);
            return false;
        }

        _logger.LogInformation("Dispatched workflow {WorkflowId} on {Repo} (ref {Ref}).", workflowId, dispatchRepo, workflowRef);
        return true;
    }

    private sealed class GitHubCommitResponse
    {
        [JsonPropertyName("commit")]
        public GitHubCommitDetail? Commit { get; set; }
    }

    private sealed class GitHubCommitDetail
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
