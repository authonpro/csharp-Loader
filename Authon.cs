// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  Authon C# SDK — Software Licensing & Authentication                       ║
// ║  Version: 2.0.0                                                            ║
// ║  GitHub:  https://github.com/authonpro                                     ║
// ║  Docs:    https://authon.pro/docs                                          ║
// ║                                                                            ║
// ║  Compatible with: .NET 6+, .NET 7+, .NET 8+, .NET Framework 4.6.1+        ║
// ║  Dependencies: None (System.Net.Http + System.Text.Json + System.Management)║
// ╚══════════════════════════════════════════════════════════════════════════════╝

#pragma warning disable CS8604 // Possible null reference argument (safe after EnsureAuthenticated guard)
#pragma warning disable CS8603 // Possible null reference return (nullable string? return types are intentional)
#pragma warning disable CS8625 // Cannot convert null literal (default parameter values)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AuthonSDK
{
    /// <summary>
    /// Main client for the Authon authentication and licensing API.
    /// <para>
    /// Provides methods for application initialization, user authentication (login, license, register),
    /// session management, variable storage, file downloads, activity logging, and more.
    /// </para>
    /// <example>
    /// <code>
    /// var client = new AuthonClient("your-app-id", "your-api-key");
    /// await client.InitAsync();
    /// var result = await client.LoginAsync("username", "password");
    /// if (result.Success) Console.WriteLine($"Welcome, {client.Username}!");
    /// </code>
    /// </example>
    /// </summary>
    public sealed class AuthonClient : IDisposable
    {
        #region Constants

        private const string DefaultApiUrl = "https://api.authon.pro/v1";
        private const int DefaultTimeoutSeconds = 30;
        private const int MaxRetries = 2;
        private const int RetryDelayMs = 1000;

        #endregion

        #region Private Fields

        private readonly string _appId;
        private readonly string _apiKey;
        private readonly string _apiUrl;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private bool _disposed;

        #endregion

        #region Public Properties — Session State

        /// <summary>Gets the current session token. Null if not authenticated.</summary>
        public string? SessionToken { get; private set; }

        /// <summary>Gets the authenticated username. Null if not authenticated.</summary>
        public string? Username { get; private set; }

        /// <summary>Gets the user's access level (0+). Higher levels grant more permissions.</summary>
        public int Level { get; private set; }

        /// <summary>Gets the subscription plan name. Null if no subscription is assigned.</summary>
        public string? Subscription { get; private set; }

        /// <summary>Gets the subscription expiration date as an ISO 8601 string. Null for lifetime licenses.</summary>
        public string? ExpiresAt { get; private set; }

        /// <summary>Gets whether the client has an active authenticated session.</summary>
        public bool IsAuthenticated => !string.IsNullOrEmpty(SessionToken);

        #endregion

        #region Public Properties — Application Info

        /// <summary>Gets the application name returned from <see cref="InitAsync"/>.</summary>
        public string? AppName { get; private set; }

        /// <summary>Gets the application version returned from <see cref="InitAsync"/>.</summary>
        public string? AppVersion { get; private set; }

        /// <summary>Gets the application update URL if set. Null otherwise.</summary>
        public string? UpdateUrl { get; private set; }

        /// <summary>Gets whether the app has been successfully initialized.</summary>
        public bool IsInitialized { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new Authon client instance.
        /// </summary>
        /// <param name="appId">Your application ID from the Authon dashboard.</param>
        /// <param name="apiKey">Your API key from the Authon dashboard (Settings tab).</param>
        /// <param name="apiUrl">
        /// Optional custom API URL. Defaults to <c>https://api.authon.pro/v1</c>.
        /// </param>
        /// <param name="timeoutSeconds">HTTP request timeout in seconds. Defaults to 30.</param>
        /// <exception cref="ArgumentNullException">Thrown when appId or apiKey is null or empty.</exception>
        public AuthonClient(string appId, string apiKey, string apiUrl = DefaultApiUrl, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentNullException(nameof(appId), "Application ID is required.");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "API key is required.");

            _appId = appId.Trim();
            _apiKey = apiKey.Trim();
            _apiUrl = apiUrl?.TrimEnd('/') ?? DefaultApiUrl;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AuthonSDK-CSharp/2.0.0");
            _ownsHttpClient = true;
        }

        /// <summary>
        /// Creates a new Authon client with an externally managed <see cref="HttpClient"/>.
        /// Use this constructor for dependency injection or connection pooling scenarios.
        /// </summary>
        /// <param name="appId">Your application ID.</param>
        /// <param name="apiKey">Your API key.</param>
        /// <param name="httpClient">A pre-configured HttpClient instance. The caller is responsible for its lifetime.</param>
        /// <param name="apiUrl">Optional custom API URL.</param>
        public AuthonClient(string appId, string apiKey, HttpClient httpClient, string apiUrl = DefaultApiUrl)
        {
            if (string.IsNullOrWhiteSpace(appId))
                throw new ArgumentNullException(nameof(appId));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey));

            _appId = appId.Trim();
            _apiKey = apiKey.Trim();
            _apiUrl = apiUrl?.TrimEnd('/') ?? DefaultApiUrl;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ownsHttpClient = false;
        }

        #endregion

        #region HWID Generation

        /// <summary>
        /// Generates a hardware identifier (HWID) unique to the current machine.
        /// <para>
        /// On Windows, combines the primary disk drive serial number with the computer name,
        /// then produces an MD5 hash. Falls back to computer name only if WMI is unavailable.
        /// </para>
        /// </summary>
        /// <returns>A 32-character lowercase hexadecimal hardware ID string.</returns>
        public static string GetHWID()
        {
            string raw;
            try
            {
                string serial = GetDiskSerial();
                raw = serial + Environment.MachineName;
            }
            catch
            {
                // Fallback: use machine name only
                raw = Environment.MachineName + Environment.UserName;
            }

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder(32);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Retrieves the serial number of the primary physical disk drive via WMI.
        /// </summary>
        private static string GetDiskSerial()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive WHERE Index=0"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string? serial = obj["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(serial))
                        return serial;
                }
            }

            // Fallback: try any disk
            using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string? serial = obj["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(serial))
                        return serial;
                }
            }

            return string.Empty;
        }

        #endregion

        #region API Methods — Initialization

        /// <summary>
        /// Initializes the connection to the Authon API and validates your application credentials.
        /// <para>Must be called before any other API method.</para>
        /// </summary>
        /// <param name="hash">Optional application hash for integrity verification (if hash check is enabled).</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>An <see cref="AuthonResponse"/> containing app info on success.</returns>
        /// <example>
        /// <code>
        /// var response = await client.InitAsync();
        /// if (response.Success)
        ///     Console.WriteLine($"Connected to {client.AppName} v{client.AppVersion}");
        /// else
        ///     Console.WriteLine($"Init failed: {response.Message}");
        /// </code>
        /// </example>
        public async Task<AuthonResponse> InitAsync(string hash = null, CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object> { { "type", "init" } };
            if (!string.IsNullOrEmpty(hash))
                payload["hash"] = hash;

            var response = await SendRequestAsync(payload, cancellationToken).ConfigureAwait(false);

            if (response.Success && response.Data != null)
            {
                AppName = response.Data.GetString("name");
                AppVersion = response.Data.GetString("version");
                UpdateUrl = response.Data.GetString("updateUrl");
                IsInitialized = true;
            }

            return response;
        }

        #endregion

        #region API Methods — Authentication

        /// <summary>
        /// Authenticates a user with username and password.
        /// <para>On success, populates session properties: <see cref="SessionToken"/>, <see cref="Username"/>,
        /// <see cref="Level"/>, <see cref="Subscription"/>, <see cref="ExpiresAt"/>.</para>
        /// </summary>
        /// <param name="username">The user's username.</param>
        /// <param name="password">The user's password.</param>
        /// <param name="hwid">
        /// Optional hardware ID. If null, auto-generated via <see cref="GetHWID"/>.
        /// </param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>An <see cref="AuthonResponse"/> indicating success or failure with a message.</returns>
        public async Task<AuthonResponse> LoginAsync(string username, string password, string hwid = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentNullException(nameof(username));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentNullException(nameof(password));

            var response = await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "login" },
                { "username", username },
                { "password", password },
                { "hwid", hwid ?? GetHWID() }
            }, cancellationToken).ConfigureAwait(false);

            if (response.Success && response.Data != null)
            {
                SessionToken = response.Data.GetString("sessionToken");
                Username = response.Data.GetString("username");
                Level = response.Data.GetInt("level");
                Subscription = response.Data.GetString("subscription");
                ExpiresAt = response.Data.GetString("expiresAt");
            }

            return response;
        }

        /// <summary>
        /// Authenticates using a license key only (no username/password required).
        /// <para>On success, populates session properties.</para>
        /// </summary>
        /// <param name="licenseKey">The license key to authenticate with.</param>
        /// <param name="hwid">Optional hardware ID. Auto-generated if null.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>An <see cref="AuthonResponse"/> indicating success or failure.</returns>
        public async Task<AuthonResponse> LicenseAsync(string licenseKey, string hwid = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
                throw new ArgumentNullException(nameof(licenseKey));

            var response = await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "license" },
                { "licenseKey", licenseKey },
                { "hwid", hwid ?? GetHWID() }
            }, cancellationToken).ConfigureAwait(false);

            if (response.Success && response.Data != null)
            {
                SessionToken = response.Data.GetString("sessionToken");
                Level = response.Data.GetInt("level");
                Subscription = response.Data.GetString("subscription");
                ExpiresAt = response.Data.GetString("expiresAt");
            }

            return response;
        }

        /// <summary>
        /// Registers a new user account with a license key.
        /// </summary>
        /// <param name="username">Desired username for the new account.</param>
        /// <param name="password">Password for the new account (minimum 6 characters recommended).</param>
        /// <param name="licenseKey">A valid unused license key to activate the account.</param>
        /// <param name="hwid">Optional hardware ID. Auto-generated if null.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>An <see cref="AuthonResponse"/>. On success, contains the new user's level and expiration.</returns>
        public async Task<AuthonResponse> RegisterAsync(string username, string password, string licenseKey, string hwid = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentNullException(nameof(username));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentNullException(nameof(password));
            if (string.IsNullOrWhiteSpace(licenseKey))
                throw new ArgumentNullException(nameof(licenseKey));

            return await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "register" },
                { "username", username },
                { "password", password },
                { "licenseKey", licenseKey },
                { "hwid", hwid ?? GetHWID() }
            }, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region API Methods — Session Management

        /// <summary>
        /// Validates the current session token and refreshes the heartbeat.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns><c>true</c> if the session is still valid; otherwise <c>false</c>.</returns>
        public async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(SessionToken))
                return false;

            var response = await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "check" },
                { "sessionToken", SessionToken! }
            }, cancellationToken).ConfigureAwait(false);

            return response.Success;
        }

        /// <summary>
        /// Terminates the current session and clears all local session data.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>An <see cref="AuthonResponse"/> confirming the logout.</returns>
        public async Task<AuthonResponse> LogoutAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(SessionToken))
                return new AuthonResponse { Success = false, Message = "No active session" };

            var response = await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "logout" },
                { "sessionToken", SessionToken }
            }, cancellationToken).ConfigureAwait(false);

            if (response.Success)
                ClearSession();

            return response;
        }

        #endregion

        #region API Methods — Variables

        /// <summary>
        /// Retrieves an application-level variable by key.
        /// <para>Application variables are shared across all users and set from the dashboard.</para>
        /// </summary>
        /// <param name="key">The variable key to look up.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The variable value as a string, or <c>null</c> if not found or on error.</returns>
        public async Task<string> GetVarAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));
            EnsureAuthenticated();

            var response = await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "var" },
                { "key", key },
                { "sessionToken", SessionToken }
            }, cancellationToken).ConfigureAwait(false);

            return response.Success ? response.Data.GetString("value") : null;
        }

        /// <summary>
        /// Sets a user-level variable (scoped to the authenticated user).
        /// <para>Creates the variable if it doesn't exist, or updates the existing value.</para>
        /// </summary>
        /// <param name="key">The variable key.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>An <see cref="AuthonResponse"/> indicating success or failure.</returns>
        public async Task<AuthonResponse> SetVarAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));
            EnsureAuthenticated();

            return await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "setvar" },
                { "key", key },
                { "value", value ?? string.Empty },
                { "sessionToken", SessionToken }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves a user-level variable (scoped to the authenticated user).
        /// </summary>
        /// <param name="key">The variable key to look up.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The variable value as a string, or <c>null</c> if not found.</returns>
        public async Task<string> GetUserVarAsync(string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));
            EnsureAuthenticated();

            var response = await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "getvar" },
                { "key", key },
                { "sessionToken", SessionToken }
            }, cancellationToken).ConfigureAwait(false);

            return response.Success ? response.Data.GetString("value") : null;
        }

        #endregion

        #region API Methods — Files

        /// <summary>
        /// Lists all files available to the authenticated user based on their access level.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A list of <see cref="AuthonFileInfo"/> objects, or an empty list on failure.</returns>
        public async Task<List<AuthonFileInfo>> ListFilesAsync(CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();

            var response = await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "list_files" },
                { "sessionToken", SessionToken }
            }, cancellationToken).ConfigureAwait(false);

            if (response.Success && response.Data.HasValue)
            {
                try
                {
                    var dataElement = response.Data.Value;
                    // data can be an array directly or wrapped in an object
                    if (dataElement.ValueKind == JsonValueKind.Array)
                    {
                        string json = dataElement.GetRawText();
                        return JsonSerializer.Deserialize<List<AuthonFileInfo>>(json) ?? new List<AuthonFileInfo>();
                    }
                }
                catch
                {
                    // gracefully fall through
                }
            }

            return new List<AuthonFileInfo>();
        }

        /// <summary>
        /// Downloads a file by its ID. Returns the raw file bytes.
        /// <para>
        /// The authenticated user must have a level equal to or greater than the file's minimum level.
        /// </para>
        /// </summary>
        /// <param name="fileId">The file ID (from <see cref="ListFilesAsync"/>).</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The file content as a byte array, or <c>null</c> on failure.</returns>
        /// <example>
        /// <code>
        /// byte[] data = await client.DownloadFileAsync("file-id-here");
        /// if (data != null)
        ///     File.WriteAllBytes("output.exe", data);
        /// </code>
        /// </example>
        public async Task<byte[]?> DownloadFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
                throw new ArgumentNullException(nameof(fileId));
            EnsureAuthenticated();

            try
            {
                var payload = new Dictionary<string, object>
                {
                    { "type", "file" },
                    { "appId", _appId },
                    { "apiKey", _apiKey },
                    { "fileId", fileId },
                    { "sessionToken", SessionToken! }
                };

                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(_apiUrl, content, cancellationToken).ConfigureAwait(false);

                // File downloads return application/octet-stream on success
                string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (contentType == "application/octet-stream")
                {
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }

                // If not binary, it's likely a JSON error response — return null
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Downloads a file using the GET endpoint with token-based authentication.
        /// <para>Alternative download method: GET /v1/files/download/:fileId?token=sessionToken</para>
        /// </summary>
        /// <param name="fileId">The file ID to download.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The file content as a byte array, or <c>null</c> on failure.</returns>
        public async Task<byte[]?> DownloadFileViaGetAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
                throw new ArgumentNullException(nameof(fileId));
            EnsureAuthenticated();

            try
            {
                string url = $"{_apiUrl}/files/download/{Uri.EscapeDataString(fileId)}?token={Uri.EscapeDataString(SessionToken!)}";
                using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

                return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region API Methods — Logging & Analytics

        /// <summary>
        /// Sends an activity log message to the Authon dashboard.
        /// <para>Useful for tracking feature usage, errors, or custom events in your application.</para>
        /// </summary>
        /// <param name="message">The log message to record (max 500 characters recommended).</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>An <see cref="AuthonResponse"/> confirming the log was recorded.</returns>
        public async Task<AuthonResponse> LogAsync(string message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));

            var payload = new Dictionary<string, object>
            {
                { "type", "log" },
                { "message", message }
            };

            if (!string.IsNullOrEmpty(SessionToken))
                payload["sessionToken"] = SessionToken;

            return await SendRequestAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Fetches the list of currently online users for your application.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>
        /// An <see cref="AuthonResponse"/> with <c>data.count</c> and <c>data.users</c> array on success.
        /// </returns>
        public async Task<AuthonResponse> FetchOnlineAsync(CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();

            return await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "fetch_online" },
                { "sessionToken", SessionToken }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Fetches application statistics (total users, online count, total keys, app version).
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>
        /// An <see cref="AuthonResponse"/> with <c>data.totalUsers</c>, <c>data.onlineUsers</c>,
        /// <c>data.totalKeys</c>, and <c>data.appVersion</c>.
        /// </returns>
        public async Task<AuthonResponse> FetchStatsAsync(CancellationToken cancellationToken = default)
        {
            EnsureAuthenticated();

            return await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "fetch_stats" },
                { "sessionToken", SessionToken }
            }, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region API Methods — Security

        /// <summary>
        /// Checks whether an IP address or hardware ID is blacklisted for your application.
        /// <para>Does not require an active session.</para>
        /// </summary>
        /// <param name="ip">Optional IP address to check.</param>
        /// <param name="hwid">Optional hardware ID to check.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>
        /// An <see cref="AuthonResponse"/> with <c>data.blacklisted</c> (bool) and <c>data.reason</c> (string or null).
        /// </returns>
        /// <exception cref="ArgumentException">Thrown when both ip and hwid are null.</exception>
        public async Task<AuthonResponse> CheckBlacklistAsync(string? ip = null, string? hwid = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(ip) && string.IsNullOrEmpty(hwid))
                throw new ArgumentException("At least one of 'ip' or 'hwid' must be provided.");

            var payload = new Dictionary<string, object> { { "type", "check_blacklist" } };

            if (!string.IsNullOrEmpty(ip))
                payload["ip"] = ip;
            if (!string.IsNullOrEmpty(hwid))
                payload["hwid"] = hwid;

            return await SendRequestAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Redeems a referral code, adding bonus days to both the redeemer and the referral creator.
        /// </summary>
        /// <param name="code">The referral code to redeem.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>
        /// An <see cref="AuthonResponse"/> with <c>data.expiresAt</c> (new expiry) and <c>data.rewardDays</c>.
        /// </returns>
        public async Task<AuthonResponse> RedeemReferralAsync(string code, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentNullException(nameof(code));
            EnsureAuthenticated();

            return await SendRequestAsync(new Dictionary<string, object>
            {
                { "type", "redeem_referral" },
                { "code", code },
                { "sessionToken", SessionToken }
            }, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Internal Helpers

        /// <summary>
        /// Sends a POST request to the Authon API with automatic credential injection and retry logic.
        /// </summary>
        private async Task<AuthonResponse> SendRequestAsync(Dictionary<string, object> payload, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            // Inject credentials
            payload["appId"] = _appId;
            payload["apiKey"] = _apiKey;

            int attempt = 0;
            while (true)
            {
                try
                {
                    string json = JsonSerializer.Serialize(payload);
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var httpResponse = await _httpClient.PostAsync(_apiUrl, content, cancellationToken).ConfigureAwait(false);

                    string responseBody = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

                    var result = JsonSerializer.Deserialize<AuthonResponse>(responseBody);
                    if (result == null)
                    {
                        return new AuthonResponse
                        {
                            Success = false,
                            Message = "Failed to parse API response"
                        };
                    }

                    return result;
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout — retry if attempts remain
                    attempt++;
                    if (attempt >= MaxRetries)
                        return new AuthonResponse { Success = false, Message = "Request timed out" };

                    await Task.Delay(RetryDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // User cancelled
                    return new AuthonResponse { Success = false, Message = "Request cancelled" };
                }
                catch (HttpRequestException ex)
                {
                    attempt++;
                    if (attempt >= MaxRetries)
                        return new AuthonResponse { Success = false, Message = $"Connection failed: {ex.Message}" };

                    await Task.Delay(RetryDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return new AuthonResponse { Success = false, Message = $"Unexpected error: {ex.Message}" };
                }
            }
        }

        /// <summary>Throws if the client has no active session token.</summary>
        private void EnsureAuthenticated()
        {
            if (string.IsNullOrEmpty(SessionToken))
                throw new InvalidOperationException("Not authenticated. Call LoginAsync() or LicenseAsync() first.");
        }

        /// <summary>Clears all session-related properties.</summary>
        private void ClearSession()
        {
            SessionToken = null;
            Username = null;
            Level = 0;
            Subscription = null;
            ExpiresAt = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AuthonClient));
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Releases the underlying <see cref="HttpClient"/> if it was created internally.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_ownsHttpClient)
                _httpClient?.Dispose();
        }

        #endregion
    }

    #region Response Models

    /// <summary>
    /// Represents a response from the Authon API.
    /// </summary>
    public sealed class AuthonResponse
    {
        /// <summary>Whether the API call was successful.</summary>
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        /// <summary>Human-readable message describing the result or error.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>
        /// The response data payload. Structure varies by endpoint.
        /// Use extension methods <see cref="JsonElementExtensions.GetString"/> and
        /// <see cref="JsonElementExtensions.GetInt"/> to access nested properties safely.
        /// </summary>
        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Success
                ? $"[OK] {Message ?? "Success"}"
                : $"[ERROR] {Message ?? "Unknown error"}";
        }
    }

    /// <summary>
    /// Represents a downloadable file entry returned by <see cref="AuthonClient.ListFilesAsync"/>.
    /// </summary>
    public sealed class AuthonFileInfo
    {
        /// <summary>Unique file identifier used for downloading.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>Display name of the file.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>File size in bytes.</summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>Minimum user level required to access this file.</summary>
        [JsonPropertyName("minLevel")]
        public int MinLevel { get; set; }

        /// <summary>Returns a human-readable file size string.</summary>
        public string SizeFormatted
        {
            get
            {
                if (Size < 1024) return $"{Size} B";
                if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
                return $"{Size / (1024.0 * 1024.0):F1} MB";
            }
        }
    }

    #endregion

    #region JSON Extensions

    /// <summary>
    /// Extension methods for safely extracting values from <see cref="JsonElement"/> responses.
    /// </summary>
    public static class JsonElementExtensions
    {
        /// <summary>Safely extracts a string property from a nullable JsonElement.</summary>
        /// <param name="element">The JSON element to read from.</param>
        /// <param name="property">The property name.</param>
        /// <returns>The string value, or <c>null</c> if not found or null-valued.</returns>
        public static string? GetString(this JsonElement? element, string property)
        {
            if (element == null || !element.HasValue) return null;
            if (element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(property, out var val))
            {
                return val.ValueKind == JsonValueKind.Null ? null : val.ToString();
            }
            return null;
        }

        /// <summary>Safely extracts an integer property from a nullable JsonElement.</summary>
        /// <param name="element">The JSON element to read from.</param>
        /// <param name="property">The property name.</param>
        /// <returns>The integer value, or <c>0</c> if not found or not parsable.</returns>
        public static int GetInt(this JsonElement? element, string property)
        {
            if (element == null || !element.HasValue) return 0;
            if (element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(property, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number)
                    return val.TryGetInt32(out int i) ? i : 0;
                if (val.ValueKind == JsonValueKind.String && int.TryParse(val.GetString(), out int parsed))
                    return parsed;
            }
            return 0;
        }

        /// <summary>Safely extracts a boolean property from a nullable JsonElement.</summary>
        /// <param name="element">The JSON element to read from.</param>
        /// <param name="property">The property name.</param>
        /// <returns>The boolean value, or <c>false</c> if not found.</returns>
        public static bool GetBool(this JsonElement? element, string property)
        {
            if (element == null || !element.HasValue) return false;
            if (element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(property, out var val))
            {
                if (val.ValueKind == JsonValueKind.True) return true;
                if (val.ValueKind == JsonValueKind.False) return false;
            }
            return false;
        }

        /// <summary>Safely extracts a long property from a nullable JsonElement.</summary>
        /// <param name="element">The JSON element to read from.</param>
        /// <param name="property">The property name.</param>
        /// <returns>The long value, or <c>0</c> if not found.</returns>
        public static long GetLong(this JsonElement? element, string property)
        {
            if (element == null || !element.HasValue) return 0;
            if (element.Value.ValueKind == JsonValueKind.Object &&
                element.Value.TryGetProperty(property, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number)
                    return val.TryGetInt64(out long l) ? l : 0;
            }
            return 0;
        }

        /// <summary>Gets the raw JSON text of the element (useful for arrays/objects).</summary>
        /// <param name="element">The JSON element.</param>
        /// <returns>The raw JSON string.</returns>
        public static string? GetRawJson(this JsonElement? element)
        {
            if (element == null || !element.HasValue) return null;
            return element.Value.GetRawText();
        }
    }

    #endregion
}
