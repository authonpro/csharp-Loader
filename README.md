# Authon C# SDK

<p align="center">
  <img src="https://authon.pro/logo.png" alt="Authon" width="100" />
  <br/><br/>
  <strong>Official C# SDK for Authon — Software Licensing & Authentication Platform</strong>
  <br/><br/>
  <a href="https://authon.pro"><img src="https://img.shields.io/badge/Website-authon.pro-blue?style=for-the-badge" alt="Website" /></a>
  <a href="https://authon.pro/docs"><img src="https://img.shields.io/badge/Docs-API%20Reference-green?style=for-the-badge" alt="Docs" /></a>
  <a href="https://discord.gg/MTY79JDFm6"><img src="https://img.shields.io/badge/Discord-Join%20Server-5865F2?style=for-the-badge&logo=discord&logoColor=white" alt="Discord" /></a>
  <a href="https://authon.pro/status"><img src="https://img.shields.io/badge/Status-Live-brightgreen?style=for-the-badge" alt="Status" /></a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-6%20|%207%20|%208-512BD4?style=flat-square&logo=dotnet" alt=".NET" />
  <img src="https://img.shields.io/badge/.NET%20Framework-4.6.1+-512BD4?style=flat-square" alt=".NET Framework" />
  <img src="https://img.shields.io/badge/Dependencies-None*-success?style=flat-square" alt="Dependencies" />
  <img src="https://img.shields.io/badge/License-MIT-yellow?style=flat-square" alt="License" />
</p>

---

## Features

- 🔐 **User Authentication** — Login with username/password or license key only
- 🎫 **License Management** — Activate, validate, and track license keys
- 👤 **User Registration** — Create accounts with license key activation
- 📊 **Session Management** — Heartbeat validation, multi-device support
- 💾 **Variable Storage** — App-level and user-level key-value storage
- 📁 **File Downloads** — Level-gated binary file delivery
- 📈 **Analytics** — Online users, app statistics, activity logging
- 🛡️ **Security** — Blacklist checking, HWID lock, referral system
- 🖥️ **HWID Generation** — Automatic hardware fingerprinting via WMI

---

## Requirements

| Requirement | Minimum |
|-------------|---------|
| .NET | 6.0+ (or .NET Framework 4.6.1+) |
| OS | Windows (for HWID generation via WMI) |
| NuGet | `System.Management` (included in project) |
| External packages | **None** — uses built-in `System.Net.Http` + `System.Text.Json` |

---

## Installation

### Option 1: Copy into your project
1. Copy `Authon.cs` into your project
2. Add `System.Management` NuGet package (for HWID)
3. Add `using AuthonSDK;` to your files

### Option 2: Build from solution
```bash
git clone https://github.com/authonpro/sdk-csharp
cd sdk-csharp
dotnet build
```

### Option 3: Add as project reference
```xml
<ProjectReference Include="path/to/AuthonSDK.csproj" />
```

---

## Quick Start

```csharp
using AuthonSDK;

// Create client
using var client = new AuthonClient("your-app-id", "your-api-key");

// Initialize (validates credentials, fetches app info)
var init = await client.InitAsync();
if (!init.Success) { Console.WriteLine(init.Message); return; }
Console.WriteLine($"Connected to {client.AppName} v{client.AppVersion}");

// Authenticate
var login = await client.LoginAsync("username", "password");
if (login.Success)
{
    Console.WriteLine($"Welcome {client.Username}! Level: {client.Level}");
    Console.WriteLine($"Subscription: {client.Subscription ?? "None"}");
    Console.WriteLine($"Expires: {client.ExpiresAt ?? "Lifetime"}");
}

// Use features...
string motd = await client.GetVarAsync("welcome_message");
await client.LogAsync("User logged in from C# app");

// Logout when done
await client.LogoutAsync();
```

---

## API Reference

### Initialization

| Method | Description | Returns |
|--------|-------------|---------|
| `InitAsync(hash?)` | Connect and validate app credentials | `AuthonResponse` |

### Authentication

| Method | Description | Returns |
|--------|-------------|---------|
| `LoginAsync(username, password, hwid?)` | Login with credentials | `AuthonResponse` |
| `LicenseAsync(licenseKey, hwid?)` | Login with license key only | `AuthonResponse` |
| `RegisterAsync(username, password, licenseKey, hwid?)` | Register new account | `AuthonResponse` |

### Session Management

| Method | Description | Returns |
|--------|-------------|---------|
| `CheckAsync()` | Validate current session (heartbeat) | `bool` |
| `LogoutAsync()` | End session and clear local state | `AuthonResponse` |

### Variables

| Method | Description | Returns |
|--------|-------------|---------|
| `GetVarAsync(key)` | Get app-level variable | `string?` |
| `SetVarAsync(key, value)` | Set user-level variable | `AuthonResponse` |
| `GetUserVarAsync(key)` | Get user-level variable | `string?` |

### Files

| Method | Description | Returns |
|--------|-------------|---------|
| `ListFilesAsync()` | List files for user's level | `List<AuthonFileInfo>` |
| `DownloadFileAsync(fileId)` | Download file (POST method) | `byte[]?` |
| `DownloadFileViaGetAsync(fileId)` | Download file (GET method) | `byte[]?` |

