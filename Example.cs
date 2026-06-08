// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  Authon C# SDK — Interactive Example                                       ║
// ║  Run: dotnet run                                                           ║
// ║  Docs: https://authon.pro/docs                                             ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

#pragma warning disable CS8600 // Converting null literal or possible null value

using System;
using System.IO;
using System.Threading.Tasks;
using AuthonSDK;

namespace AuthonSDK
{
    internal static class Program
    {
        // ═══════════════════════════════════════════════════════════════════════
        //  CONFIGURATION — Replace with your app credentials from the dashboard
        // ═══════════════════════════════════════════════════════════════════════
        private const string APP_ID  = "your-app-id";     // Dashboard → Apps
        private const string API_KEY = "your-api-key";    // Dashboard → Apps → Settings

        static async Task Main(string[] args)
        {
            Console.Title = "Authon SDK Example";
            PrintHeader();

            using var client = new AuthonClient(APP_ID, API_KEY);

            // ─── Step 1: Initialize ─────────────────────────────────────────
            Console.Write("\n[*] Connecting to Authon API... ");
            var initResult = await client.InitAsync();

            if (!initResult.Success)
            {
                PrintError($"Initialization failed: {initResult.Message}");
                WaitForExit();
                return;
            }

            PrintSuccess($"Connected to {client.AppName} v{client.AppVersion}");
            Console.WriteLine($"    HWID: {AuthonClient.GetHWID()}");

            // ─── Step 2: Authenticate ───────────────────────────────────────
            Console.WriteLine("\n╔══════════════════════════════════════╗");
            Console.WriteLine("║  Authentication                      ║");
            Console.WriteLine("╠══════════════════════════════════════╣");
            Console.WriteLine("║  [1] Login (Username + Password)     ║");
            Console.WriteLine("║  [2] License Key                     ║");
            Console.WriteLine("║  [3] Register New Account            ║");
            Console.WriteLine("╚══════════════════════════════════════╝");
            Console.Write("\n> Select option: ");

            string authChoice = Console.ReadLine()?.Trim() ?? "1";
            AuthonResponse authResult;

            switch (authChoice)
            {
                case "1":
                    Console.Write("  Username: ");
                    string username = Console.ReadLine()?.Trim() ?? "";
                    Console.Write("  Password: ");
                    string password = ReadPassword();
                    authResult = await client.LoginAsync(username, password);
                    break;

                case "2":
                    Console.Write("  License Key: ");
                    string licenseKey = Console.ReadLine()?.Trim() ?? "";
                    authResult = await client.LicenseAsync(licenseKey);
                    break;

                case "3":
                    Console.Write("  Username: ");
                    string regUser = Console.ReadLine()?.Trim() ?? "";
                    Console.Write("  Password: ");
                    string regPass = ReadPassword();
                    Console.Write("  License Key: ");
                    string regKey = Console.ReadLine()?.Trim() ?? "";
                    authResult = await client.RegisterAsync(regUser, regPass, regKey);
                    if (authResult.Success)
                    {
                        PrintSuccess("Account created! Logging in...");
                        authResult = await client.LoginAsync(regUser, regPass);
                    }
                    break;

                default:
                    PrintError("Invalid option.");
                    WaitForExit();
                    return;
            }

            if (!authResult.Success)
            {
                PrintError($"Authentication failed: {authResult.Message}");
                WaitForExit();
                return;
            }

            // ─── Authentication Success ─────────────────────────────────────
            Console.WriteLine();
            PrintSuccess("Authentication successful!");
            Console.WriteLine($"    Username:     {client.Username ?? "N/A (license-only)"}");
            Console.WriteLine($"    Level:        {client.Level}");
            Console.WriteLine($"    Subscription: {client.Subscription ?? "None"}");
            Console.WriteLine($"    Expires:      {client.ExpiresAt ?? "Lifetime"}");

            // ─── Step 3: Interactive Menu ────────────────────────────────────
            bool running = true;
            while (running)
            {
                Console.WriteLine("\n╔══════════════════════════════════════╗");
                Console.WriteLine("║  Features Menu                       ║");
                Console.WriteLine("╠══════════════════════════════════════╣");
                Console.WriteLine("║  [1] Get App Variable                ║");
                Console.WriteLine("║  [2] Set User Variable               ║");
                Console.WriteLine("║  [3] Get User Variable               ║");
                Console.WriteLine("║  [4] List Files                      ║");
                Console.WriteLine("║  [5] Download File                   ║");
                Console.WriteLine("║  [6] Send Log                        ║");
                Console.WriteLine("║  [7] Fetch Online Users              ║");
                Console.WriteLine("║  [8] Fetch Stats                     ║");
                Console.WriteLine("║  [9] Check Blacklist                 ║");
                Console.WriteLine("║  [10] Redeem Referral                ║");
                Console.WriteLine("║  [11] Validate Session               ║");
                Console.WriteLine("║  [0] Logout & Exit                   ║");
                Console.WriteLine("╚══════════════════════════════════════╝");
                Console.Write("\n> Select option: ");

                string choice = Console.ReadLine()?.Trim() ?? "0";

                switch (choice)
                {
                    case "1":
                        await DemoGetVar(client);
                        break;
                    case "2":
                        await DemoSetVar(client);
                        break;
                    case "3":
                        await DemoGetUserVar(client);
                        break;
                    case "4":
                        await DemoListFiles(client);
                        break;
                    case "5":
                        await DemoDownloadFile(client);
                        break;
                    case "6":
                        await DemoLog(client);
                        break;
                    case "7":
                        await DemoFetchOnline(client);
                        break;
                    case "8":
                        await DemoFetchStats(client);
                        break;
                    case "9":
                        await DemoCheckBlacklist(client);
                        break;
                    case "10":
                        await DemoRedeemReferral(client);
                        break;
                    case "11":
                        await DemoCheckSession(client);
                        break;
                    case "0":
                        running = false;
                        break;
                    default:
                        PrintError("Invalid option.");
                        break;
                }
            }

            // ─── Logout ─────────────────────────────────────────────────────
            Console.Write("\n[*] Logging out... ");
            var logoutResult = await client.LogoutAsync();
            if (logoutResult.Success)
                PrintSuccess("Session terminated.");
            else
                PrintError(logoutResult.Message);

            WaitForExit();
        }

