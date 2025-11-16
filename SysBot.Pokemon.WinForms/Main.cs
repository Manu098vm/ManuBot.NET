using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Z3;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms;

public sealed partial class Main : Form
{
    private readonly List<PokeBotState> Bots = [];
    private readonly IPokeBotRunner RunningEnvironment;
    private readonly ProgramConfig Config;

    public Main(ProgramConfig config)
    {
        InitializeComponent();

        Config = config;
        PokeTradeBotSWSH.SeedChecker = new Z3SeedSearchHandler<PK8>();
        RunningEnvironment = GetRunner(Config);

        foreach (var bot in Config.Bots)
        {
            bot.Initialize();
            AddBot(bot);
        }

        if (Program.IsDarkTheme)
        {

            foreach (var tab in new[] { Tab_Bots, Tab_Logs, Tab_Hub })
                tab.BackColor = Color.FromArgb(32, 32, 32);

            foreach (var text in new[] { TB_IP, NUD_Port })
                text.BorderStyle = BorderStyle.FixedSingle;
        }

        RTB_Logs.MaxLength = 32_767; // character length
        LoadControls();
        Text = $"{Text} ({Config.Hub.Mode})";
        Task.Run(BotMonitor);

        InitUtil.InitializeStubs(Config.Hub.Mode);
    }

    private static IPokeBotRunner GetRunner(ProgramConfig cfg) => cfg.Hub.Mode switch
    {
        ProgramMode.LGPE => new PokeBotRunnerImpl<PB7>(cfg.Hub, new BotFactory7LGPE()),
        ProgramMode.SWSH => new PokeBotRunnerImpl<PK8>(cfg.Hub, new BotFactory8SWSH()),
        ProgramMode.BDSP => new PokeBotRunnerImpl<PB8>(cfg.Hub, new BotFactory8BS()),
        ProgramMode.LA   => new PokeBotRunnerImpl<PA8>(cfg.Hub, new BotFactory8LA()),
        ProgramMode.SV   => new PokeBotRunnerImpl<PK9>(cfg.Hub, new BotFactory9SV()),
        ProgramMode.LZA  => new PokeBotRunnerImpl<PA9>(cfg.Hub, new BotFactory9LZA()),
        _ => throw new IndexOutOfRangeException("Unsupported mode."),
    };

    private async Task BotMonitor()
    {
        while (!Disposing)
        {
            try
            {
                foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                    c.ReadState();
            }
            catch
            {
                // Updating the collection by adding/removing bots will change the iterator
                // Can try a for-loop or ToArray, but those still don't prevent concurrent mutations of the array.
                // Just try, and if failed, ignore. Next loop will be fine. Locks on the collection are kinda overkill, since this task is not critical.
            }
            await Task.Delay(2_000).ConfigureAwait(false);
        }
    }

    private void LoadControls()
    {
        MinimumSize = Size;
        PG_Hub.SelectedObject = RunningEnvironment.Config;

        var routines = Enum.GetValues<PokeRoutineType>().Where(z => RunningEnvironment.SupportsRoutine(z));
        var list = routines.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
        CB_Routine.DisplayMember = nameof(ComboItem.Text);
        CB_Routine.ValueMember = nameof(ComboItem.Value);
        CB_Routine.DataSource = list;
        CB_Routine.SelectedValue = (int)PokeRoutineType.FlexTrade; // default option

        var protocols = Enum.GetValues<SwitchProtocol>();
        var listP = protocols.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
        CB_Protocol.DisplayMember = nameof(ComboItem.Text);
        CB_Protocol.ValueMember = nameof(ComboItem.Value);
        CB_Protocol.DataSource = listP;
        CB_Protocol.SelectedIndex = (int)SwitchProtocol.WiFi; // default option

        LogUtil.Forwarders.Add(new TextBoxForwarder(RTB_Logs));
    }

    private ProgramConfig GetCurrentConfiguration()
    {
        Config.Bots = [.. Bots];
        return Config;
    }

