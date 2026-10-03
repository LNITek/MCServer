using Markdig;
using MCServer.Plugins;

namespace MCServer.Services;

public static class DocsService
{
    private static string? _docs404;

    /// <summary>Lazily loaded so a missing assets folder can never poison the type.</summary>
    public static string Docs404 => _docs404 ??= Load404();

    private static string Load404()
    {
        try
        {
            var filePath = Path.Combine(Program.AssetsPath, "Docs_404.html");
            if (File.Exists(filePath))
                return File.ReadAllText(filePath);
        }
        catch { }
        return "<h1>Documentation not found.</h1>";
    }

    private static string GetHTML(string path)
    {
        if (!File.Exists(path))
            return Docs404;

        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseEmojiAndSmiley()
            .Build();

        return Path.GetExtension(path).ToLower() switch
        {
            ".html" => File.ReadAllText(path),
            ".md" or ".txt" => Markdown.ToHtml(File.ReadAllText(path), pipeline),
            _ => File.ReadAllText(path),
        };
    }

    public static string GetReadMe()
    {
        return GetHTML(Path.Combine(Program.AssetsPath, "README.md"));
    }

    public static string GetReleaseNotes()
    {
        return GetHTML(Path.Combine(Program.AssetsPath, "ChangeLog.md"));
    }

    /// <summary>
    /// Renders the file a server registered under <paramref name="slug"/>
    /// (see <see cref="IGameServer.DocFiles"/>). Unknown slug or missing file falls back to 404.
    /// </summary>
    public static string GetServerDoc(this IGameServer server, string slug)
    {
        var doc = server.DocFiles.FirstOrDefault(d =>
            d.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (doc is null)
            return Docs404;

        return GetHTML(doc.FilePath);
    }
}