        #region Feature Demos

        private static async Task DemoGetVar(AuthonClient client)
        {
            Console.Write("  Variable key: ");
            string key = Console.ReadLine()?.Trim() ?? "";
            string value = await client.GetVarAsync(key);
            if (value != null)
                PrintSuccess($"{key} = {value}");
            else
                PrintError("Variable not found or access denied.");
        }

        private static async Task DemoSetVar(AuthonClient client)
        {
            Console.Write("  Variable key: ");
            string key = Console.ReadLine()?.Trim() ?? "";
            Console.Write("  Value: ");
            string value = Console.ReadLine()?.Trim() ?? "";
            var result = await client.SetVarAsync(key, value);
            if (result.Success)
                PrintSuccess("Variable saved.");
            else
                PrintError(result.Message);
        }

        private static async Task DemoGetUserVar(AuthonClient client)
        {
            Console.Write("  Variable key: ");
            string key = Console.ReadLine()?.Trim() ?? "";
            string value = await client.GetUserVarAsync(key);
            if (value != null)
                PrintSuccess($"{key} = {value}");
            else
                PrintError("Variable not found.");
        }

        private static async Task DemoListFiles(AuthonClient client)
        {
            var files = await client.ListFilesAsync();
            if (files.Count == 0)
            {
                PrintError("No files available for your level.");
                return;
            }

            Console.WriteLine($"\n  Available files ({files.Count}):");
            Console.WriteLine("  ┌────────────────────────────────────────────────────────┐");
            Console.WriteLine("  │ #   Name                          Size       Level     │");
            Console.WriteLine("  ├────────────────────────────────────────────────────────┤");
            for (int i = 0; i < files.Count; i++)
            {
                string name = files[i].Name.Length > 28 ? files[i].Name[..25] + "..." : files[i].Name;
                Console.WriteLine($"  │ {i + 1,-3} {name,-30} {files[i].SizeFormatted,-10} Lv.{files[i].MinLevel,-4}│");
            }
            Console.WriteLine("  └────────────────────────────────────────────────────────┘");
        }

        private static async Task DemoDownloadFile(AuthonClient client)
        {
            var files = await client.ListFilesAsync();
            if (files.Count == 0)
            {
                PrintError("No files available.");
                return;
            }

            Console.WriteLine("  Available files:");
            for (int i = 0; i < files.Count; i++)
                Console.WriteLine($"    [{i + 1}] {files[i].Name} ({files[i].SizeFormatted})");

            Console.Write("  Select file number: ");
            if (!int.TryParse(Console.ReadLine()?.Trim(), out int index) || index < 1 || index > files.Count)
            {
                PrintError("Invalid selection.");
                return;
            }

            var file = files[index - 1];
            Console.Write($"  Downloading {file.Name}... ");

            byte[] data = await client.DownloadFileAsync(file.Id);
            if (data != null && data.Length > 0)
            {
                string outputPath = Path.Combine(Directory.GetCurrentDirectory(), file.Name);
                File.WriteAllBytes(outputPath, data);
                PrintSuccess($"Saved to {outputPath} ({data.Length:N0} bytes)");
            }
            else
            {
                PrintError("Download failed or file is empty.");
            }
        }