    private void Main_FormClosing(object sender, FormClosingEventArgs e)
    {
        SaveCurrentConfig();
        var bots = RunningEnvironment;
        if (!bots.IsRunning)
            return;

        async Task WaitUntilNotRunning()
        {
            while (bots.IsRunning)
                await Task.Delay(10).ConfigureAwait(false);
        }

        // Try to let all bots hard-stop before ending execution of the entire program.
        WindowState = FormWindowState.Minimized;
        ShowInTaskbar = false;
        bots.StopAll();
        Task.WhenAny(WaitUntilNotRunning(), Task.Delay(5_000)).ConfigureAwait(true).GetAwaiter().GetResult();
    }

    private void SaveCurrentConfig()
    {
        var cfg = GetCurrentConfiguration();
        var lines = JsonSerializer.Serialize(cfg, ProgramConfigContext.Default.ProgramConfig);
        File.WriteAllText(Program.ConfigPath, lines);
    }

    private void B_Start_Click(object sender, EventArgs e)
    {
        SaveCurrentConfig();

        LogUtil.LogInfo("Starting all bots...", "Form");
        RunningEnvironment.InitializeStart();
        SendAll(BotControlCommand.Start);
        Tab_Logs.Select();

        if (Bots.Count == 0)
            WinFormsUtil.Alert("No bots configured, but all supporting services have been started.");
    }

    private void SendAll(BotControlCommand cmd)
    {
        foreach (var c in FLP_Bots.Controls.OfType<BotController>())
            c.SendCommand(cmd, false);

        EchoUtil.Echo($"All bots have been issued a command to {cmd}.");
    }

    private void B_Stop_Click(object sender, EventArgs e)
    {
        var env = RunningEnvironment;
        if (!env.IsRunning && (ModifierKeys & Keys.Alt) == 0)
        {
            WinFormsUtil.Alert("Nothing is currently running.");
            return;
        }

        var cmd = BotControlCommand.Stop;

        if ((ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) != 0) // either, because remembering which can be hard
        {
            if (env.IsRunning)
            {
                WinFormsUtil.Alert("Commanding all bots to Idle.", "Press Stop (without a modifier key) to hard-stop and unlock control, or press Stop with the modifier key again to resume.");
                cmd = BotControlCommand.Idle;
            }
            else
            {
                WinFormsUtil.Alert("Commanding all bots to resume their original task.", "Press Stop (without a modifier key) to hard-stop and unlock control.");
                cmd = BotControlCommand.Resume;
            }
        }
        SendAll(cmd);
    }

    private void B_RebootStop_Click(object sender, EventArgs e)
    {
        if (RunningEnvironment.IsRunning)
        {
            B_Stop_Click(sender, e);
            Task.Run(async () => { await Task.Delay(3_000).ConfigureAwait(false); });
        }

        SaveCurrentConfig();
        LogUtil.LogInfo("Restarting all the consoles...", "Form");
        RunningEnvironment.InitializeStart();
        SendAll(BotControlCommand.RebootAndStop);
        Tab_Logs.Select();

        if (Bots.Count == 0)
            WinFormsUtil.Alert("No bots configured, but all supporting services have been issued the reboot command.");
    }

    private void B_New_Click(object sender, EventArgs e)
    {
        var cfg = CreateNewBotConfig();
        if (!AddBot(cfg))
        {
            WinFormsUtil.Alert("Unable to add bot; ensure details are valid and not duplicate with an already existing bot.");
            return;
        }
        System.Media.SystemSounds.Asterisk.Play();
    }

    private bool AddBot(PokeBotState cfg)
    {
        if (!cfg.IsValid())
            return false;

        if (Bots.Any(z => z.Connection.Equals(cfg.Connection)))
            return false;

        PokeRoutineExecutorBase newBot;
        try
        {
            Console.WriteLine($"Current Mode ({Config.Hub.Mode}) does not support this type of bot ({cfg.CurrentRoutineType}).");
            newBot = RunningEnvironment.CreateBotFromConfig(cfg);
        }
        catch
        {
            return false;
        }

        try
        {
            RunningEnvironment.Add(newBot);
        }
        catch (ArgumentException ex)
        {
            WinFormsUtil.Error(ex.Message);
            return false;
        }

        AddBotControl(cfg);
        Bots.Add(cfg);
        return true;
    }

