using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Horizon.SessionImporter;

public class ExtractedCookie
{
    public string Host { get; set; } = "";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Path { get; set; } = "/";
    public bool IsSecure { get; set; }
    public bool IsHttpOnly { get; set; }
}

public class Program
{
    public static int Main(string[] args)
    {
        string? browser = null;
        string? userDataPath = null;
        string targetDomain = "";
        string? outputPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--browser", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                browser = args[++i];
            else if (arg.Equals("--user-data-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                userDataPath = args[++i];
            else if (arg.Equals("--domain", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                targetDomain = args[++i];
            else if (arg.Equals("--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                outputPath = args[++i];
        }

        if (string.IsNullOrEmpty(userDataPath) && !string.IsNullOrEmpty(browser))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            userDataPath = browser.Equals("edge", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(localAppData, "Microsoft", "Edge", "User Data")
                : Path.Combine(localAppData, "Google", "Chrome", "User Data");
        }

        if (string.IsNullOrEmpty(userDataPath) || !Directory.Exists(userDataPath))
        {
            WriteResults(new List<ExtractedCookie>(), outputPath);
            return 0;
        }

        var results = Harvest(userDataPath, targetDomain);
        WriteResults(results, outputPath);
        return 0;
    }

    private static void WriteResults(List<ExtractedCookie> cookies, string? outputPath)
    {
        string json = JsonSerializer.Serialize(cookies, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrEmpty(outputPath))
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outputPath, json, Encoding.UTF8);
        }
        else
        {
            Console.WriteLine(json);
        }
    }

    public static List<ExtractedCookie> Harvest(string userDataPath, string targetDomain)
    {
        var results = new List<ExtractedCookie>();
        string tempDbPath = Path.Combine(Path.GetTempPath(), $"horizon_session_{Guid.NewGuid():N}.db");

        try
        {
            byte[]? masterKey = GetMasterKey(Path.Combine(userDataPath, "Local State"));
            if (masterKey == null) return results;

            string dbPath = Path.Combine(userDataPath, "Default", "Network", "Cookies");
            if (!File.Exists(dbPath))
                dbPath = Path.Combine(userDataPath, "Default", "Cookies");

            if (!File.Exists(dbPath)) return results;

            File.Copy(dbPath, tempDbPath, true);

            using var connection = new SqliteConnection($"Data Source={tempDbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT host_key, name, encrypted_value, path, is_secure, is_httponly FROM cookies WHERE host_key LIKE $domain";
            command.Parameters.AddWithValue("$domain", $"%{targetDomain}%");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var host = reader.GetString(0);
                var name = reader.GetString(1);
                var encryptedValue = (byte[])reader["encrypted_value"];
                var path = reader.GetString(3);
                var isSecure = reader.GetBoolean(4);
                var isHttpOnly = reader.GetBoolean(5);

                string decryptedValue = DecryptValue(encryptedValue, masterKey);
                if (!string.IsNullOrEmpty(decryptedValue))
                {
                    results.Add(new ExtractedCookie
                    {
                        Host = host,
                        Name = name,
                        Value = decryptedValue,
                        Path = path,
                        IsSecure = isSecure,
                        IsHttpOnly = isHttpOnly
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempDbPath))
            {
                try { File.Delete(tempDbPath); } catch { }
            }
        }

        return results;
    }

    private static byte[]? GetMasterKey(string localStatePath)
    {
        try
        {
            if (!File.Exists(localStatePath)) return null;

            string json = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("os_crypt", out var osCrypt) &&
                osCrypt.TryGetProperty("encrypted_key", out var keyElement))
            {
                string base64Key = keyElement.GetString() ?? "";
                byte[] encryptedKey = Convert.FromBase64String(base64Key);

                if (encryptedKey.Length > 5)
                {
                    byte[] rawKey = encryptedKey.Skip(5).ToArray();
                    return ProtectedData.Unprotect(rawKey, null, DataProtectionScope.CurrentUser);
                }
            }
        }
        catch { }
        return null;
    }

    private static string DecryptValue(byte[] encryptedData, byte[] key)
    {
        try
        {
            if (encryptedData.Length < 3 || encryptedData[0] != 'v' || encryptedData[1] != '1' || encryptedData[2] != '0')
                return "";

            int nonceSize = 12;
            int tagSize = 16;
            int prefixSize = 3;

            if (encryptedData.Length < prefixSize + nonceSize + tagSize) return "";

            byte[] nonce = encryptedData.Skip(prefixSize).Take(nonceSize).ToArray();
            byte[] ciphertext = encryptedData.Skip(prefixSize + nonceSize).Take(encryptedData.Length - (prefixSize + nonceSize + tagSize)).ToArray();
            byte[] tag = encryptedData.Skip(encryptedData.Length - tagSize).Take(tagSize).ToArray();

            byte[] plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, tagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return "";
        }
    }
}
