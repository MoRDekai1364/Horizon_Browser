using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

/// <summary>
/// Drag and drop for the wallpaper manager (Phase 5).
///  - Dropping image files adds them to the selected collection.
///  - Dropping a folder that contains images asks: create a collection from it (optionally renamed),
///    or just extract its images into the selected collection.
///  - Dropping several things at once works; all questions are asked first, then everything is copied.
/// </summary>
public partial class MediaManagerWindow
{
    private enum FolderChoice { Skip, AsCollection, ExtractOnly }

    private CancellationTokenSource? _importCts;

    // ------------------------------------------------------------------ overlay

    private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

    /// <summary>Window-level: a drag with files entered the window, so show the full-window drop zone.</summary>
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (_busy || !HasFiles(e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        DropOverlay.Visibility = Visibility.Visible;
    }

    private void Overlay_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasFiles(e) && !_busy ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Overlay_DragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void Overlay_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (_busy || !HasFiles(e)) return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        // Do not show dialogs from inside the drop callback: that would freeze the app the files came from.
        Dispatcher.BeginInvoke(new Action(async () => await HandleDropAsync(paths)));
    }

    private void BtnCancelImport_Click(object sender, RoutedEventArgs e)
    {
        try { _importCts?.Cancel(); } catch { }
        SetStatus("Cancelling...");
    }

    // ------------------------------------------------------------------ the drop itself

