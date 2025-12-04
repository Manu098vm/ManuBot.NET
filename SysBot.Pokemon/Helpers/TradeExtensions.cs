using System;
using System.Linq;
using PKHeX.Core;
using SysBot.Base;

namespace SysBot.Pokemon;

public interface ITradePartner
{
    public uint TID7 { get; }
    public uint SID7 { get; }
    public string OT { get; }
    public int Game { get; }
    public int Gender { get; }
    public int Language { get; }
}

public class TradeExtensions<T> where T : PKM, new()
{
    public static bool TrySetPartnerDetails(RoutineExecutor<PokeBotState> executor, ITradePartner partner, PokeTradeDetail<T> trade, PokeTradeHubConfig config, out T pkm)
    {
        void Log(string msg) => executor.Log(msg);

        var original = trade.TradeData;
        pkm = (T)original.Clone();

        //LGPE doesn't allow injection while already on the trade screen
        if (typeof(T) == typeof(PB7))
        {
            Log("Can not apply Partner details: LGPE doesn't support injecting while on the trading screen.");
            return false;
        }

        //Invalid trade request. Ditto is often requested for Masuda method, better to not apply partner details.
        if ((Species)pkm.Species is Species.None or Species.Ditto || trade.Type is not PokeTradeType.Specific)
        {
            Log("Can not apply Partner details: Not a specific trade request.");
            return false;
        }

        //Current handler cannot be past gen OT
        if (!pkm.IsEgg && pkm.Generation != pkm.Format && !config.Legality.ForceTradePartnerDetails)
        {
            Log("Can not apply Partner details: Current handler cannot be different gen OT.");
            return false;
        }

        if (pkm is IHomeTrack track && track.Tracker != 0)
        {
            //Better to not override OT data that has already been registered to Home servers
            if (!config.Legality.ResetHOMETracker)
            {
                Log("Can not apply Partner details: the Pokémon already has a set Home Tracker.");
                return false;
            }
            else
            {
                track.Tracker = 0;
            }
        }

        //Only override trainer details if user didn't specify OT details in the Showdown/PK9 request
        if (HasRequestedTrainerDetails(pkm))
        {
            Log("Can not apply Partner details: Requested Pokémon already has set Trainer details.");
            return false;
        }

        pkm.OriginalTrainerName = partner.OT;
        pkm.OriginalTrainerGender = (byte)partner.Gender;
        pkm.TrainerTID7 = partner.TID7;
        pkm.TrainerSID7 = partner.SID7;
        pkm.Language = partner.Language;
        pkm.Version = (GameVersion)partner.Game;

        if (original.IsEgg && original.Language != partner.Language)
        {
            if (pkm is PB8) pkm.NicknameTrash.Clear();
            pkm.Nickname = SpeciesName.GetEggName(pkm.Language, pkm.Format);
        }

        if (!original.IsNicknamed && !original.IsEgg)
            pkm.ClearNickname();

        if (original.IsShiny)
            pkm.PID = (uint)((pkm.TID16 ^ pkm.SID16 ^ (pkm.PID & 0xFFFF) ^ original.ShinyXor) << 16) | (pkm.PID & 0xFFFF);

        if (!pkm.ChecksumValid)
            pkm.RefreshChecksum();

        var la = new LegalityAnalysis(pkm);
        if (la.Results.Any(l => l.Identifier is CheckIdentifier.TrashBytes or CheckIdentifier.Trainer && !l.Valid))
        {
            pkm.SetString(pkm.OriginalTrainerTrash, partner.OT,
                pkm.MaxStringLengthTrainer, StringConverterOption.ClearZero);

            if (!pkm.ChecksumValid)
                pkm.RefreshChecksum();

            la = new LegalityAnalysis(pkm);
        }

        if (!la.Valid)
        {
            if (config.Legality.ForceTradePartnerDetails)
                pkm.Version = original.Version;

            if (!pkm.ChecksumValid)
                pkm.RefreshChecksum();

            la = new LegalityAnalysis(pkm);

            if (!la.Valid)
            {
                Log("Can not apply Partner details:");
                Log(la.Report());
                return false;
            }
        }

        Log($"Applying trade partner details: {partner.OT} ({(partner.Gender == 0 ? "M" : "F")}), " +
                $"TID: {partner.TID7:000000}, SID: {partner.SID7:0000}, {(LanguageID)partner.Language} ({pkm.Version})");

        return true;
    }

    private static bool HasRequestedTrainerDetails(T requested)
    {
        var host_trainer = AutoLegalityWrapper.GetTrainerInfo<T>();

        if (!requested.OriginalTrainerName.Equals(host_trainer.OT))
            return true;

        if (requested.TID16 != host_trainer.TID16)
            return true;

        if (requested.SID16 != host_trainer.SID16)
            return true;

        if (requested.Language != host_trainer.Language)
            return true;

        return false;
    }

    public static string GetPokemonImageURL(T pkm, bool canGmax, bool fullSize)
    {
        bool md = false;
        bool fd = false;
        string[] baseLink;
        if (fullSize)
            baseLink = "https://raw.githubusercontent.com/9B1td0/HomeImages/master/512x512/poke_capture_0001_000_mf_n_00000000_f_n.png".Split('_');
        else baseLink = "https://raw.githubusercontent.com/9B1td0/HomeImages/master/128x128/poke_capture_0001_000_mf_n_00000000_f_n.png".Split('_');

        if (Enum.IsDefined(typeof(GenderDependent), pkm.Species) && !canGmax && pkm.Form is 0)
        {
            if (pkm.Gender == 0 && pkm.Species != (int)Species.Torchic)
                md = true;
            else fd = true;
        }

        int form = pkm.Species switch
        {
            (int)Species.Sinistea or (int)Species.Polteageist or (int)Species.Rockruff or (int)Species.Mothim => 0,
            (int)Species.Alcremie when pkm.IsShiny || canGmax => 0,
            _ => pkm.Form,

        };

        if (pkm.Species is (ushort)Species.Sneasel)
        {
            if (pkm.Gender is 0)
                md = true;
            else fd = true;
        }

        if (pkm.Species is (ushort)Species.Basculegion)
        {
            if (pkm.Gender is 0)
            {
                md = true;
                pkm.Form = 0;
            }
            else
            {
                pkm.Form = 1;
            }

            string s = pkm.IsShiny ? "r" : "n";
            string g = md && pkm.Gender is not 1 ? "md" : "fd";
            return $"https://raw.githubusercontent.com/9B1td0/HomeImages/master/128x128/poke_capture_0" + $"{pkm.Species}" + "_00" + $"{pkm.Form}" + "_" + $"{g}" + "_n_00000000_f_" + $"{s}" + ".png";
        }

        baseLink[2] = pkm.Species < 10 ? $"000{pkm.Species}" : pkm.Species < 100 && pkm.Species > 9 ? $"00{pkm.Species}" : pkm.Species >= 1000 ? $"{pkm.Species}" : $"0{pkm.Species}";
        baseLink[3] = pkm.Form < 10 ? $"00{form}" : $"0{form}";
        baseLink[4] = pkm.PersonalInfo.OnlyFemale ? "fo" : pkm.PersonalInfo.OnlyMale ? "mo" : pkm.PersonalInfo.Genderless ? "uk" : fd ? "fd" : md ? "md" : "mf";
        baseLink[5] = canGmax ? "g" : "n";
        baseLink[6] = "0000000" + (pkm.Species == (int)Species.Alcremie && !canGmax ? pkm.Data[0xD0] : 0);
        baseLink[8] = pkm.IsShiny ? "r.png" : "n.png";
        return string.Join("_", baseLink);
    }
}
