namespace MCServer.Services;

public class ConsoleInputService
{
    public event Action<string>? OnInput;

    public void Publish(string value) => OnInput?.Invoke(value);
}

public class ConsoleReaderService : BackgroundService
{
    private readonly ConsoleInputService _bus;

    public ConsoleReaderService(ConsoleInputService bus) => _bus = bus;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // No interactive terminal (Windows service, systemd, docker, redirected stdin): nothing to read.
        if (Console.IsInputRedirected)
            return;

        // Yield so the app startup isn't blocked
        await Task.Yield();

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                // Blocks a thread-pool thread until Enter; shutdown is handled via StopAsync below.
                line = Console.ReadLine();
            }
            catch
            {
                return;
            }
            if (line is null)
                return; // stdin closed
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                _bus.Publish(line);
            }
            catch
            {
                // A failing command handler must never kill the reader loop.
            }
        }
    }

    /// <summary>
    /// <see cref="Console.ReadLine"/> ignores cancellation, so a default stop would hang
    /// shutdown until Enter is pressed. The process is exiting anyway; release it immediately.
    /// </summary>
    public override Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
