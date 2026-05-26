using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace MCServer.Helpers;

public class PacketConfig
{
    public ObservableCollection<Limit> limitGroups { get; set; } = [];
    public Algorithm defaultAlgorithm { get; set; } = new();
    [JsonIgnore]
    public Limit DefaultLimit { get; set; }

    public PacketConfig()
    {
        DefaultLimit = new()
        {
            minecraftPacketIds = null!,
            algorithm = defaultAlgorithm
        };
    }

    public enum AlgorithmType
    {
        BucketPacketLimitAlgorithm,
    }

    public class Limit
    {
        public List<uint> minecraftPacketIds { get; set; } = [];
        public Algorithm algorithm { get; set; } = new();

        [JsonIgnore]
        public string PacketIds
        {
            get => string.Join(";", minecraftPacketIds);
            set
            {
                minecraftPacketIds = [..value.Split(";", StringSplitOptions.RemoveEmptyEntries).Select<string,uint?>(x =>
                {
                    if (uint.TryParse(x.Trim(), out var result))
                        return result;
                    return null;
                }).OfType<uint>()];
            }
        }
    }

    public class Algorithm
    {
        [JsonIgnore]
        public AlgorithmType Type { get; set; } = AlgorithmType.BucketPacketLimitAlgorithm;

        internal string name
        {
            get => Type.ToString();
            set
            {
                if (Enum.TryParse<AlgorithmType>(value, false, out var res))
                    Type = res;
            }
        }

        [JsonPropertyName("params")]
        public Params Parameters { get; set; } = new();

        public class Params
        {
            public float drainRatePerSec { get; set; } = .0013f;
            public uint maxBucketSize { get; set; } = 1;
        }
    }
}