using Markdig;
using MCServer.Server;

namespace MCServer.Services;

public static class DocsService
{
    public static string Get404()
    {
        var filePath = Path.Combine(Program.AssetsPath, "Docs_404.html");
        return File.ReadAllText(filePath);
    }

    public static string Docs404 = Get404();
    
    private static string GetHTML(string path)
    {
        if(!File.Exists(path))
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
    
    public static string GetReadMe(this MCBedrockServer server)
    {
        return GetHTML(Path.Combine(server.ServerPath, "bedrock_server_how_to.html"));
    }
    
    public static string GetReleaseNotes()
    {
        return GetHTML(Path.Combine(Program.AssetsPath, "ChangeLog.md"));
    }
    
    public static string GetReleaseNotes(this MCBedrockServer server)
    {
        return GetHTML(Path.Combine(server.ServerPath, "release-notes.txt"));
    }
}