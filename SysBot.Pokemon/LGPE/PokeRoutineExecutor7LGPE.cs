using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.PokeDataOffsetsLGPE;

namespace SysBot.Pokemon;

public abstract class PokeRoutineExecutor7LGPE(PokeBotState cfg) : PokeRoutineExecutor<PB7>(cfg)
{
    public static uint GetBoxOffset(int box) => BoxStartOffset + (uint)((BoxFormatSlotSize + GapSize) * SlotCount * box);
    public static uint GetSlotOffset(int box, int slot) => GetBoxOffset(box) + (uint)((BoxFormatSlotSize + GapSize) * slot);

    public override async Task<PB7> ReadPokemon(ulong offset, CancellationToken token) =>
        await ReadPokemon(offset, BoxFormatSlotSize, token).ConfigureAwait(false);

    public override async Task<PB7> ReadPokemon(ulong offset, int size, CancellationToken token) =>
        new PB7(await Connection.ReadBytesAsync((uint)offset, size, token).ConfigureAwait(false));

    public async Task SetBoxPokemon(PB7 pk, int box, int slot, CancellationToken token)
    {
        var offset = GetSlotOffset(box, slot);

        Span<byte> data = stackalloc byte[pk.SIZE_PARTY];
        pk.WriteDecryptedDataParty(data);

        var full = data.ToArray();

        var chunkLength = BoxFormatSlotSize - 0x1C;

        var chunk1 = full.AsSpan(0, chunkLength).ToArray();
        await Connection.WriteBytesAsync(chunk1, offset, token).ConfigureAwait(false);

        var chunk2 = full.AsSpan(chunkLength, 0x1C).ToArray();
        await Connection.WriteBytesAsync(chunk2, offset + (uint)chunkLength + 0x70, token).ConfigureAwait(false);
    }

    public override async Task<PB7> ReadBoxPokemon(int box, int slot, CancellationToken token)
    {
        var offset = GetSlotOffset(box, slot);
        var data = await Connection.ReadBytesAsync(offset, BoxFormatSlotSize + GapSize, token).ConfigureAwait(false);
        var chunkLength = BoxFormatSlotSize - 0x1C;
        var chunk1 = data.AsSpan(0, chunkLength).ToArray();
        var chunk2 = data.AsSpan(chunkLength + 0x70, 0x1C).ToArray();
        Span<byte> fullData = new byte[chunk1.Length + chunk2.Length];
        chunk1.CopyTo(fullData);
        chunk2.CopyTo(fullData[chunk1.Length..]);
        return new PB7(fullData.ToArray());
    }

    public override async Task<PB7> ReadPokemonPointer(IEnumerable<long> jumps, int size, CancellationToken token)
    {
        var (valid, offset) = await ValidatePointerAll(jumps, token).ConfigureAwait(false);
        if (!valid) return new PB7();
        return await ReadPokemon(offset, token).ConfigureAwait(false);
    }

    public async Task<PB7?> ReadUntilPresent(uint offset, int waitms, int waitInterval, CancellationToken token, int size = BoxFormatSlotSize)
    {
        int msWaited = 0;
        while (msWaited < waitms)
        {
            var pk = await ReadPokemon(offset, size, token).ConfigureAwait(false);
            if (pk.Species != 0 && pk.ChecksumValid)
                return pk;
            await Task.Delay(waitInterval, token).ConfigureAwait(false);
            msWaited += waitInterval;
        }
        return null;
    }

    public async Task<SAV7b> IdentifyTrainer(CancellationToken token)
    {
        // Check if botbase is on the correct version or later.
        await VerifyBotbaseVersion(token).ConfigureAwait(false);

        // Check title so we can warn if mode is incorrect.
        string title = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
        if (title != LetsGoEeveeID && title != LetsGoPikachuID)
            throw new Exception($"{title} is not a valid Pokémon: Let's Go title. Is your mode correct?");

        // Verify the game version.
        var game_version = await SwitchConnection.GetGameInfo("version", token).ConfigureAwait(false);
        if (!game_version.SequenceEqual(LGPEGameVersion))
            throw new Exception($"Game version is not supported. Expected version {LGPEGameVersion}, and current game version is {game_version}.");

        Log("Grabbing trainer data of host console...");
        var sav = await GetFakeTrainerSAV(token).ConfigureAwait(false);
        InitSaveData(sav);

        if (!IsValidTrainerData())
        {
            await CheckForRAMShiftingApps(token).ConfigureAwait(false);
            throw new Exception("Refer to the SysBot.NET wiki (https://github.com/kwsch/SysBot.NET/wiki/Troubleshooting) for more information.");
        }

        if (await GetTextSpeed(token).ConfigureAwait(false) < TextSpeedOption.Fast)
            throw new Exception("Text speed should be set to FAST. Fix this for correct operation.");

        // Verify B1S1 is not set to the Starter (Partner) Pokémon.
        var poke = await ReadBoxPokemon(0, 0, token).ConfigureAwait(false);
        if (poke is { Species: (ushort)Species.Pikachu, Form: 8 } or { Species: (ushort)Species.Eevee, Form: 1 })
            throw new Exception("Your Pokémon in B1S1 should not be your Starter (Partner) Pokémon. Please fix this for correct operation.");

        return sav;
    }

