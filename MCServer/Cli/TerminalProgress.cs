namespace MCServer.Cli;

/// <summary>Renders an indeterminate, cancellable terminal progress bar.</summary>
public sealed class TerminalProgress
{
    private static readonly char[] Spinner = ['|', '/', '-', '\\'];
    private readonly object _gate = new();
    private int _spinner;
    private DateTime _lastUpdate = DateTime.MinValue;
    private int _lastLineLength;

    public void Report(double value, string? message = null)
    {
        var fraction = Math.Clamp(value, 0, 1);
        string text = Format(fraction, message, Spinner[_spinner++ % Spinner.Length]);

        lock (_gate)
        {
            // Avoid flashing the bar too rapidly on very small updates.
            if (DateTime.UtcNow - _lastUpdate < TimeSpan.FromMilliseconds(30) && fraction < 1)
                return;

            _lastUpdate = DateTime.UtcNow;
            WriteLine(text);
        }
    }

    public void Complete(string message = "Done")
    {
        lock (_gate)
        {
            _spinner = 0;
            _lastUpdate = DateTime.UtcNow;
            WriteLine(Format(1, message, '✓'));
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            WriteLine("\rCancelled.");
        }
    }

    public void Fail(string message)
    {
        lock (_gate)
        {
            WriteLine($"\rFailed: {message}");
        }
    }

    public void Status(string message)
    {
        lock (_gate)
        {
            WriteLine(Format(0, message, Spinner[_spinner++ % Spinner.Length]));
        }
    }

    private static string Format(double fraction, string? message, char spinner)
    {
        const int width = 24;
        int filled = (int)Math.Round(fraction * width);
        string bar = new string('█', filled) + new string('░', width - filled);
        string suffix = string.IsNullOrWhiteSpace(message) ? string.Empty : $" {message}";
        return $"\r{spinner} [{bar}] {fraction * 100,3:F0}%{suffix,-48}";
    }

    private void WriteLine(string text)
    {
        int padding = Math.Max(0, _lastLineLength - text.Length);
        Console.Write(text + new string(' ', padding));
        _lastLineLength = text.Length;

        if (text.StartsWith("\r✓", StringComparison.Ordinal) ||
            text.StartsWith("\rCancelled", StringComparison.Ordinal) ||
            text.StartsWith("\rFailed", StringComparison.Ordinal))
        {
            Console.WriteLine();
            _lastLineLength = 0;
        }
    }
}
