using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using Windows.Management.Deployment;

namespace Horizon.Stealth.Services;

public static class WebAppShareTargetService
{
    private const string LogTag = "WEBAPP_SHARE";
    private const string ToolExeName = "Horizon.WebAppShareTarget.exe";
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string GetShareTargetDir(string webAppId) =>
        Path.Combine(WebAppService.GetAppDir(webAppId), "ShareTarget");

    public static string BuildPackageName(string webAppId)
    {
        var sb = new StringBuilder("HorizonBrowser.WebApp.");
        foreach (char c in webAppId)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-')
                sb.Append(c);
            else
                sb.Append('-');
            if (sb.Length >= 50) break;
        }
        return sb.ToString().TrimEnd('.', '-');
    }

    public static bool SetEnabled(string id, bool enable, List<string> errors)
    {
        var manifest = WebAppService.Load(id);
        if (manifest == null)
        {
            errors.Add("Web App not found.");
            return false;
        }

        bool ok = enable ? Register(manifest, errors) : Unregister(manifest, errors);
        if (ok)
        {
            manifest.RegisterInShareMenu = enable;
            manifest.ShareTargetPackageName = enable ? BuildPackageName(manifest.Id) : "";
            WebAppService.SaveManifest(manifest);
            WebAppService.NotifyChanged();
        }
        return ok;
    }

    public static bool Register(WebAppManifest manifest, List<string> errors)
    {
        try
        {
            string horizonExe = WebAppService.ResolveExePath();
            if (string.IsNullOrEmpty(horizonExe) || !File.Exists(horizonExe))
            {
                errors.Add("Horizon executable path could not be resolved.");
                return false;
            }

            string baseDir = Path.GetDirectoryName(horizonExe) ?? AppContext.BaseDirectory;
            string toolsDir = Path.Combine(baseDir, "Tools");
            string sourceDir = File.Exists(Path.Combine(toolsDir, ToolExeName)) ? toolsDir : baseDir;

            if (!File.Exists(Path.Combine(sourceDir, ToolExeName)))
            {
                errors.Add("Share Target helper binary (" + ToolExeName + ") was not found.");
                return false;
            }

            string targetDir = GetShareTargetDir(manifest.Id);
            string assetsDir = Path.Combine(targetDir, "Assets");
            Directory.CreateDirectory(assetsDir);

            foreach (string file in Directory.GetFiles(sourceDir, "Horizon.WebAppShareTarget.*"))
            {
                string dest = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }

            string configPath = Path.Combine(targetDir, "sharetarget.json");
            File.WriteAllText(
                configPath,
                JsonSerializer.Serialize(new
                {
                    WebAppId = manifest.Id,
                    HorizonExePath = horizonExe
                }, JsonOpts),
                new UTF8Encoding(false));

            string iconPath = string.IsNullOrEmpty(manifest.Icon)
                ? ""
                : Path.Combine(WebAppService.GetAppDir(manifest.Id), manifest.Icon);
            GenerateAssets(iconPath, horizonExe, assetsDir);

            string packageName = BuildPackageName(manifest.Id);
            string safeName = SecurityElement.Escape(manifest.Name) ?? "Horizon Web App";
            string manifestPath = Path.Combine(targetDir, "AppxManifest.xml");
            string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Package
  xmlns=""http://schemas.microsoft.com/appx/manifest/foundation/windows10""
  xmlns:uap=""http://schemas.microsoft.com/appx/manifest/uap/windows10""
  xmlns:rescap=""http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities""
  IgnorableNamespaces=""uap rescap"">

  <Identity
    Name=""{packageName}""
    Publisher=""CN=HorizonBrowserLocal""
    Version=""1.0.0.0""
    ProcessorArchitecture=""x64"" />

  <Properties>
    <DisplayName>{safeName}</DisplayName>
    <PublisherDisplayName>Horizon Browser</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name=""Windows.Desktop"" MinVersion=""10.0.19041.0"" MaxVersionTested=""10.0.26100.0"" />
  </Dependencies>

  <Resources>
    <Resource Language=""en-us"" />
  </Resources>

  <Applications>
    <Application
      Id=""App""
      Executable=""{ToolExeName}""
      EntryPoint=""Windows.FullTrustApplication"">
      <uap:VisualElements
        DisplayName=""{safeName}""
        Description=""Share links, text, and files to {safeName}""
        BackgroundColor=""transparent""
        Square150x150Logo=""Assets\Square150x150Logo.png""
        Square44x44Logo=""Assets\Square44x44Logo.png"" />

      <Extensions>
        <uap:Extension Category=""windows.shareTarget"">
          <uap:ShareTarget Description=""Share to {safeName}"">
            <uap:SupportedFileTypes>
              <uap:SupportsAnyFileType />
            </uap:SupportedFileTypes>
            <uap:DataFormat>Text</uap:DataFormat>
            <uap:DataFormat>Uri</uap:DataFormat>
            <uap:DataFormat>Bitmap</uap:DataFormat>
          </uap:ShareTarget>
        </uap:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name=""runFullTrust"" />
  </Capabilities>
</Package>
";
            File.WriteAllText(manifestPath, xml, new UTF8Encoding(false));

            var pm = new PackageManager();
            var deployOp = pm.RegisterPackageAsync(
                new Uri(manifestPath),
                null,
                DeploymentOptions.DevelopmentMode);
            var result = deployOp.AsTask().GetAwaiter().GetResult();

            if (!string.IsNullOrEmpty(result.ErrorText) || result.ExtendedErrorCode != null)
            {
                string msg = string.IsNullOrEmpty(result.ErrorText)
                    ? result.ExtendedErrorCode?.Message ?? "Windows Share Target registration failed."
                    : result.ErrorText;
                errors.Add(msg);
                LogService.Write(LogTag, $"Register failed for {manifest.Id}: {msg}");
                return false;
            }

            manifest.RegisterInShareMenu = true;
            manifest.ShareTargetPackageName = packageName;
            LogService.Write(LogTag, $"Registered Share Target for {manifest.Id} ({packageName}).");
            return true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppShareTargetService.Register");
            errors.Add("Share menu registration failed (ensure Windows Developer Mode is enabled): " + ex.Message);
            return false;
        }
    }

    public static bool Unregister(WebAppManifest manifest, List<string> errors)
    {
        try
        {
            string packageName = string.IsNullOrWhiteSpace(manifest.ShareTargetPackageName)
                ? BuildPackageName(manifest.Id)
                : manifest.ShareTargetPackageName;

            var pm = new PackageManager();
            var packages = pm.FindPackagesForUser(string.Empty)
                .Where(p => string.Equals(p.Id.Name, packageName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var pkg in packages)
            {
                var removeOp = pm.RemovePackageAsync(pkg.Id.FullName);
                var result = removeOp.AsTask().GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(result.ErrorText) || result.ExtendedErrorCode != null)
                {
                    string msg = string.IsNullOrEmpty(result.ErrorText)
                        ? result.ExtendedErrorCode?.Message ?? "Failed to remove Share Target package."
                        : result.ErrorText;
                    errors.Add(msg);
                    return false;
                }
            }

            string targetDir = GetShareTargetDir(manifest.Id);
            if (Directory.Exists(targetDir))
            {
                try { Directory.Delete(targetDir, true); }
                catch { /* ignore locked temp files */ }
            }

            manifest.RegisterInShareMenu = false;
            manifest.ShareTargetPackageName = "";
            LogService.Write(LogTag, $"Unregistered Share Target for {manifest.Id} ({packageName}).");
            return true;
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "WebAppShareTargetService.Unregister");
            errors.Add(ex.Message);
            return false;
        }
    }

    private static void GenerateAssets(string iconPath, string fallbackExePath, string assetsDir)
    {
        using Bitmap? source = LoadSourceBitmap(iconPath, fallbackExePath);
        if (source == null) return;

        SaveResizedPng(source, 44, Path.Combine(assetsDir, "Square44x44Logo.png"));
        SaveResizedPng(source, 150, Path.Combine(assetsDir, "Square150x150Logo.png"));
        SaveResizedPng(source, 50, Path.Combine(assetsDir, "StoreLogo.png"));
    }

    private static Bitmap? LoadSourceBitmap(string iconPath, string fallbackExePath)
    {
        try
        {
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                using var ico = new Icon(iconPath, 256, 256);
                return ico.ToBitmap();
            }
        }
        catch { }

        try
        {
            if (!string.IsNullOrEmpty(fallbackExePath) && File.Exists(fallbackExePath))
            {
                using var ico = Icon.ExtractAssociatedIcon(fallbackExePath);
                if (ico != null) return ico.ToBitmap();
            }
        }
        catch { }

        return null;
    }

    private static void SaveResizedPng(Bitmap source, int size, string outputPath)
    {
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(source, 0, 0, size, size);
        bmp.Save(outputPath, ImageFormat.Png);
    }
}
