using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Broadcast;

public class Packet
{
    [JsonProperty]
    public PacketCommand Command { get; set; }

    [JsonProperty]
    public string Message { get; set; }


    // Used for username field in the private msg packet type
    [JsonProperty]
    public string MetaData { get; set; }

    public Packet(PacketCommand command, string message = "", string metadata = "")
    {
        Command = command;
        Message = message;
        MetaData = metadata;
    }

    public override string ToString() =>
        $"[Packet]:\n\tCommand: {Command}\n\tMessage: {Message}";

    public string ToJson() => JsonConvert.SerializeObject(this);
    public static Packet? FromJson(string jsonData) => JsonConvert.DeserializeObject<Packet>(jsonData);
}

[JsonConverter(typeof(StringEnumConverter))]
public enum PacketCommand
{
    Msg,
    PrivateMsg,
    List,
    Exit,
    Name
}