    private async Task HandleDropAsync(string[] paths)
    {
        if (_busy) return;

        try
        {
            var files = new List<string>();
            var folders = new List<string>();
            var emptyFolders = new List<string>();
            int unsupported = 0;

            foreach (string p in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(p))
                {
                    if (Lib.FolderHasMedia(p)) folders.Add(p);
                    else emptyFolders.Add(FolderLabel(p));
                }
                else if (File.Exists(p) && Lib.IsMediaFile(p)) files.Add(p);
                else unsupported++;
            }

            if (files.Count == 0 && folders.Count == 0)
            {
                MessageBox.Show(this,
                    "Nothing to add. " + _p.DropHint,
                    "Media", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 1) Ask everything up front, so the long copy can run without interruptions.
            var plan = new List<(string Folder, FolderChoice Choice, string Name)>();
            FolderChoice? rememberedChoice = null;

            for (int i = 0; i < folders.Count; i++)
            {
                string folder = folders[i];
                string label = FolderLabel(folder);
                bool auto = rememberedChoice != null; // "do the same for the others" was ticked earlier

                FolderChoice choice;
                if (auto)
                {
                    choice = rememberedChoice!.Value;
                }
                else
                {
                    int count = Lib.ScanFolder(folder).Count;
                    choice = AskFolderChoice(label, count, folders.Count - i > 1, out bool all);
                    if (all) rememberedChoice = choice;
                }

                if (choice == FolderChoice.Skip) continue;

                string name = "";
                if (choice == FolderChoice.AsCollection)
                {
                    string suggested = MediaLibrary.SanitizeName(label);
                    if (suggested.Length == 0) suggested = "Collection";

                    if (auto)
                    {
                        name = suggested;
                    }
                    else
                    {
                        string? typed = Prompt("Name the collection",
                            $"Name for the collection created from \"{label}\":", suggested);
                        if (typed == null) continue; // cancelled the naming: skip this folder
                        name = typed;
                    }
                }

                plan.Add((folder, choice, name));
            }

            if (files.Count == 0 && plan.Count == 0) return;

            // 2) Copy.
            await RunDropImportAsync(files, plan, unsupported, emptyFolders);
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "MediaManager.Drop");
            SetBusy(false, "Adding files failed.");
        }
    }

    private async Task RunDropImportAsync(
        List<string> files,
        List<(string Folder, FolderChoice Choice, string Name)> plan,
        int unsupported,
        List<string> emptyFolders)
    {
        var d = SettingsService.Current;
        string targetCollection = _currentCollection;

        int copied = 0, skipped = unsupported;
        bool cancelled = false;
        var failures = new List<(string Path, string Error)>();
        var created = new List<string>();

        void Accumulate(MediaImportResult r, bool createdCollection)
        {
            copied += r.Copied.Count;
            skipped += r.Skipped.Count;
            failures.AddRange(r.Failed);
            cancelled |= r.Cancelled;

            if (d.MediaRememberSourcePaths)
                foreach (var pair in r.Copied)
                    MediaSelectionSync.RememberSource(_p.IsWallpaper, r.CollectionName, Path.GetFileName(pair.Value), pair.Key);

            if (createdCollection && r.Copied.Count > 0 && r.CollectionName.Length > 0)
                created.Add(r.CollectionName);
        }

        _importCts = new CancellationTokenSource();
        var ct = _importCts.Token;
        BtnCancelImport.Visibility = Visibility.Visible;
        SetBusy(true, $"Adding {_p.Plural}...");

        string label = _p.Plural;
        var progress = new Progress<MediaProgress>(p =>
            TxtStatus.Text = $"Adding {label}... {p.Done}/{p.Total}  {p.CurrentFile}");

        string summary;
        try
        {
            if (files.Count > 0)
                Accumulate(await Lib.ImportFilesAsync(files, targetCollection, progress, ct), false);

            foreach (var job in plan)
            {
                if (ct.IsCancellationRequested) { cancelled = true; break; }

                if (job.Choice == FolderChoice.AsCollection)
                    Accumulate(await Lib.ImportFolderAsCollectionAsync(job.Folder, job.Name, progress, ct), true);
                else
                    Accumulate(await Lib.ImportFolderImagesAsync(job.Folder, targetCollection, progress, ct), false);
            }

            if (d.MediaRememberSourcePaths && copied > 0) SettingsService.Save();

            summary = $"Added {copied} file{(copied == 1 ? "" : "s")}";
            if (created.Count > 0) summary += $", created {created.Count} collection{(created.Count == 1 ? "" : "s")}";
            if (skipped > 0) summary += $", skipped {skipped}";
            if (failures.Count > 0) summary += $", failed {failures.Count}";
            if (emptyFolders.Count > 0) summary += $", no images in: {string.Join(", ", emptyFolders.Take(3))}";
            if (cancelled) summary += " (cancelled)";
            summary += ".";
        }
        catch (Exception ex)
        {
            LogService.RecordCrash(ex, "MediaManager.DropImport");
            summary = "Adding files failed.";
        }
        finally
        {
            BtnCancelImport.Visibility = Visibility.Collapsed;
            _importCts?.Dispose();
            _importCts = null;
        }

        SetBusy(false, summary);

        if (failures.Count > 0)
        {
            string detail = string.Join("\n", failures.Take(5).Select(f => Path.GetFileName(f.Path) + ": " + f.Error));
            MessageBox.Show(this, "Some files could not be added:\n\n" + detail,
                "Media", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Jump to the newest collection created from a dropped folder; otherwise stay where we were.
        RefreshCollections(created.Count > 0 ? created[created.Count - 1] : targetCollection, reloadTiles: true);
    }

    private static string FolderLabel(string folder)
    {
        string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return name.Length > 0 ? name : folder; // drive roots have no folder name
    }

    // ------------------------------------------------------------------ "collection or just the images?" dialog

    private FolderChoice AskFolderChoice(string folderName, int imageCount, bool offerApplyAll, out bool applyToAll)
    {
        Brush bg = TryFindResource("Brush_DeepBackground") as Brush ?? Brushes.Black;
        Brush fg = TryFindResource("Brush_SoftText") as Brush ?? Brushes.White;
        string target = _currentCollection.Length == 0 ? _p.LooseName : _currentCollection;

        var result = FolderChoice.Skip;
        bool all = false;

        var text = new TextBlock
        {
            Text = $"The folder \"{folderName}\" contains {imageCount} {(imageCount == 1 ? _p.Singular : _p.Plural)}.\n\n" +
                   $"Create a new collection from it, or just add the {_p.Plural} to \"{target}\"?",
            Foreground = fg,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };

        var chk = new CheckBox
        {
            Content = "Do the same for the other folders I dropped",
            Foreground = fg,
            Margin = new Thickness(0, 12, 0, 0),
            Visibility = offerApplyAll ? Visibility.Visible : Visibility.Collapsed
        };

        var btnCollection = new Button { Content = "Create collection", Height = 30, Padding = new Thickness(14, 0, 14, 0), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var btnExtract = new Button { Content = $"Just add the {_p.Plural}", Height = 30, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0) };
        var btnSkip = new Button { Content = "Skip", Height = 30, Padding = new Thickness(14, 0, 14, 0), IsCancel = true };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(btnCollection);
        buttons.Children.Add(btnExtract);
        buttons.Children.Add(btnSkip);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(text);
        panel.Children.Add(chk);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = "Add folder",
            Owner = this,
            Content = panel,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = bg
        };

        btnCollection.Click += (_, __) => { result = FolderChoice.AsCollection; all = chk.IsChecked == true; win.DialogResult = true; };
        btnExtract.Click += (_, __) => { result = FolderChoice.ExtractOnly; all = chk.IsChecked == true; win.DialogResult = true; };
        btnSkip.Click += (_, __) => { all = chk.IsChecked == true; };

        win.ShowDialog();
        applyToAll = all;
        return result;
    }
}
