using MCServer;
using MCServer.BDS;
using MCServer.Components;
using MCServer.Plugins;
using MCServer.Server;
using MCServer.Services;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;

namespace MCServer;

public class Program
{
    public static string AssetsPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "Assets"));

#if DEBUG
    public static string ServerPath = Path.GetFullPath("./../Data/");
#else
    public static string ServerPath = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCServer/"));
#endif

    public static string PluginsPath = Path.GetFullPath(Path.Combine(ServerPath, "Plugins"));

    public static bool IsWin => OperatingSystem.IsWindows();

    public static ISnackbar? Snackbar;
    public static AppSettings Settings = new();
    public static SettingsService? SettingsStore;

    public static void NotifyUser(string msg, Severity severity)
    {
        Snackbar?.Add(msg, severity);
    }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services
            .Configure<AppSettings>(builder.Configuration.GetSection(AppSettings.SettingName))
            .AddLocalStorageServices()
            .AddMudServices(config =>
             {
                 config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
                 config.SnackbarConfiguration.RequireInteraction = false;
                 config.SnackbarConfiguration.PreventDuplicates = false;
                 config.SnackbarConfiguration.NewestOnTop = false;
                 config.SnackbarConfiguration.ShowCloseIcon = true;
                 config.SnackbarConfiguration.VisibleStateDuration = 7000;
                 config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
             })
            .AddRazorComponents()
            .AddInteractiveServerComponents();

        var serverHost = new ServerHost();
        var pluginService = new PluginService(PluginsPath);
        var gameServers = new GameServers();

        builder.Services.AddSingleton<IServerHost>(serverHost);
        builder.Services.AddSingleton(pluginService);
        builder.Services.AddSingleton(gameServers);
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<ProcessMetricsService>();
        builder.Services.AddSingleton<HostMetricsService>();

        var app = builder.Build();

        Settings = app.Services.GetRequiredService<IOptions<AppSettings>>().Value;
        SettingsStore = app.Services.GetRequiredService<SettingsService>();

        // Built-in server plugins ship with the app; external ones load from Plugins/.
        pluginService.RegisterBuiltIn(new BedrockPlugin());
        pluginService.LoadFromFolder();

        foreach (var plugin in pluginService.ServerPlugins)
            gameServers.RegisterPlugin(plugin);

        gameServers.Register(Settings.ServerSettings, serverHost);

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Lifetime.ApplicationStopping.Register(() => gameServers.Dispose());

        app.Run();
    }

    private sealed class ServerHost : IServerHost
    {
        public string ServerFilesRoot => ServerPath;
        public string BackupPath => Settings?.BackupPath ?? "";
        public bool IsWindows => IsWin;
        public void NotifyUser(string message, Severity severity) => Program.NotifyUser(message, severity);
        public void SaveSettings() => SettingsStore?.Save();
    }
}
