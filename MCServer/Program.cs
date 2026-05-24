using MCServer;
using MCServer.Components;
using MCServer.Server;
using MudBlazor;
using MudBlazor.Services;

namespace MCServer;

public class Program
{
#if DEBUG
    public const string ServerPath = "..\\Data\\Bedrock Server\\";
#else
    public static string ServerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MCServer\\Bedrock Server\\");
#endif

    public static bool IsWin => OperatingSystem.IsWindows();

    public static ISnackbar? Snackbar;

    public static void NotifyUser(string msg, Severity severity)
    {
        Snackbar?.Add(msg, severity);
    }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.RequireInteraction = false;
            config.SnackbarConfiguration.PreventDuplicates = false;
            config.SnackbarConfiguration.NewestOnTop = false;
            config.SnackbarConfiguration.ShowCloseIcon = true;
            config.SnackbarConfiguration.VisibleStateDuration = 7000;
            config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
        });

        var gameServer = new MCBedrockServer(ServerPath);
        builder.Services.AddSingleton(gameServer);

        var app = builder.Build();

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

        app.Lifetime.ApplicationStopping.Register(() => gameServer.Dispose());

        app.Run();
    }
}
