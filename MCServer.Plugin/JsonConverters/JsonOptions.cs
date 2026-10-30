using Newtonsoft.Json;

namespace MCServer.Plugins;

public static class JsonOptions
{
    public static JsonSerializerSettings SerializerSettings = new()
    {
        Formatting = Formatting.None,
    };
}
