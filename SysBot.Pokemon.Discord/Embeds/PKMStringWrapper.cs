using System;
using PKHeX.Core;
using System.Collections.Generic;

namespace SysBot.Pokemon.Discord;

internal class PKMStringWrapper<T>(T PKM, TradeEmbedSettings Config) where T : PKM, new()
{
    protected GameStrings GameStrings =>
        GameInfo.GetStrings(Language.GetLanguageCode(Config.ForceEmbedLanguage is LanguageID.None ? (LanguageID)PKM.Language : Config.ForceEmbedLanguage));

    internal string Species => GetSpeciesString();
    internal string Shiny => GetShinyString();
    internal string Gender => GetGenderString();
    internal string Scale => GetScaleString();
    internal string TeraType => GetTeraTypeString();

    internal string Ability => GameStrings.Ability[PKM.Ability];
    internal string Nature => GameStrings.Natures[(byte)PKM.StatNature];
    internal string HeldItem => GameStrings.Item[PKM.HeldItem];

    internal bool HasTeraType => PKM is ITeraType { TeraType: > MoveType.Any };
    internal bool HasItem => PKM.HeldItem > 0;
    internal PokemonMark Mark => new(PKM);
    internal List<string> Moves => GetMovesStrings();

    private string GetSpeciesString()
    {
        var species = $"{SpeciesName.GetSpeciesNameGeneration(PKM.Species, PKM.Language, PKM.Generation)}";
        var forms = FormConverter.GetFormList(PKM.Species, GameStrings.types, GameStrings.forms, GameInfo.GenderSymbolASCII, PKM.Context);
        return $"{species}{(forms.Length > 1 && PKM.Form > 0 ? $"-{$"{forms[PKM.Form]}"}" : "")}";
    }

    private string GetShinyString() =>
        PKM.ShinyXor == 0 ? "■" : PKM.IsShiny ? "★" : "";

    private string GetGenderString() => Config.UseGenderEmoji switch
    {
        true => $"<:GenderEmoji:{Config.GenderEmojiCodes.GetEmojiCode(PKM.Gender)}>",
        _ => (Gender)PKM.Gender != PKHeX.Core.Gender.Genderless ? $" - {GameInfo.GenderSymbolUnicode[PKM.Gender]}" : ""
    };

    private List<string> GetMovesStrings()
    {
        var moves = new List<string>();
        for (int i = 0; i < PKM.Moves.Length; i++)
        {
            if (PKM.Moves[i] is { } move && move is not (ushort)Move.None)
            {
                var type = (MoveType)MoveInfo.GetType(move, PKM.Context);
                var emoji = $"{(Config.UseMoveEmoji ? $"<:TypeEmoji:{Config.TypesEmojiCodes.GetEmojiCode(type)}> " : "")}";
                var name = GameStrings.movelist[move];
                var pp = Config.ShowMovePP ? i switch
                {
                    0 => $"({PKM.Move1_PP} PP)",
                    1 => $"({PKM.Move2_PP} PP)",
                    2 => $"({PKM.Move3_PP} PP)",
                    3 => $"({PKM.Move4_PP} PP)",
                    _ => throw new ArgumentOutOfRangeException(nameof(i), "Invalid move index.")
                } : "";
                moves.Add($"- {emoji}{name} {pp}");
            }
        }
        return moves;
    }

    private string GetScaleString() => PKM switch
    {
        var pk when pk is IScaledSize3 { } s => $"{PokeSizeDetailedUtil.GetSizeRating(s.Scale)} ({s.Scale})",
        var pk when pk is IScaledSize { } hw => $"{PokeSizeDetailedUtil.GetSizeRating(hw.HeightScalar)} ({hw.HeightScalar})",
        _ => throw new NotSupportedException("Unsupported PKM type for scale string.")
    };

    private string GetTeraTypeString()
    {
        if (PKM is ITeraType tera)
        {
            var type = (GemType)(tera.TeraType + 2);
            if (Config.UseTeraEmoji)
                return $"<:TypeEmoji:{Config.TypesEmojiCodes.GetEmojiCode(type)}>";
            return $"{GameStrings.types[type is GemType.Stellar ? 18 : (int)(type - 2)]}";
        }
        return "";
    }

    internal string GetPokemonImageURL() =>
        TradeExtensions<T>.GetPokemonImageURL(PKM, PKM is IGigantamax { } g && g.CanGigantamax, fullSize: false);

    internal string GetBallImageURL() =>
        "https://raw.githubusercontent.com/BakaKaito/HomeImages/refs/heads/main/Ballimg/50x50/" + $"{(Ball)PKM.Ball}ball.png".ToLower();
}