    private void AddBotControl(PokeBotState cfg)
    {
        var row = new BotController { Width = FLP_Bots.Width };
        row.Initialize(RunningEnvironment, cfg);
        FLP_Bots.Controls.Add(row);
        FLP_Bots.SetFlowBreak(row, true);
        row.Click += (s, e) =>
        {
            var details = cfg.Connection;
            TB_IP.Text = details.IP;
            NUD_Port.Text = details.Port.ToString();
            CB_Protocol.SelectedIndex = (int)details.Protocol;
            CB_Routine.SelectedValue = (int)cfg.InitialRoutine;
        };

        row.Remove += (s, e) =>
        {
            Bots.Remove(row.State);
            RunningEnvironment.Remove(row.State, !RunningEnvironment.Config.SkipConsoleBotCreation);
            FLP_Bots.Controls.Remove(row);
        };
    }

    private PokeBotState CreateNewBotConfig()
    {
        var ip = TB_IP.Text;
        var port = int.TryParse(NUD_Port.Text, out var p) ? p : 6000;
        var cfg = BotConfigUtil.GetConfig<SwitchConnectionConfig>(ip, port);
        cfg.Protocol = (SwitchProtocol)WinFormsUtil.GetIndex(CB_Protocol);

        var pk = new PokeBotState { Connection = cfg };
        var type = (PokeRoutineType)WinFormsUtil.GetIndex(CB_Routine);
        pk.Initialize(type);
        return pk;
    }

    private void FLP_Bots_Resize(object sender, EventArgs e)
    {
        foreach (var c in FLP_Bots.Controls.OfType<BotController>())
            c.Width = FLP_Bots.Width;
    }

    private void CB_Protocol_SelectedIndexChanged(object sender, EventArgs e)
    {
        var isWifi = CB_Protocol.SelectedIndex == 0;
        TB_IP.Visible = isWifi;
        NUD_Port.Visible = !isWifi;
        //NUD_Port.ReadOnly = isWifi;

        if (isWifi)
            NUD_Port.Text = "6000";
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        CenterTopButtons();
        CenterAddButton();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        CenterAddButton();
    }

    private void CenterTopButtons()
    {
        int topLine = TC_Main.Top;
        int tabHeaderHeight = TC_Main.DisplayRectangle.Top;
        int bottomLine = topLine + tabHeaderHeight;
        int availableHeight = bottomLine - topLine;

        int minButtonHeight = 22;
        float minFontSize = 6f;

        void CenterResizeAndFontButton(Button btn)
        {
            if (btn.Tag is not Font originalFont)
            {
                originalFont = btn.Font;
                btn.Tag = originalFont;
            }

            int targetHeight = Math.Min(originalFont.Height, availableHeight);
            if (targetHeight < minButtonHeight)
                targetHeight = minButtonHeight;

            btn.Height = targetHeight;

            float scale = (float)targetHeight / originalFont.Height;
            float fontSize = Math.Max(minFontSize, Math.Min(originalFont.Size * scale, originalFont.Size));

            if (Math.Abs(btn.Font.Size - fontSize) > 0.5f)
                btn.Font = new Font(originalFont.FontFamily, fontSize, originalFont.Style);

            btn.Top = topLine + (availableHeight - btn.Height) / 2;
        }

        CenterResizeAndFontButton(B_Start);
        CenterResizeAndFontButton(B_Stop);
        CenterResizeAndFontButton(B_RebootStop);
    }

    private void CenterAddButton()
    {
        B_New.Height = TB_IP.Height;
        B_New.Top = TB_IP.Top;

        if (B_New.Tag is not Font originalFont)
        {
            originalFont = B_New.Font;
            B_New.Tag = originalFont;
        }

        float scale = (float)B_New.Height / originalFont.Height;
        float fontSize = Math.Max(6f, Math.Min(originalFont.Size * scale, originalFont.Size));

        if (Math.Abs(B_New.Font.Size - fontSize) > 0.5f)
            B_New.Font = new Font(originalFont.FontFamily, fontSize, originalFont.Style);
    }
}
