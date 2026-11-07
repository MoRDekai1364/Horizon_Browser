using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Stealth.Services;

/// <summary>
/// Startup video collections stored in user data: &lt;UserDataRoot&gt;\StartupVideos\&lt;Collection&gt;\*.mp4
/// Phase 1: storage only. StartupVideoWindow still reads start_videos\ until Phase 7.
/// </summary>
public static class StartupVideoLibraryService
{
    public static readonly string[] Extensions = { ".mp4" };

    public static MediaLibrary Library { get; } = new(
        () => Path.Combine(ConfigService.UserDataRoot, "StartupVideos"),
        Extensions);

    /// <summary>The old, exe-side folder (Book / Entangle / Pulse sub-folders become collections).</summary>
    public static string LegacyDefaultFolder
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "start_videos");

    public static bool NeedsLegacyMigration() => Library.NeedsMigration(LegacyDefaultFolder);

    public static Task<MediaImportResult> MigrateLegacyAsync(
        IProgress<MediaProgress>? progress = null, CancellationToken ct = default)
        => Library.MigrateFromFolderAsync(LegacyDefaultFolder, progress, ct);
}
