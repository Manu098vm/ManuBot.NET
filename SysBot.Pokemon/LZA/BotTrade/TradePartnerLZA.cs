using PKHeX.Core;
using System;
using System.Diagnostics;

namespace SysBot.Pokemon;

public sealed class TradePartnerLZA : ITradePartner
{
    public ulong NID { get; }
    public uint TID7 { get; }
    public uint SID7 { get; }
    public string OT { get; }
    public int Game => (int)GameVersion.ZA;
    public int Gender { get; }
    public int Language { get; }

    public TradePartnerLZA(ulong ID, byte[] TIDSID, byte[] trainerNameObject, int gender, int language)
    {
        NID = ID;

        Debug.Assert(TIDSID.Length == 4);
        var tidsid = BitConverter.ToUInt32(TIDSID, 0);
        TID7 = tidsid % 1_000_000;
        SID7 = tidsid / 1_000_000;

        OT = StringConverter8.GetString(trainerNameObject);
        Gender = gender;
        Language = language;
    }

    public const int MaxByteLengthStringObject = 26;
}