### Analytics & Logging

| Method | Description | Returns |
|--------|-------------|---------|
| `LogAsync(message)` | Send activity log to dashboard | `AuthonResponse` |
| `FetchOnlineAsync()` | Get online users count and list | `AuthonResponse` |
| `FetchStatsAsync()` | Get app statistics | `AuthonResponse` |

### Security

| Method | Description | Returns |
|--------|-------------|---------|
| `CheckBlacklistAsync(ip?, hwid?)` | Check if IP/HWID is banned | `AuthonResponse` |
| `RedeemReferralAsync(code)` | Redeem a referral code | `AuthonResponse` |

### Utilities

| Method | Description | Returns |
|--------|-------------|---------|
| `AuthonClient.GetHWID()` | Generate machine hardware ID | `string` |

---

## Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsInitialized` | `bool` | Whether `InitAsync()` succeeded |
| `IsAuthenticated` | `bool` | Whether user has active session |
| `SessionToken` | `string?` | Current session token |
| `Username` | `string?` | Authenticated username |
| `Level` | `int` | User access level |
| `Subscription` | `string?` | Subscription plan name |
| `ExpiresAt` | `string?` | Expiration date (ISO 8601) |
| `AppName` | `string?` | Application name |
| `AppVersion` | `string?` | Application version |
| `UpdateUrl` | `string?` | Update URL (if configured) |

---

## Error Handling

All API calls return an `AuthonResponse` with `Success` and `Message` properties:

```csharp
var result = await client.LoginAsync("user", "wrong_password");
if (!result.Success)
{
    switch (result.Message)
    {
        case "Invalid credentials":
            Console.WriteLine("Wrong username or password");
            break;
        case "Account banned":
            Console.WriteLine("Your account has been banned");
            break;
        case "Hardware ID mismatch":
            Console.WriteLine("This device is not authorized");
            break;
        case "Subscription expired":
            Console.WriteLine("Your subscription has expired");
            break;
        default:
            Console.WriteLine($"Error: {result.Message}");
            break;
    }
}
```

### Common Error Messages

| Error Message | Cause |
|--------------|-------|
| `Invalid appId or apiKey` | Wrong app credentials |
| `Application is paused` | App disabled from dashboard |
| `Invalid credentials` | Wrong username or password |
| `Account banned` | User is banned |
| `Account is frozen. Contact admin to unfreeze.` | Account frozen by admin |
| `Hardware ID mismatch` | HWID doesn't match registered device |
| `HWID cooldown active` | Too soon after HWID reset |
| `Subscription expired` | License time ran out |
| `Invalid or already used license key` | License consumed or nonexistent |
| `Username already exists` | Duplicate username on register |
| `Invalid license key` | License key not found |
| `License is banned` | License revoked by admin |
| `License has expired` | License time expired |
| `Invalid session` | Session expired or invalidated |
| `Variable not found` | Key doesn't exist |
| `File not found` | File ID doesn't exist |
| `Insufficient access level for this file` | User level too low |
| `IP address banned` | IP on blacklist |
| `Hardware banned` | HWID on blacklist |
| `VPN/Proxy connections are not allowed` | VPN detected (if enabled) |
| `Connection failed` | Network error |
| `Request timed out` | Server didn't respond in time |

---

## Advanced Usage

### Custom HWID

```csharp
// Use your own HWID instead of auto-generated
string myHwid = "my-custom-hardware-id";
var result = await client.LoginAsync("user", "pass", hwid: myHwid);
```

### Dependency Injection / HttpClient Pooling

```csharp
// Share an HttpClient (recommended for high-throughput scenarios)
var httpClient = new HttpClient();
using var client = new AuthonClient("app-id", "api-key", httpClient);
```

### Cancellation Token Support

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var result = await client.LoginAsync("user", "pass", cancellationToken: cts.Token);
```

### Hash Verification

```csharp
// If your app has hash check enabled on the dashboard
string appHash = ComputeFileHash("MyApp.exe");
var init = await client.InitAsync(hash: appHash);
```

---

## Project Structure

```
sdk/csharp/
├── Authon.cs           # SDK source — all API methods, models, HWID generation
├── Example.cs          # Interactive demo with all features
├── AuthonSDK.csproj    # .NET 8 project file
├── AuthonSDK.sln       # Visual Studio solution
└── README.md           # This file
```

---

## Running the Example

```bash
# 1. Edit Example.cs — set your APP_ID and API_KEY
# 2. Build and run
dotnet run

# Or open AuthonSDK.sln in Visual Studio and press F5
```

---

## Links

| Resource | URL |
|----------|-----|
| 🌐 Website | https://authon.pro |
| 📖 Documentation | https://authon.pro/docs |
| 💬 Discord | https://discord.gg/MTY79JDFm6 |
| 📊 Status Page | https://authon.pro/status |
| 🔗 API Endpoint | https://api.authon.pro/v1 |
| 🏥 API Health | https://api.authon.pro/health |
| 🐙 GitHub | https://github.com/authonpro |

---

## License

MIT — see [LICENSE](LICENSE) for details.

---

<p align="center">
  Made with ❤️ by <a href="https://authon.pro">Authon</a>
</p>
