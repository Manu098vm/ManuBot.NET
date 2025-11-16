using System;
using System.IO;
using SysBot.Base;
using Microsoft.Win32;
using System.Text.Json;
using System.Windows.Forms;
using System.ComponentModel;

using PKHeX.Core;
using SysBot.Pokemon.Z3;

namespace SysBot.Pokemon.WinForms;

internal static class Program
{
    public static readonly string WorkingDirectory = Environment.CurrentDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
    public static string ConfigPath { get; private set; } = Path.Combine(WorkingDirectory, "config.json");
    public static bool IsDarkTheme { get; private set; } = false;

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
#if NETCOREAPP
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
#endif

        var config = InitConfig();

        foreach (var container in DrawableCollectionEditor.CollectionContainers)
            TypeDescriptor.AddProvider(new CollectionDescriptionProvider(container), container);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

#pragma warning disable WFO5001
        if (IsDarkThemeSet(config))
        {
            IsDarkTheme = true;
            Application.SetColorMode(SystemColorMode.Dark);
        }
#pragma warning restore WFO5001

        Application.Run(new Main(config));
    }

    private static ProgramConfig InitConfig()
    {
        if (File.Exists(ConfigPath))
        {
            var lines = File.ReadAllText(ConfigPath);
            var conf = JsonSerializer.Deserialize(lines, ProgramConfigContext.Default.ProgramConfig) ?? new ProgramConfig();
            LogConfig.MaxArchiveFiles = conf.Hub.MaxArchiveFiles;
            LogConfig.LoggingEnabled = conf.Hub.LoggingEnabled;
            return conf;
        }

        var config = new ProgramConfig();
        config.Hub.Folder.CreateDefaults(WorkingDirectory);
        return config;
    }

    private static bool IsDarkThemeSet(ProgramConfig config)
    {
        var theme = config.Hub.ColorTheme;
        return (theme is SystemColorTheme.Dark || (theme is SystemColorTheme.System && GetFromRegistry() is SystemColorTheme.Dark));

        static SystemColorTheme GetFromRegistry()
        {
            const string keyPath = @"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0 ? SystemColorTheme.Dark : SystemColorTheme.Light;
            return SystemColorTheme.Light;
        }
    }
}