    public async Task<SAV7b> GetFakeTrainerSAV(CancellationToken token)
    {
        var sav = new SAV7b();
        var info = sav.Blocks.Status;
        var read = await Connection.ReadBytesAsync(TrainerDataOffset, TrainerDataLength, token).ConfigureAwait(false);
        read.CopyTo(info.Data);
        return sav;
    }

    public async Task<SAV7b> GetTradePartnerMyStatus(uint offset, CancellationToken token)
    {
        var info = new SAV7b();
        var read = await Connection.ReadBytesAsync(offset, 0x168, token).ConfigureAwait(false);
        read.CopyTo(info.Blocks.Status.Data);
        return info;
    }

    public async Task InitializeHardware(IBotStateSettings settings, CancellationToken token)
    {
        Log("Detaching on startup.");
        await DetachController(token).ConfigureAwait(false);
        if (settings.ScreenOff)
        {
            Log("Turning off screen.");
            await SetScreen(ScreenState.Off, token).ConfigureAwait(false);
        }
        await SetController(SwitchController.JoyRight1, token);
    }

    public async Task CleanExit(CancellationToken token)
    {
        await SetScreen(ScreenState.On, token).ConfigureAwait(false);
        Log("Detaching controllers on routine exit.");
        await DetachController(token).ConfigureAwait(false);
    }

    public async Task CloseGame(PokeTradeHubConfig config, CancellationToken token)
    {
        var timing = config.Timings;
        await Click(HOME, 2_000 + timing.ExtraTimeReturnHome, token).ConfigureAwait(false);
        await Click(X, 1_000, token).ConfigureAwait(false);
        await Click(A, 5_000 + timing.ExtraTimeCloseGame, token).ConfigureAwait(false);
        Log("Closed out of the game!");
    }

    public async Task StartGame(PokeTradeHubConfig config, CancellationToken token)
    {
        var timing = config.Timings;
        // Open game.
        await Click(A, 1_000 + timing.ExtraTimeLoadProfile, token).ConfigureAwait(false);

        // Menus here can go in the order: Update Prompt -> Profile -> DLC check -> Unable to use DLC.
        //  The user can optionally turn on the setting if they know of a breaking system update incoming.
        if (timing.AvoidSystemUpdate)
        {
            await Click(DUP, 0_600, token).ConfigureAwait(false);
            await Click(A, 1_000 + timing.ExtraTimeLoadProfile, token).ConfigureAwait(false);
        }

        await Click(A, 1_000, token).ConfigureAwait(false);

        Log("Restarting the game!");
        await Task.Delay(4_000 + timing.ExtraTimeLoadGame, token).ConfigureAwait(false);
        await DetachController(token).ConfigureAwait(false);

        while (!await IsOnOverworld(token).ConfigureAwait(false))
            await Click(A, 1_000, token).ConfigureAwait(false);

        Log("Back in the overworld!");
    }

    public async Task RestartGameLGPE(PokeTradeHubConfig config, CancellationToken token)
    {
        await CloseGame(config, token);
        await StartGame(config, token);
    }

    public async Task<ScreenScenario> GetCurrentScreen(int size, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesMainAsync(CurrentScreenOffset, size, token).ConfigureAwait(false);
        var value = BitConverter.ToUInt16(data, 0);
        var scenario = (ScreenScenario)value;
        return scenario;
    }

    public async Task<bool> IsOnOverworld(CancellationToken token) =>
        (await Connection.ReadBytesAsync(OverworldOffset, 1, token).ConfigureAwait(false))[0] == 1;

    public async Task<bool> IsInWaitingScreen(CancellationToken token) =>
        BitConverter.ToUInt32(await SwitchConnection.ReadBytesMainAsync(WaitingScreenOffset, 4, token).ConfigureAwait(false), 0) == 0;

    public async Task<TextSpeedOption> GetTextSpeed(CancellationToken token) =>
        (TextSpeedOption)((await Connection.ReadBytesAsync(TextSpeedOffset, 1, token).ConfigureAwait(false))[0] & 3);
}