        private static async Task DemoLog(AuthonClient client)
        {
            Console.Write("  Log message: ");
            string message = Console.ReadLine()?.Trim() ?? "";
            var result = await client.LogAsync(message);
            if (result.Success)
                PrintSuccess("Log recorded on dashboard.");
            else
                PrintError(result.Message);
        }

        private static async Task DemoFetchOnline(AuthonClient client)
        {
            var result = await client.FetchOnlineAsync();
            if (!result.Success)
            {
                PrintError(result.Message);
                return;
            }

            int count = result.Data.GetInt("count");
            PrintSuccess($"Online users: {count}");

            // Try to list usernames
            if (result.Data.HasValue &&
                result.Data.Value.ValueKind == System.Text.Json.JsonValueKind.Object &&
                result.Data.Value.TryGetProperty("users", out var usersElement) &&
                usersElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var user in usersElement.EnumerateArray())
                {
                    string name = user.TryGetProperty("username", out var un) ? un.GetString() ?? "Unknown" : "Unknown";
                    Console.WriteLine($"    • {name}");
                }
            }
        }

        private static async Task DemoFetchStats(AuthonClient client)
        {
            var result = await client.FetchStatsAsync();
            if (!result.Success)
            {
                PrintError(result.Message);
                return;
            }

            Console.WriteLine("\n  ┌─────────────────────────────────────┐");
            Console.WriteLine($"  │ Total Users:   {result.Data.GetInt("totalUsers"),-20}│");
            Console.WriteLine($"  │ Online Users:  {result.Data.GetInt("onlineUsers"),-20}│");
            Console.WriteLine($"  │ Total Keys:    {result.Data.GetInt("totalKeys"),-20}│");
            Console.WriteLine($"  │ App Version:   {result.Data.GetString("appVersion") ?? "N/A",-20}│");
            Console.WriteLine("  └─────────────────────────────────────┘");
        }

        private static async Task DemoCheckBlacklist(AuthonClient client)
        {
            Console.Write("  Check HWID? (y/n): ");
            string checkHwid = Console.ReadLine()?.Trim()?.ToLower() ?? "y";

            string hwid = null;
            if (checkHwid == "y")
                hwid = AuthonClient.GetHWID();

            var result = await client.CheckBlacklistAsync(hwid: hwid);
            if (!result.Success)
            {
                PrintError(result.Message);
                return;
            }

            bool blacklisted = result.Data.GetBool("blacklisted");
            if (blacklisted)
            {
                string reason = result.Data.GetString("reason") ?? "No reason given";
                PrintError($"BLACKLISTED — Reason: {reason}");
            }
            else
            {
                PrintSuccess("Not blacklisted. You're clean!");
            }
        }

        private static async Task DemoRedeemReferral(AuthonClient client)
        {
            Console.Write("  Referral code: ");
            string code = Console.ReadLine()?.Trim() ?? "";
            var result = await client.RedeemReferralAsync(code);
            if (result.Success)
                PrintSuccess($"Referral redeemed! {result.Message}");
            else
                PrintError(result.Message);
        }

        private static async Task DemoCheckSession(AuthonClient client)
        {
            Console.Write("  Checking session validity... ");
            bool valid = await client.CheckAsync();
            if (valid)
                PrintSuccess("Session is valid.");
            else
                PrintError("Session is invalid or expired.");
        }

        #endregion

        #region UI Helpers

        private static void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
    ╔═══════════════════════════════════════════════════╗
    ║                                                   ║
    ║        █████╗ ██╗   ██╗████████╗██╗  ██╗         ║
    ║       ██╔══██╗██║   ██║╚══██╔══╝██║  ██║         ║
    ║       ███████║██║   ██║   ██║   ███████║         ║
    ║       ██╔══██║██║   ██║   ██║   ██╔══██║         ║
    ║       ██║  ██║╚██████╔╝   ██║   ██║  ██║         ║
    ║       ╚═╝  ╚═╝ ╚═════╝    ╚═╝   ╚═╝  ╚═╝         ║
    ║                                                   ║
    ║         Authon SDK — C# Example v2.0              ║
    ║         https://authon.pro                        ║
    ║                                                   ║
    ╚═══════════════════════════════════════════════════╝");
            Console.ResetColor();
        }

        private static void PrintSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[+] {message}");
            Console.ResetColor();
        }

        private static void PrintError(string? message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[-] {message ?? "Unknown error"}");
            Console.ResetColor();
        }

        private static string ReadPassword()
        {
            var password = new System.Text.StringBuilder();
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    password.Append(key.KeyChar);
                    Console.Write("*");
                }
            }
            Console.WriteLine();
            return password.ToString();
        }

        private static void WaitForExit()
        {
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey(true);
        }

        #endregion
    }
}
