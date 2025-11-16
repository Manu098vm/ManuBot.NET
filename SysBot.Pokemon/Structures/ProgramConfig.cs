using SysBot.Base;
using System.Text.Json.Serialization;

namespace SysBot.Pokemon;

public class ProgramConfig : BotList<PokeBotState>
{
    public PokeTradeHubConfig Hub { get; set; } = new();
}

[JsonSerializable(typeof(ProgramConfig))]
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class ProgramConfigContext : JsonSerializerContext;
