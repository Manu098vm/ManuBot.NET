using Discord;
using PKHeX.Core;
using System;

namespace SysBot.Pokemon.Discord;

public class TradeEmbedBuilder<T>(T PKM, PokeTradeHub<T> Hub, QueueUser trader) where T : PKM, new()
{
    private bool Initialized { get; set; } = false;
    private EmbedBuilder Builder { get; init; } = new();
    private PKMStringWrapper<T> Strings { get; init; } = new(PKM, Hub.Config.Discord.TradeEmbedSettings);

    public Embed Build()
    {
        if (!Initialized)
            InitializeEmbed();

        return Builder.Build();
    }

    public void InitializeEmbed()
    {
        const string UNSET = "** **";

        Builder.Color = InitializeColor();
        Builder.Author = InitializeAuthor();
        Builder.Footer = InitializeFooter();
        Builder.ThumbnailUrl = Strings.GetPokemonImageURL();

        // Set the Pokémon Species as Embed Title
        var mark = Strings.Mark;
        var fieldName = mark.HasMark switch
        {
            true => $"{Strings.Shiny} {Strings.Species} {Strings.Gender} {mark.Title}",
            _ => $"{Strings.Shiny} {Strings.Species} {Strings.Gender}",
        };

        Builder.AddField(x =>
        {
            x.Name = fieldName;
            x.Value = UNSET;
            x.IsInline = false;
        });

        // Add Pokémon Held Item, if any
        if (Strings.HasItem) { Builder.AddField(x =>
        {
            x.Name = $"**Held Item**: {Strings.HeldItem}";
            x.Value = UNSET;
            x.IsInline = false;
        });}

        // Add general Pokémon informations
        var fieldValue = $"**Level:** {PKM.CurrentLevel}{Environment.NewLine}" +
                         $"**Ability:** {Strings.Ability}{Environment.NewLine}" +
                         $"**Nature:** {Strings.Nature}{Environment.NewLine}" +
                         $"**Scale:** {Strings.Scale}";

        if (Strings.HasTeraType)
            fieldValue += $"{Environment.NewLine}**Tera Type:** {Strings.TeraType}";

        if (mark.HasMark)
            fieldValue += $"{Environment.NewLine}**Mark:** {mark.Name}";

        Builder.AddField(x =>
        {
            x.Name = "Pokémon Info:";
            x.Value = fieldValue;
            x.IsInline = true;
        });

        // Empty Field, so we build a two-column layout
        var unsetField = new EmbedFieldBuilder
        {
            Name = UNSET,
            Value = UNSET,
            IsInline = true
        };
        Builder.AddField(unsetField);

        // Add Pokémon Moveset
        Builder.AddField(x =>
        {
            x.Name = "Moveset:";
            x.Value = string.Join(Environment.NewLine, Strings.Moves);
            x.IsInline = true;
        });

        //Add Pokémon IVs and EVs, if enabled
        if (Hub.Config.Discord.TradeEmbedSettings.ShowIVsAndEVs)
        {
            var ivs = $"**HP:** {PKM.IV_HP}{Environment.NewLine}" +
                      $"**Atk:** {PKM.IV_ATK}{Environment.NewLine}" +
                      $"**Def:** {PKM.IV_DEF}{Environment.NewLine}" +
                      $"**SpA:** {PKM.IV_SPA}{Environment.NewLine}" +
                      $"**SpD:** {PKM.IV_SPD}{Environment.NewLine}" +
                      $"**Spe:** {PKM.IV_SPE}{Environment.NewLine}";

            Builder.AddField(x =>
            {
                x.Name = "Pokémon IVs:";
                x.Value = ivs;
                x.IsInline = true;
            });

            Builder.AddField(unsetField); // Add empty field for two-column layout

            var evs = $"**HP:** {PKM.EV_HP}{Environment.NewLine}" +
                      $"**Atk:** {PKM.EV_ATK}{Environment.NewLine}" +
                      $"**Def:** {PKM.EV_DEF}{Environment.NewLine}" +
                      $"**SpA:** {PKM.EV_SPA}{Environment.NewLine}" +
                      $"**SpD:** {PKM.EV_SPD}{Environment.NewLine}" +
                      $"**Spe:** {PKM.EV_SPE}";

            Builder.AddField(x =>
            {
                x.Name = "Pokémon EVs:";
                x.Value = evs;
                x.IsInline = true;
            });
        }

        Initialized = true;
    }

    private Color InitializeColor() =>
        PKM.IsShiny && (PKM.ShinyXor == 0 || PKM.FatefulEncounter) ? Color.Gold : PKM.IsShiny ? Color.LighterGrey : Color.Teal;

    private EmbedAuthorBuilder InitializeAuthor() => new()
    {
        Name = $"{trader.Username}'s Pokémon",
        IconUrl = Strings.GetBallImageURL(),
    };

    private EmbedFooterBuilder InitializeFooter()
    {
        var type = Hub.Config.Discord.UseTradeEmbeds;
        string footerText = Environment.NewLine;

        // Assume OT and TID can change during the trade process, only show them if the trade has been completed.
        if (type is TradeEmbedDisplay.TradeComplete)
        {
            footerText += $"OT: {PKM.OriginalTrainerName} | TID: {PKM.DisplayTID}" +
                          $"{Environment.NewLine}Trade finished. Enjoy your Pokémon!";
        }

        return new EmbedFooterBuilder { Text = footerText };
    }
}

public record QueueUser(ulong UID, string Username);
