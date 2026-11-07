using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Stealth.Services;

/// <summary>
/// Wallpaper collections stored in user data: &lt;UserDataRoot&gt;\Wallpapers\&lt;Collection&gt;\*.
/// Phase 1: storage only. Nothing calls this yet, so browser behavior is unchanged.
/// </summary>
public static class WallpaperLibraryService
{
    // Same set the old code accepted (HomePageView.WallpaperImageExts + WallpaperVideoExts).
    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".mp4" };

    public static MediaLibrary Library { get; } = new(
        () => Path.Combine(ConfigService.UserDataRoot, "Wallpapers"),
        Extensions);

    /// <summary>The old, exe-side folder that ships with the app.</summary>
    public static string LegacyDefaultFolder
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallpapers");

    /// <summary>Custom folder the user picked in the old settings ("" if none). Not auto-migrated
    /// (could be many GB); Phase 6 offers to import it as a collection.</summary>
    public static string LegacyCustomFolder => SettingsService.Current.WallpaperFolderPath ?? "";

    public static bool NeedsLegacyMigration() => Library.NeedsMigration(LegacyDefaultFolder);

    public static Task<MediaImportResult> MigrateLegacyAsync(
        IProgress<MediaProgress>? progress = null, CancellationToken ct = default)
        => Library.MigrateFromFolderAsync(LegacyDefaultFolder, progress, ct);
}
