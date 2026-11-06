using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Archives;
using D3skCommander.App.Features.Associations;
using D3skCommander.App.Features.Catalog;
using D3skCommander.App.Features.Clipboard;
using D3skCommander.App.Features.Compare;
using D3skCommander.App.Features.Crypt;
using D3skCommander.App.Features.DiskUsage;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.Editor;
using D3skCommander.App.Features.Favorites;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Ftp;
using D3skCommander.App.Features.Help;
using D3skCommander.App.Features.Jobs;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Features.Lister;
using D3skCommander.App.Features.Log;
using D3skCommander.App.Features.MultiRename;
using D3skCommander.App.Features.NetDrives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.QuickFind;
using D3skCommander.App.Features.QuickView;
using D3skCommander.App.Features.Search;
using D3skCommander.App.Features.Settings;
using D3skCommander.App.Features.Sync;
using D3skCommander.App.Features.Toolbar;
using D3skCommander.App.Features.Tools;
using D3skCommander.App.Features.Update;
using D3skCommander.App.Smoke;
using D3skCommander.App.Startup;
using D3skCommander.App.ToolWindows;
using Microsoft.Extensions.DependencyInjection;

namespace D3skCommander.App.Shell;

/// <summary>
/// Explicit module list. No reflection: a later row fills its own <c>Register</c>
/// and does not edit this file or <c>Program</c>.
/// </summary>
public static class AppComposition
{
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArchivesModule.Register(services);
        AssociationsModule.Register(services);
        CatalogModule.Register(services);
        ClipboardModule.Register(services);
        CompareModule.Register(services);
        CryptModule.Register(services);
        DiskUsageModule.Register(services);
        DrivesModule.Register(services);
        EditorModule.Register(services);
        FavoriteTabsModule.Register(services);
        FileOpsModule.Register(services);
        FtpModule.Register(services);
        HelpModule.Register(services);
        JobsModule.Register(services);
        LaunchModule.Register(services);
        ListerModule.Register(services);
        LogModule.Register(services);
        MultiRenameModule.Register(services);
        NetDrivesModule.Register(services);
        PanelsModule.Register(services);
        QuickFindModule.Register(services);
        QuickViewModule.Register(services);
        SearchModule.Register(services);
        SettingsModule.Register(services);
        SyncModule.Register(services);
        ToolbarModule.Register(services);
        ToolsModule.Register(services);
        UpdateModule.Register(services);
        DialogsModule.Register(services);
        ToolWindowsModule.Register(services);
        SmokeModule.Register(services);
        StartupModule.Register(services);
    }
}
