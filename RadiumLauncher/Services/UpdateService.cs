using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RadiumLauncher.Services;

public class UpdateService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<(bool IsAvailable, string LatestVersion, string HtmlUrl)> CheckLatestReleaseAsync(string currentVersion)
    {
        // AppConstants.GitHubRepo must be set to "owner/repo"
        var repo = Models.AppConstants.GitHubRepo;
        if (string.IsNullOrWhiteSpace(repo) || repo == "owner/repo")
            return (false, string.Empty, string.Empty);

        var url = $"https://api.github.com/repos/{repo}/releases/latest";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("RadiumLauncher-Updater");

        using var resp = await _http.SendAsync(req).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return (false, string.Empty, string.Empty);

        using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        try
        {
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagEl)) return (false, string.Empty, string.Empty);
            var tag = tagEl.GetString() ?? string.Empty;
            if (!root.TryGetProperty("html_url", out var htmlEl)) return (false, string.Empty, string.Empty);
            var html = htmlEl.GetString() ?? string.Empty;

            // normalize: strip leading 'v' from tag
            var tagTrim = tag.TrimStart('v', 'V');

            if (!Version.TryParse(tagTrim, out var latestV))
            {
                return (false, tag, html);
            }

            if (!Version.TryParse(currentVersion, out var currentV))
            {
                currentV = new Version(0, 0, 0);
            }

            var available = latestV > currentV;
            return (available, tag, html);
        }
        catch
        {
            return (false, string.Empty, string.Empty);
        }
    }
}
