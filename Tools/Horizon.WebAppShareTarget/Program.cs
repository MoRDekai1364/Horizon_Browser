using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using Clipboard = System.Windows.Forms.Clipboard;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Horizon.WebAppShareTarget;

internal sealed class ShareTargetConfig
{
    public string WebAppId { get; set; } = "";
    public string HorizonExePath { get; set; } = "";
}

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ShareTargetConfig? config = LoadConfig();
        if (config == null || string.IsNullOrWhiteSpace(config.WebAppId) || string.IsNullOrWhiteSpace(config.HorizonExePath))
            return;

        var activationArgs = AppInstance.GetActivatedEventArgs();
        if (activationArgs?.Kind == ActivationKind.ShareTarget &&
            activationArgs is ShareTargetActivatedEventArgs shareArgs)
        {
            HandleShareActivationAsync(shareArgs, config).GetAwaiter().GetResult();
            return;
        }

        LaunchWebApp(config);
    }

    private static ShareTargetConfig? LoadConfig()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "sharetarget.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ShareTargetConfig>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static async Task HandleShareActivationAsync(ShareTargetActivatedEventArgs shareArgs, ShareTargetConfig config)
    {
        ShareOperation shareOperation = shareArgs.ShareOperation;
        shareOperation.ReportStarted();

        try
        {
            DataPackageView data = shareOperation.Data;
            bool handled = false;

            if (data.Contains(StandardDataFormats.StorageItems))
            {
                IReadOnlyList<IStorageItem> items = await data.GetStorageItemsAsync();
                var fileList = new StringCollection();
                foreach (IStorageItem item in items)
                {
                    if (item is StorageFile file && !string.IsNullOrWhiteSpace(file.Path))
                        fileList.Add(file.Path);
                }

                if (fileList.Count > 0)
                {
                    Clipboard.SetFileDropList(fileList);
                    handled = true;
                }
            }

            if (!handled && data.Contains(StandardDataFormats.Bitmap))
            {
                RandomAccessStreamReference bitmapRef = await data.GetBitmapAsync();
                using IRandomAccessStreamWithContentType stream = await bitmapRef.OpenReadAsync();
                string tempFile = Path.Combine(
                    Path.GetTempPath(),
                    $"HorizonWebAppShare_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

                using (FileStream fs = File.Create(tempFile))
                using (Stream input = stream.AsStreamForRead())
                {
                    await input.CopyToAsync(fs);
                }

                var fileList = new StringCollection { tempFile };
                Clipboard.SetFileDropList(fileList);
                handled = true;
            }

            if (!handled && data.Contains(StandardDataFormats.WebLink))
            {
                Uri uri = await data.GetWebLinkAsync();
                string? extraText = data.Contains(StandardDataFormats.Text)
                    ? await data.GetTextAsync()
                    : null;

                string payload = string.IsNullOrWhiteSpace(extraText)
                    ? uri.AbsoluteUri
                    : $"{extraText}\n{uri.AbsoluteUri}";

                Clipboard.SetText(payload);
                handled = true;
            }

            if (!handled && data.Contains(StandardDataFormats.Text))
            {
                string text = await data.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    Clipboard.SetText(text);
                    handled = true;
                }
            }

            LaunchWebApp(config);
            shareOperation.ReportCompleted();
        }
        catch (Exception ex)
        {
            shareOperation.ReportError($"Failed to share to Horizon Web App: {ex.Message}");
        }
    }

    private static void LaunchWebApp(ShareTargetConfig config)
    {
        if (!File.Exists(config.HorizonExePath)) return;

        var psi = new ProcessStartInfo
        {
            FileName = config.HorizonExePath,
            WorkingDirectory = Path.GetDirectoryName(config.HorizonExePath) ?? string.Empty,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--webapp-id=" + config.WebAppId);
        Process.Start(psi);
    }
}
