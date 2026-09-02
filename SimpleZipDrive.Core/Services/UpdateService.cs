using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimpleZipDrive.Core.Services;

/// <summary>
///     Implementation of the update service.
/// </summary>
public partial class UpdateService : IUpdateService
{
    private const string RepoOwner = "purelogiccode";
    private const string RepoName = "SimpleZipDrive";

    /// <summary>Release-check endpoint (canonical repository owner).</summary>
    internal const string LatestApiUrl =
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly SocketsHttpHandler DefaultHttpHandler = new()
    {
        SslOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.None
        }
    };

    private static HttpClient? _defaultHttpClient;
    private static readonly Lock HttpClientLock = new();
    private readonly HttpClient? _injectedHttpClient;

    private readonly IUserNotificationService _userNotificationService;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UpdateService" /> class.
    /// </summary>
    /// <param name="userNotificationService">The user notification service.</param>
    public UpdateService(IUserNotificationService userNotificationService)
    {
        _userNotificationService =
            userNotificationService ?? throw new ArgumentNullException(nameof(userNotificationService));
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="UpdateService" /> class with a custom HttpClient.
    ///     This constructor is intended for testing purposes.
    /// </summary>
    /// <param name="userNotificationService">The user notification service.</param>
    /// <param name="httpClient">The HttpClient to use for HTTP requests.</param>
    public UpdateService(IUserNotificationService userNotificationService, HttpClient httpClient)
    {
        _userNotificationService =
            userNotificationService ?? throw new ArgumentNullException(nameof(userNotificationService));
        _injectedHttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var current = Assembly.GetEntryAssembly()?.GetName().Version
                          ?? new Version(0, 0, 0, 0);

            var client = GetHttpClient();

            var release = await TryGetLatestReleaseAsync(client, LatestApiUrl, cancellationToken);

            if (release is null) return;

            var (tagName, htmlUrl) = release.Value;

            var m = VersionRegex().Match(tagName);
            if (!m.Success) return;

            var latest = Version.Parse(m.Value);

            if (latest <= current) return;

            _userNotificationService.ShowUpdateAvailable(current, latest, htmlUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or TimeoutException)
        {
            // No internet / DNS failure / connection problem - expected environment condition,
            // not a bug. Log quietly without forwarding to the bug report API.
            DiagnosticLogger.Log($"Update check skipped: {ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            await ErrorLoggerStatic.LogErrorAsync(ex, "UpdateService.CheckForUpdateAsync", cancellationToken);
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient(DefaultHttpHandler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.Add("User-Agent", $"{RepoName}-UpdateChecker");
        return client;
    }

    private HttpClient GetHttpClient()
    {
        if (_injectedHttpClient != null) return _injectedHttpClient;

        if (_defaultHttpClient == null)
        {
            lock (HttpClientLock)
            {
                _defaultHttpClient ??= CreateDefaultHttpClient();
            }
        }

        return _defaultHttpClient;
    }

    /// <summary>
    ///     Fetches and parses the latest release payload from the given GitHub API endpoint.
    ///     Returns <see langword="null" /> when the endpoint reports a non-success status or the
    ///     payload lacks required fields; network-level exceptions propagate to the caller.
    /// </summary>
    private static async Task<(string TagName, string HtmlUrl)?> TryGetLatestReleaseAsync(
        HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var resp = await client.GetAsync(url, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;

        await using var jsonStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: cancellationToken);

        var tagName = doc.RootElement.GetProperty("tag_name").GetString();
        var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();
        if (tagName is null || htmlUrl is null) return null;

        return (tagName, htmlUrl);
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+", RegexOptions.Compiled, "00:00:01")]
    private static partial Regex VersionRegex();
}