using System;
using System.Globalization;
using System.Windows;

namespace ByGalacticAccord.Wpf;

/// <summary>
/// Application entry. Normally opens the live window; with <c>--shot &lt;path&gt;</c> it renders a single
/// still PNG headlessly (handy for screenshots and for verifying the visuals without a display).
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        long seed = ArgLong(e.Args, "--seed", 20260619L);
        string? shot = ArgValue(e.Args, "--shot");

        if (shot is not null)
        {
            long warmup = ArgLong(e.Args, "--ticks", 900L);
            var window = new MainWindow(seed, autoRun: false);
            window.RenderToPng(shot, 1366, 820, warmup);
            Shutdown(0);
            return;
        }

        new MainWindow(seed, autoRun: true).Show();
    }

    private static string? ArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static long ArgLong(string[] args, string flag, long fallback) =>
        ArgValue(args, flag) is { } v && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : fallback;
}
