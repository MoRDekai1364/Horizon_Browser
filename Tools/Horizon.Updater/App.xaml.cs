using System;
using System.Windows;

namespace Horizon.Updater;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string installerPath = "";
        string restoreSource = "";
        string targetDir = "";
        int parentPid = 0;
        bool silent = false;

        for (int i = 0; i < e.Args.Length; i++)
        {
            string arg = e.Args[i];
            if (arg.Equals("--installer", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                installerPath = e.Args[++i];
            else if (arg.Equals("--restore", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                restoreSource = e.Args[++i];
            else if (arg.Equals("--target", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                targetDir = e.Args[++i];
            else if (arg.Equals("--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                int.TryParse(e.Args[++i], out parentPid);
            else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                silent = true;
        }

        var win = new MainWindow(installerPath, restoreSource, targetDir, parentPid, silent);
        if (!silent)
        {
            win.Show();
        }
        else
        {
            win.WindowState = WindowState.Minimized;
            win.ShowInTaskbar = false;
            win.Show();
        }
    }
}
