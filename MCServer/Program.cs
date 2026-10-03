using System.Security.Claims;
using MCServer;
using MCServer.BDS;
using MCServer.Components;
using MCServer.Plugins;
using MCServer.Server;
using MCServer.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;

namespace MCServer;

public class Program
{
    public static string AssetsPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "Assets"));

#if DEBUG
    public static string RootPath = Path.GetFullPath("./../Data/");
#else
    public static string RootPath = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCServer/"));
#endif

    public static string ServerPath = Path.GetFullPath(Path.Combine(RootPath, "Servers"));
    public static string PluginsPath = Path.GetFullPath(Path.Combine(RootPath, "Plugins"));

    /// <summary>
    /// User-data settings file (users, servers). Lives under the data folder:
    /// writable without elevation and untouched by install/upgrade/uninstall.
    /// The appsettings.json next to the binaries only provides install-time
    /// defaults and is never written to by the app.
    /// </summary>
    public static string SettingsFilePath = Path.GetFullPath(Path.Combine(RootPath, "settings.json"));

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
        // User data lives outside the install dir (Program Files is read-only
        // for standard users and is replaced on upgrade). Ensure it exists for
        // both the web host and the --reset-auth CLI below.
        Directory.CreateDirectory(RootPath);

        // Host CLI: reset the admin login without starting the web server.
        // Usage: MCServer --reset-auth
        if (args.Any(a => a.Equals("--reset-auth", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("--reset-admin", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("reset-auth", StringComparison.OrdinalIgnoreCase)))
        {
            ResetAdminViaCli();
            return;
        }

        var builder = WebApplication.CreateBuilder(args);

        // Layer the data-folder settings over the shipped defaults so UI-saved
        // users/servers win without ever writing into the install directory.
        builder.Configuration.AddJsonFile(SettingsFilePath, optional: true, reloadOnChange: false);

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

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.LogoutPath = "/login";
                options.AccessDeniedPath = "/login";
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                options.Cookie.Name = "MCServer.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
            });
        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();

        var serverHost = new ServerHost();
        var pluginService = new PluginService(PluginsPath);
        var gameServers = new GameServers();

        builder.Services.AddSingleton<IServerHost>(serverHost);
        builder.Services.AddSingleton(pluginService);
        builder.Services.AddSingleton(gameServers);
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<ConsoleInputService>();
        builder.Services.AddSingleton<HostConsoleCommands>();
        builder.Services.AddHostedService<ConsoleReaderService>();
        builder.Services.AddSingleton<ProcessMetricsService>();
        builder.Services.AddSingleton<HostMetricsService>();
        builder.Services.AddScoped<PageHeaderService>();

        var app = builder.Build();

        Settings = app.Services.GetRequiredService<IOptions<AppSettings>>().Value;
        SettingsStore = app.Services.GetRequiredService<SettingsService>();
        // Ensures a default Admin/Admin login exists on first start.
        app.Services.GetRequiredService<AuthService>();
        // Attaches the live host-console commands (help, reset-auth) to the input bus.
        app.Services.GetRequiredService<HostConsoleCommands>();

        // Built-in server plugins ship with the app; external ones load from Plugins/.
        pluginService.RegisterBuiltIn(new BedrockPlugin());
        pluginService.ProcessPendingDeletes();
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

        app.UseAuthentication();
        app.UseAuthorization();

        // Blazor Server SSR guard: redirect page navigations before Blazor renders,
        // so anonymous users always land on /login (never a half-rendered shell).
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Method == HttpMethods.Get)
            {
                var path = ctx.Request.Path;
                var authenticated = ctx.User.Identity?.IsAuthenticated == true;

                if (!authenticated && !IsPublicPage(path))
                {
                    var returnUrl = path + ctx.Request.QueryString;
                    ctx.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl.ToString())}");
                    return;
                }

                if (authenticated && path.Equals("/login", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.Redirect(SafeReturnUrl(ctx.Request.Query["returnUrl"].ToString()));
                    return;
                }
            }
            await next();
        });

        MapAuthFormEndpoints(app);

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Lifetime.ApplicationStopping.Register(() => gameServers.Dispose());

        app.Run();
    }

    /// <summary>
    /// Plain HTML form POSTs (no JSON API): the login page posts a native form,
    /// the server sets the auth cookie and redirects. Works in SSR and interactive circuits.
    /// </summary>
    private static void MapAuthFormEndpoints(WebApplication app)
    {
        app.MapPost("/auth/login", async (HttpContext ctx, AuthService auth) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();

            if (!auth.ValidateCredentials(username, password))
            {
                var dest = $"/login?error=1{(string.IsNullOrEmpty(returnUrl) ? "" : $"&returnUrl={Uri.EscapeDataString(returnUrl)}")}";
                return Results.Redirect(dest);
            }

            var user = auth.FindByUsername(username)!;
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, user.Role)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

            return Results.Redirect(SafeReturnUrl(returnUrl));
        }).DisableAntiforgery();

        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).DisableAntiforgery();
    }

    /// <summary>Only local return URLs are honored (open-redirect protection).</summary>
    private static string SafeReturnUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") ? url : "/dashboard";

    /// <summary>Paths reachable without a login: the login page itself, error pages, Blazor/asset endpoints.</summary>
    private static bool IsPublicPage(PathString path)
    {
        if (path.Equals("/login") || path.Equals("/not-found") || path.Equals("/Error"))
            return true;

        var s = path.Value ?? "";
        if (s.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("/_content", StringComparison.OrdinalIgnoreCase))
            return true;

        return Path.HasExtension(s); // static assets: css, js, png, ...
    }

    /// <summary>
    /// Resets the Admin user to Admin/Admin by editing the data-folder
    /// settings file directly (no web server needed). Creates the file when
    /// absent so a CLI reset works before the first web launch. Never throws.
    /// </summary>
    private static void ResetAdminViaCli()
    {
        // Fresh start: the legacy install-dir appsettings files are
        // install-time defaults only and are intentionally left alone.
        var file = SettingsFilePath;
        try
        {
            var defaultUsers = new JArray
            {
                new JObject
                {
                    ["Username"] = AuthService.DefaultUsername,
                    ["PasswordHash"] = PasswordHasher.Hash(AuthService.DefaultPassword),
                    ["Role"] = AuthService.AdminRole
                }
            };

            var root = File.Exists(file)
                ? JObject.Parse(File.ReadAllText(file))
                : new JObject();

            var section = root[AppSettings.SettingName] as JObject ?? new JObject();
            section["Users"] = defaultUsers;
            root[AppSettings.SettingName] = section;

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, root.ToString(Formatting.Indented));

            Console.WriteLine($"Admin login in '{file}' has been reset to {AuthService.DefaultUsername}/{AuthService.DefaultPassword}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to reset admin login: {ex.Message}");
        }
    }

    private sealed class ServerHost : IServerHost
    {
        public string ServerFiles => ServerPath;
        public string ServerFilesRoot => RootPath;
        public string BackupPath => Settings?.BackupPath ?? "";
        public bool IsWindows => IsWin;
        public void NotifyUser(string message, Severity severity) => Program.NotifyUser(message, severity);
        public void SaveSettings() => SettingsStore?.Save();
    }
}
