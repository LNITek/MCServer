using Cronos;
using Newtonsoft.Json;

namespace MCServer.Plugins;

public class CronosJsonConverter : JsonConverter<CronExpression>
{
    public override CronExpression? ReadJson(JsonReader reader, Type objectType, CronExpression? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        string val = reader.Value?.ToString() ?? "";

        if (CronExpression.TryParse(val, out CronExpression? res))
            return res;
        return null;
    }

    public override void WriteJson(JsonWriter writer, CronExpression? value, JsonSerializer serializer)
    {
        string val = value?.ToString() ?? "";
        writer.WriteValue(val);
    }
}
