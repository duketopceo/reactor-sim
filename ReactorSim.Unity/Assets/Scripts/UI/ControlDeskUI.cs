// ReactorSim.Unity — U6: Full-screen PWR Control Desk UI.
// Renders an industrial dark-theme 3-column Control Room dashboard scaled to 1920x1080.
// Every operator control dispatches a PlantIntent through PlantBridge.SubmitIntent(),
// so local single-player, 2-player Co-op (Host & Partner Client), and HTTP/MCP agents
// share the exact same state and control pipeline.

using System;
using System.Globalization;
using UnityEngine;
using ReactorSim;

public class ControlDeskUI : MonoBehaviour
{
    private PlantBridge _bridge;

    private string _joinAddress = "127.0.0.1";
    private string _joinPort = "7777";
    private Vector2 _alarmScroll;
    private Vector2 _logScroll;

    // Cached textures & styles
    private Texture2D _texBg;
    private Texture2D _texPanel;
    private Texture2D _texCard;
    private Texture2D _texBarBg;
    private Texture2D _texGreen;
    private Texture2D _texAmber;
    private Texture2D _texRed;
    private Texture2D _texCyan;
    private Texture2D _texBlue;
    private bool _stylesReady;

    // Rich-text color tags, from theme tokens.
    private static readonly string HexNormal = ThemeColors.Hex(ReactorSim.Ui.Theme.StatusNormal);
    private static readonly string HexOk = ThemeColors.Hex(ReactorSim.Ui.Theme.StatusOk);
    private static readonly string HexWarn = ThemeColors.Hex(ReactorSim.Ui.Theme.StatusCaution);
    private static readonly string HexCrit = ThemeColors.Hex(ReactorSim.Ui.Theme.StatusCritical);
    private static readonly string HexDim = ThemeColors.Hex(ReactorSim.Ui.Theme.TextDim);
    private static readonly string HexBody = ThemeColors.Hex(ReactorSim.Ui.Theme.TextBody);

    private GUIStyle _headerStyle;
    private GUIStyle _subHeaderStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _monoStyle;
    private GUIStyle _badgeStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _scramButtonStyle;
    private GUIStyle _cardStyle;

    public void Initialize(PlantBridge bridge)
    {
        _bridge = bridge;
    }

    private void InitStyles()
    {
        if (_stylesReady && _texBg != null) return;

        _texBg = MakeTex(ThemeColors.SurfaceScreen);
        _texPanel = MakeTex(ThemeColors.SurfacePanel);
        _texCard = MakeTex(ThemeColors.SurfaceRow);
        _texBarBg = MakeTex(ThemeColors.SurfaceScreen);
        _texGreen = MakeTex(ThemeColors.StatusOk);
        _texAmber = MakeTex(ThemeColors.StatusCaution);
        _texRed = MakeTex(ThemeColors.StatusCritical);
        _texCyan = MakeTex(ThemeColors.StatusNormal);
        _texBlue = MakeTex(ThemeColors.ButtonBorder);

        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _headerStyle.normal.textColor = ThemeColors.TextHeading;

        _subHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _subHeaderStyle.normal.textColor = ThemeColors.StatusNormal;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            wordWrap = true,
            richText = true
        };
        _labelStyle.normal.textColor = ThemeColors.TextBody;

        _monoStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            richText = true
        };
        _monoStyle.normal.textColor = ThemeColors.TextHeading;

        _badgeStyle = new GUIStyle(GUI.skin.box)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _badgeStyle.normal.textColor = ThemeColors.SurfaceScreen;   // dark ink on the bright status fill

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold
        };

        _scramButtonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold
        };
        _scramButtonStyle.normal.background = _texRed;
        _scramButtonStyle.hover.background = _texRed;
        _scramButtonStyle.active.background = _texAmber;
        _scramButtonStyle.normal.textColor = ThemeColors.SurfaceScreen;
        _scramButtonStyle.hover.textColor = ThemeColors.SurfaceScreen;
        _scramButtonStyle.active.textColor = ThemeColors.SurfaceScreen;

        _cardStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 8, 8)
        };
        _cardStyle.normal.background = _texCard;

        _stylesReady = true;
    }

    private static Texture2D MakeTex(Color col)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.SetPixels(new[] { col, col, col, col });
        tex.Apply();
        return tex;
    }

    private void OnGUI()
    {
        if (_bridge == null || _bridge.Sim == null) return;
        InitStyles();

        Matrix4x4 prevMatrix = GUI.matrix;
        float scaleX = Screen.width / 1920f;
        float scaleY = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scaleX, scaleY, 1f));

        // Full background
        GUI.DrawTexture(new Rect(0, 0, 1920, 1080), _texBg);

        var sim = _bridge.Sim;
        var p = sim.Plant;

        DrawTopBar(sim, p);
        DrawLeftColumn(p);
        DrawCenterColumn(p);
        DrawRightColumn(sim, p);

        GUI.matrix = prevMatrix;
    }

    private void DrawTopBar(Simulation sim, Plant p)
    {
        GUI.DrawTexture(new Rect(12, 10, 1896, 82), _texPanel);

        // Title & Status Badge
        GUI.Label(new Rect(26, 16, 360, 28), "REACTOR-SIM // PWR CONTROL DESK", _headerStyle);

        string httpInfo = (_bridge.HttpServer != null && _bridge.HttpServer.IsRunning)
            ? $"HTTP/MCP API: 127.0.0.1:{_bridge.HttpServer.Port}"
            : "HTTP API: offline";
        GUI.Label(new Rect(26, 46, 360, 22), $"<color={HexNormal}>{httpInfo}</color>  |  Sim Tick: #{sim.TickCount}", _labelStyle);

        // Criticality / Mode badge
        Rect badgeRect = new Rect(390, 18, 210, 56);
        Texture2D badgeTex = p.Scrammed ? _texRed : (p.Core.IsCritical ? _texGreen : _texAmber);
        GUI.DrawTexture(badgeRect, badgeTex);
        string statusText = p.Scrammed
            ? "SCRAM TRIPPED\nMODE: SHUTDOWN"
            : (p.Core.IsCritical ? $"REACTOR CRITICAL\nMODE: {p.Mode.ToString().ToUpperInvariant()}" : $"SUBCRITICAL\nMODE: {p.Mode.ToString().ToUpperInvariant()}");
        GUI.Label(badgeRect, statusText, _badgeStyle);

        // Sim Clock & Time Warp
        GUI.DrawTexture(new Rect(612, 16, 330, 62), _texCard);
        GUI.Label(new Rect(622, 20, 210, 24), $"<b>SIM TIME:</b> {sim.GameMinutes:F1} game-min", _monoStyle);
        if (GUI.Button(new Rect(835, 20, 96, 24), _bridge.Paused ? "RESUME" : "PAUSE", _buttonStyle))
        {
            _bridge.Paused = !_bridge.Paused;
        }
        GUI.Label(new Rect(622, 48, 105, 22), $"Warp: {_bridge.timeWarp:F1}x", _labelStyle);
        _bridge.timeWarp = GUI.HorizontalSlider(new Rect(720, 53, 145, 18), _bridge.timeWarp, 0.2f, 20.0f);
        if (GUI.Button(new Rect(872, 46, 58, 24), "1x", _buttonStyle)) _bridge.timeWarp = 1.0f;

        // Scenario Presets
        GUI.DrawTexture(new Rect(952, 16, 370, 62), _texCard);
        GUI.Label(new Rect(962, 18, 350, 20), "<b>LOAD SCENARIO PRESET:</b>", _labelStyle);
        if (GUI.Button(new Rect(962, 42, 84, 28), "Cold Shut", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.LoadPreset("cold-shutdown"));
        if (GUI.Button(new Rect(1050, 42, 84, 28), "Hot Stby", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.LoadPreset("hot-standby"));
        if (GUI.Button(new Rect(1138, 42, 84, 28), "100% Pwr", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.LoadPreset("full-power"));
        if (GUI.Button(new Rect(1226, 42, 86, 28), "Xenon Pit", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.LoadPreset("xenon-pit"));

        // Co-op Session Bar (U7)
        GUI.DrawTexture(new Rect(1332, 16, 360, 62), _texCard);
        var coop = _bridge.Coop;
        GUI.Label(new Rect(1342, 18, 345, 20), $"<b>CO-OP:</b> {coop.StatusMessage}", _labelStyle);
        if (coop.Role == CoopSessionManager.SessionRole.Standalone)
        {
            if (GUI.Button(new Rect(1342, 42, 86, 28), "Host :7777", _buttonStyle))
                coop.StartHost(7777);
            _joinAddress = GUI.TextField(new Rect(1434, 44, 110, 24), _joinAddress);
            _joinPort = GUI.TextField(new Rect(1548, 44, 52, 24), _joinPort);
            if (GUI.Button(new Rect(1604, 42, 78, 28), "Join", _buttonStyle))
            {
                int port = 7777;
                int.TryParse(_joinPort, out port);
                coop.StartClient(_joinAddress.Trim(), port);
            }
        }
        else
        {
            if (GUI.Button(new Rect(1342, 42, 140, 28), "Disconnect Co-op", _buttonStyle))
                coop.StopSession();
        }

        // SCRAM & Reset SCRAM Buttons
        if (GUI.Button(new Rect(1704, 16, 115, 62), "SCRAM\nREACTOR", _scramButtonStyle))
        {
            _bridge.SubmitIntent(PlantIntent.TriggerScram());
        }
        if (GUI.Button(new Rect(1825, 16, 75, 62), "RESET\nSCRAM", _buttonStyle))
        {
            _bridge.SubmitIntent(PlantIntent.ClearScram());
        }
    }

    private void DrawLeftColumn(Plant p)
    {
        float x = 12f;
        float y = 102f;
        float w = 616f;
        float h = 966f;
        GUI.DrawTexture(new Rect(x, y, w, h), _texPanel);

        var c = p.Core;
        var ch = p.Chemistry;
        var ic = CultureInfo.InvariantCulture;

        float cy = y + 12f;
        GUI.Label(new Rect(x + 16, cy, w - 32, 26), "1. CORE NEUTRONICS & REACTIVITY LEDGER", _subHeaderStyle);
        cy += 30f;

        // Power & Period Card
        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 108), _texCard);
        double mw = c.Power * 180.0;
        double totalMw = c.TotalHeatPower * 180.0;
        DrawBar(new Rect(x + 26, cy + 12, w - 52, 26), (float)Math.Min(1.2, c.Power) / 1.2f,
            c.Power > 1.08 ? _texRed : (c.Power > 0.02 ? _texGreen : _texCyan),
            string.Format(ic, "FISSION POWER: {0:P2}  ({1:F1} MWt)", c.Power, mw));

        DrawBar(new Rect(x + 26, cy + 44, w - 52, 22), (float)Math.Min(0.10, c.DecayHeatPower) / 0.10f,
            _texAmber,
            string.Format(ic, "DECAY HEAT: {0:P2}  |  TOTAL CORE HEAT: {1:F1} MWt", c.DecayHeatPower, totalMw));

        string periodStr = Math.Abs(c.PeriodSeconds) > 9000 ? "INF (Steady)" : string.Format(ic, "{0:+0.0;-0.0} s", c.PeriodSeconds);
        GUI.Label(new Rect(x + 26, cy + 74, w - 52, 24),
            string.Format(ic, "<b>REACTOR PERIOD (T):</b> {0}    |    <b>NET REACTIVITY:</b> <color={1}>{2:+0;-0;0} pcm</color> ({3:+0.00;-0.00;0.00} $)",
                periodStr,
                Math.Abs(c.TotalReactivityPcm) > 200 ? HexCrit : HexOk,
                c.TotalReactivityPcm,
                c.TotalReactivityDk / ReactorSim.Physics.BetaTotal),
            _monoStyle);
        cy += 118f;

        // Reactivity Ledger Breakdown Card
        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 152), _texCard);
        GUI.Label(new Rect(x + 26, cy + 6, w - 52, 22), "<b>REACTIVITY LEDGER BREAKDOWN (pcm = 10^-5 dk/k)</b>", _labelStyle);
        DrawLedgerRow(x + 26, cy + 30, "Clean Core Excess (k_inf)", c.BaseReactivityPcm);
        DrawLedgerRow(x + 320, cy + 30, "Control Rod Banks (D-A)", c.RodReactivityPcm);
        DrawLedgerRow(x + 26, cy + 54, "Soluble Boron Worth", c.BoronReactivityPcm);
        DrawLedgerRow(x + 320, cy + 54, "Fuel Doppler Defect", c.DopplerPcm);
        DrawLedgerRow(x + 26, cy + 78, "Moderator Temp Defect", c.ModeratorPcm);
        DrawLedgerRow(x + 320, cy + 78, "Xenon-135 Poisoning", c.XenonPcm);
        DrawLedgerRow(x + 26, cy + 102, "Iodine-135 Poisoning", c.IodinePcm);
        DrawLedgerRow(x + 320, cy + 102, "NET TOTAL REACTIVITY", c.TotalReactivityPcm, true);

        double peakXe, minToPeak, minToRec;
        p.Assistant.EstimateXenonPitWindow(p, out peakXe, out minToPeak, out minToRec);
        GUI.Label(new Rect(x + 26, cy + 126, w - 52, 22),
            string.Format(ic, "<b>Poison State:</b> I-135={0:F2} eq  |  Xe-135={1:F2} eq  |  Est. Xe Peak={2:F2} ({3:F0} pcm)  |  Pit Wait={4:F1}m",
                c.Iodine135, c.Xenon135, peakXe, peakXe * ReactorCore.XeWorthAtEq, minToRec),
            _labelStyle);
        cy += 162f;

        // Control Rod Banks (D, C, B, A)
        GUI.Label(new Rect(x + 16, cy, w - 32, 24), "2. CONTROL ROD DRIVE MECHANISMS (0.00 = OUT, 1.00 = IN)", _subHeaderStyle);
        cy += 28f;
        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 286), _texCard);

        string[] bankRoles =
        {
            "Bank D (Shutdown 1)",
            "Bank C (Shutdown 2)",
            "Bank B (Shutdown 3)",
            "Bank A (Regulating)"
        };

        float ry = cy + 10f;
        for (int i = 0; i < 4; i++)
        {
            var b = p.Banks[i];
            GUI.Label(new Rect(x + 26, ry, 240, 22),
                string.Format(ic, "<b>{0}:</b> Pos={1:F2} (Tgt={2:F2})", bankRoles[i], b.Insertion, b.InsertionTarget),
                _monoStyle);
            GUI.Label(new Rect(x + 275, ry, 315, 22),
                string.Format(ic, "Worth={0,6:F0} / {1:F0} pcm  |  Integrity={2:P0}", b.ReactivityPcm, b.WorthPcm, b.Integrity),
                _labelStyle);

            float oldTarget = (float)b.InsertionTarget;
            float newTarget = GUI.HorizontalSlider(new Rect(x + 26, ry + 24, 410, 20), oldTarget, 0.0f, 1.0f);
            if (Math.Abs(newTarget - oldTarget) > 0.002f && !p.Scrammed)
            {
                _bridge.SubmitIntent(PlantIntent.SetRodBank(i, Math.Round(newTarget, 3)));
            }
            if (GUI.Button(new Rect(x + 448, ry + 18, 66, 24), "OUT (0)", _buttonStyle))
                _bridge.SubmitIntent(PlantIntent.SetRodBank(i, 0.0));
            if (GUI.Button(new Rect(x + 520, ry + 18, 66, 24), "IN (1)", _buttonStyle))
                _bridge.SubmitIntent(PlantIntent.SetRodBank(i, 1.0));

            ry += 56f;
        }

        // Quick Rod Group Buttons
        if (GUI.Button(new Rect(x + 26, ry + 4, 180, 30), "Withdraw Shutdown (D-B)", _buttonStyle))
        {
            _bridge.SubmitIntent(PlantIntent.SetRodBank(0, 0.0));
            _bridge.SubmitIntent(PlantIntent.SetRodBank(1, 0.0));
            _bridge.SubmitIntent(PlantIntent.SetRodBank(2, 0.0));
        }
        if (GUI.Button(new Rect(x + 214, ry + 4, 180, 30), "Bank A -> Startup (0.44)", _buttonStyle))
        {
            _bridge.SubmitIntent(PlantIntent.SetRodBank(3, 0.44));
        }
        if (GUI.Button(new Rect(x + 402, ry + 4, 184, 30), "Insert All Banks (1.00)", _buttonStyle))
        {
            _bridge.SubmitIntent(PlantIntent.SetAllRods(1.0));
        }
        cy += 296f;

        // Chemistry & Volume Control System (CVCS)
        GUI.Label(new Rect(x + 16, cy, w - 32, 24), "3. CHEMICAL & VOLUME CONTROL (BORON / pH / ION-EX)", _subHeaderStyle);
        cy += 28f;
        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 250), _texCard);

        GUI.Label(new Rect(x + 26, cy + 10, w - 52, 24),
            string.Format(ic, "<b>SOLUBLE BORON:</b> {0:F1} ppm ({1:F0} pcm)    |    <b>COOLANT pH:</b> {2:F2} (Opt: {3:F1})",
                ch.BoronPpm, ch.BoronWorthPcm, ch.Ph, ch.OperativePh),
            _monoStyle);

        DrawControlSlider(x + 26, cy + 44, "Borate Injection (+ppm/s)", (float)ch.BorateRate, 0f, 10f, "F1",
            v => _bridge.SubmitIntent(PlantIntent.SetBorate(v)));
        DrawControlSlider(x + 26, cy + 92, "Pure Water Dilution (-ppm/s)", (float)ch.DiluteRate, 0f, 10f, "F1",
            v => _bridge.SubmitIntent(PlantIntent.SetDilute(v)));
        DrawControlSlider(x + 26, cy + 140, "NaOH pH Trim Dosing", (float)ch.NaohRate, -2f, 2f, "F2",
            v => _bridge.SubmitIntent(PlantIntent.SetNaoh(v)));
        DrawControlSlider(x + 26, cy + 188, "Ion-Exchange Demineralizer", (float)ch.IonExchangeRate, 0f, 1f, "P0",
            v => _bridge.SubmitIntent(PlantIntent.SetIonExchange(v)));
    }

    private void DrawCenterColumn(Plant p)
    {
        float x = 640f;
        float y = 102f;
        float w = 616f;
        float h = 966f;
        GUI.DrawTexture(new Rect(x, y, w, h), _texPanel);

        var th = p.Thermal;
        var ic = CultureInfo.InvariantCulture;

        float cy = y + 12f;
        GUI.Label(new Rect(x + 16, cy, w - 32, 26), "4. PRIMARY REACTOR COOLANT SYSTEM (RCS)", _subHeaderStyle);
        cy += 30f;

        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 300), _texCard);

        DrawBar(new Rect(x + 26, cy + 14, w - 52, 26), (float)Math.Min(1200.0, th.FuelTempC) / 1200f,
            th.FuelTempC > 850 ? _texRed : (th.FuelTempC > 650 ? _texAmber : _texGreen),
            string.Format(ic, "FUEL PELLET TEMP: {0:F1} C  (Nominal Full Power ~605 C, Limit 1200 C)", th.FuelTempC));

        DrawBar(new Rect(x + 26, cy + 48, w - 52, 26), (float)Math.Min(360.0, th.PrimaryTempC) / 360f,
            th.PrimaryTempC > 330 ? _texRed : _texCyan,
            string.Format(ic, "PRIMARY COOLANT TEMP: {0:F1} C  (Tsat @ {1:F1} bar = {2:F1} C)",
                th.PrimaryTempC, th.PrimaryPressureBar, th.PrimaryTempC + th.PrimarySatMarginC));

        DrawBar(new Rect(x + 26, cy + 82, w - 52, 26), (float)Math.Min(180.0, th.PrimaryPressureBar) / 180f,
            th.PrimaryPressureBar > 165 ? _texRed : _texBlue,
            string.Format(ic, "PRIMARY LOOP PRESSURE: {0:F1} bar  (Nominal 152 bar)", th.PrimaryPressureBar));

        DrawBar(new Rect(x + 26, cy + 116, w - 52, 26), (float)ReactorSim.Physics.Clamp(th.PrimarySatMarginC / 60.0, 0.0, 1.0),
            th.PrimarySatMarginC < 12.0 ? _texRed : _texGreen,
            string.Format(ic, "SUBCOOLING MARGIN: {0:F1} C  (Min Safe Margin: 15.0 C)", th.PrimarySatMarginC));

        GUI.Label(new Rect(x + 26, cy + 150, w - 52, 24),
            string.Format(ic, "<b>VESSEL LEVEL:</b> {0:P0}    |    <b>HEAD STEAM VOIDING:</b> <color={1}>{2:P1}</color>",
                th.PrimaryLevelFrac, th.SteamLevelFrac > 0.05 ? HexCrit : HexOk, th.SteamLevelFrac),
            _monoStyle);

        DrawControlSlider(x + 26, cy + 186, "Reactor Coolant Pumps (Primary Flow)", (float)th.PrimaryFlowFrac, 0f, 1f, "P0",
            v => _bridge.SubmitIntent(PlantIntent.SetPrimaryFlow(v)));

        if (GUI.Button(new Rect(x + 26, cy + 242, 180, 32), "RCP Pumps 100%", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.SetPrimaryFlow(1.0));
        if (GUI.Button(new Rect(x + 216, cy + 242, 180, 32), "RCP Pumps 50%", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.SetPrimaryFlow(0.5));
        if (GUI.Button(new Rect(x + 406, cy + 242, 180, 32), "Trip RCP Pumps (0%)", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.SetPrimaryFlow(0.0));

        cy += 314f;

        // Pressurizer Controls
        GUI.Label(new Rect(x + 16, cy, w - 32, 26), "5. PRESSURIZER (HEATER / SPRAY / PORV RELIEF)", _subHeaderStyle);
        cy += 30f;
        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 190), _texCard);

        DrawControlSlider(x + 26, cy + 14, "Pressurizer Heaters (Builds Subcooling Pressure)", (float)th.HeaterPowerFrac, 0f, 1f, "P0",
            v => _bridge.SubmitIntent(PlantIntent.SetHeaters(v)));
        DrawControlSlider(x + 26, cy + 68, "Pressurizer Cold Spray (Reduces Pressure)", (float)th.SprayFrac, 0f, 1f, "P0",
            v => _bridge.SubmitIntent(PlantIntent.SetSpray(v)));

        GUI.Label(new Rect(x + 26, cy + 132, 320, 28),
            $"<b>PILOT-OPERATED RELIEF VALVE (PORV):</b> {(th.ReliefOpen ? "<color=" + HexCrit + ">OPEN (VENTING)</color>" : "<color=" + HexOk + ">CLOSED</color>")}",
            _monoStyle);
        if (GUI.Button(new Rect(x + 360, cy + 128, 226, 36), th.ReliefOpen ? "CLOSE PORV VALVE" : "OPEN PORV RELIEF VALVE", _buttonStyle))
        {
            _bridge.SubmitIntent(PlantIntent.SetReliefValve(!th.ReliefOpen));
        }

        cy += 204f;

        // Secondary Loop / Steam Generator & Turbine
        GUI.Label(new Rect(x + 16, cy, w - 32, 26), "6. STEAM GENERATOR (SECONDARY) & TURBINE SINK", _subHeaderStyle);
        cy += 30f;
        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 350), _texCard);

        DrawBar(new Rect(x + 26, cy + 14, w - 52, 26), (float)Math.Min(85.0, th.SecondaryPressureBar) / 85f,
            th.SecondaryPressureBar > 68.0 ? _texRed : _texCyan,
            string.Format(ic, "STEAM GENERATOR PRESSURE: {0:F1} bar  (Tsat = {1:F1} C | Relief @ 75 bar)",
                th.SecondaryPressureBar, th.SecondaryTempC));

        DrawBar(new Rect(x + 26, cy + 48, w - 52, 24), (float)ReactorSim.Physics.Clamp(th.SecondaryLevelFrac, 0.0, 1.0),
            th.SecondaryLevelFrac < 0.30 ? _texRed : _texBlue,
            string.Format(ic, "SG SECONDARY WATER LEVEL: {0:P0}  (Nominal 60%)", th.SecondaryLevelFrac));

        GUI.Label(new Rect(x + 26, cy + 80, w - 52, 24),
            string.Format(ic, "<b>SG SAFETY RELIEF:</b> {0}    |    <b>EST. TURBINE OUTPUT:</b> {1:F1} MWe",
                th.SecondaryReliefOpen ? "<color=" + HexCrit + ">LIFTING (>75 bar)</color>" : "<color=" + HexOk + ">SEATED</color>",
                th.SteamDemandFrac * Math.Min(1.2, (th.SecondaryPressureBar - 1.0) / 59.0) * 60.0),
            _monoStyle);

        DrawControlSlider(x + 26, cy + 116, "Turbine Steam Demand Valve (Heat Sink)", (float)th.SteamDemandFrac, 0f, 1.1f, "P0",
            v => _bridge.SubmitIntent(PlantIntent.SetSteamDemand(v)));
        DrawControlSlider(x + 26, cy + 170, "Secondary Feedwater Makeup Pump", (float)th.FeedwaterFlowFrac, 0f, 1.5f, "P0",
            v => _bridge.SubmitIntent(PlantIntent.SetFeedwater(v)));

        if (GUI.Button(new Rect(x + 26, cy + 234, 180, 32), "Match Steam to Power", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.SetSteamDemand(ReactorSim.Physics.Clamp(p.Core.TotalHeatPower, 0.0, 1.1)));
        if (GUI.Button(new Rect(x + 216, cy + 234, 180, 32), "Trip Turbine (0% Sink)", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.SetSteamDemand(0.0));
        if (GUI.Button(new Rect(x + 406, cy + 234, 180, 32), "Full Load (96% Sink)", _buttonStyle))
            _bridge.SubmitIntent(PlantIntent.SetSteamDemand(0.96));
    }

    private void DrawRightColumn(Simulation sim, Plant p)
    {
        float x = 1268f;
        float y = 102f;
        float w = 640f;
        float h = 966f;
        GUI.DrawTexture(new Rect(x, y, w, h), _texPanel);

        var a = p.Assistant;
        float cy = y + 12f;

        // Section 7: Mission Tracker (U8)
        GUI.Label(new Rect(x + 16, cy, w - 32, 26), "7. TRAINING MISSIONS & GUIDED PROCEDURES", _subHeaderStyle);
        cy += 30f;
        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 224), _texCard);

        for (int i = 0; i < a.Missions.Count; i++)
        {
            string btnLabel = $"M{i + 1}: {(i == 0 ? "Startup" : (i == 1 ? "Power & SCRAM" : "Xenon Pit"))}";
            if (a.ActiveMissionIndex == i) btnLabel = "[ " + btnLabel + " ]";
            if (GUI.Button(new Rect(x + 26 + i * 198, cy + 10, 190, 28), btnLabel, _buttonStyle))
            {
                _bridge.SubmitIntent(PlantIntent.SetMission(i));
            }
        }

        var m = a.CurrentMission;
        if (m != null)
        {
            GUI.Label(new Rect(x + 26, cy + 44, w - 52, 22),
                $"<b>{m.Title}</b> — {(m.Completed ? "<color=" + HexOk + ">MISSION COMPLETE!</color>" : $"Step {m.CurrentStepIndex + 1} of {m.Steps.Count}")}",
                _monoStyle);

            float sy = cy + 68f;
            for (int i = 0; i < m.Steps.Count; i++)
            {
                var st = m.Steps[i];
                string prefix = st.Completed
                    ? "<color=" + HexOk + ">[DONE]</color>"
                    : (i == m.CurrentStepIndex ? "<color=" + HexWarn + ">[NEXT]</color>" : "<color=" + HexDim + ">[WAIT]</color>");
                GUI.Label(new Rect(x + 26, sy, w - 52, 20), $"{prefix} <b>{st.Title}</b>", _labelStyle);
                sy += 20f;
            }

            var curStep = m.Steps[m.CurrentStepIndex];
            GUI.Label(new Rect(x + 26, cy + 156, w - 52, 60),
                $"<b>Current Objective:</b> {curStep.Instruction}", _labelStyle);
        }
        cy += 234f;

        // Section 8: Ops Assistant Tier & Latest Advisory (U5)
        GUI.Label(new Rect(x + 16, cy, w - 32, 26), "8. OPS ASSISTANT & AUTOPILOT TIER", _subHeaderStyle);
        cy += 30f;
        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 198), _texCard);

        GUI.Label(new Rect(x + 26, cy + 10, 130, 24), "<b>ASSIST TIER:</b>", _monoStyle);
        AssistTier[] tiers = { AssistTier.Off, AssistTier.Advisory, AssistTier.Supervised, AssistTier.Autopilot };
        for (int i = 0; i < tiers.Length; i++)
        {
            string tName = tiers[i].ToString();
            if (a.Tier == tiers[i]) tName = "[" + tName.ToUpperInvariant() + "]";
            if (GUI.Button(new Rect(x + 140 + i * 118, cy + 8, 112, 26), tName, _buttonStyle))
            {
                _bridge.SubmitIntent(PlantIntent.SetAssist(tiers[i]));
            }
        }

        var adv = a.LatestAdvisory;
        if (adv != null)
        {
            GUI.Label(new Rect(x + 26, cy + 40, w - 52, 40),
                $"<b>[{adv.Category}] FINDING:</b> <color={HexWarn}>{adv.Finding}</color>", _labelStyle);
            GUI.Label(new Rect(x + 26, cy + 80, w - 52, 38),
                $"<b>EVIDENCE:</b> {adv.Evidence}", _labelStyle);
            GUI.Label(new Rect(x + 26, cy + 118, w - 52, 40),
                $"<b>RECOMMENDATION:</b> <color={HexOk}>{adv.Recommendation}</color>", _labelStyle);

            if (a.Tier == AssistTier.Supervised && a.PendingSupervisedIntent != null)
            {
                if (GUI.Button(new Rect(x + 26, cy + 158, w - 52, 30),
                    $"CONFIRM & EXECUTE SUGGESTED ACTION: {a.PendingSupervisedIntent}", _buttonStyle))
                {
                    _bridge.SubmitIntent(a.PendingSupervisedIntent);
                }
            }
            else if (adv.SuggestedIntent != null && a.Tier == AssistTier.Advisory)
            {
                if (GUI.Button(new Rect(x + 26, cy + 158, w - 52, 30),
                    $"APPLY ADVISORY ACTION: {adv.SuggestedIntent}", _buttonStyle))
                {
                    _bridge.SubmitIntent(adv.SuggestedIntent);
                }
            }
        }
        else
        {
            GUI.Label(new Rect(x + 26, cy + 60, w - 52, 40),
                "Ops Assistant is currently set to OFF. Select Advisory, Supervised, or Autopilot above.", _labelStyle);
        }
        cy += 208f;

        // Section 9: Active Alarms Annunciator (R11)
        GUI.Label(new Rect(x + 16, cy, w - 32, 26), $"9. ACTIVE ALARMS ({a.ActiveAlarms.Count}) — CAUSE / LIMIT / TREND / ACTION", _subHeaderStyle);
        cy += 30f;
        Rect alarmBox = new Rect(x + 14, cy, w - 28, 224);
        GUI.DrawTexture(alarmBox, _texCard);

        float contentH = Math.Max(210f, a.ActiveAlarms.Count * 96f);
        _alarmScroll = GUI.BeginScrollView(new Rect(x + 18, cy + 4, w - 36, 216), _alarmScroll, new Rect(0, 0, w - 58, contentH));
        if (a.ActiveAlarms.Count == 0)
        {
            GUI.Label(new Rect(10, 12, w - 70, 30), "<color=" + HexOk + "><b>ALL SYSTEMS NOMINAL — NO ACTIVE ALARMS</b></color>", _labelStyle);
        }
        else
        {
            float ay = 6f;
            for (int i = 0; i < a.ActiveAlarms.Count; i++)
            {
                var al = a.ActiveAlarms[i];
                Texture2D strip = al.Severity == AlarmSeverity.Critical ? _texRed : _texAmber;
                GUI.DrawTexture(new Rect(6, ay, 6, 86), strip);
                GUI.Label(new Rect(18, ay, w - 80, 20),
                    $"<b>[{al.Severity.ToString().ToUpperInvariant()}] {al.Title}</b>  ({al.CurrentVsLimit} | Trend: {al.Trend})", _monoStyle);
                GUI.Label(new Rect(18, ay + 20, w - 80, 34), $"<b>Cause:</b> {al.Cause}", _labelStyle);
                GUI.Label(new Rect(18, ay + 54, w - 80, 30), $"<b>Action:</b> <color={HexOk}>{al.NextAction}</color>", _labelStyle);
                ay += 92f;
            }
        }
        GUI.EndScrollView();
        cy += 234f;

        // Section 10: Assistant Log & Live Telemetry Stream
        GUI.Label(new Rect(x + 16, cy, w - 32, 24), "10. OPS ASSISTANT & TELEMETRY LOG", _subHeaderStyle);
        cy += 26f;
        GUI.DrawTexture(new Rect(x + 14, cy, w - 28, 160), _texCard);

        var lines = sim.GetTelemetryHistorySnapshot();
        int showCount = Math.Min(12, lines.Count);
        float logH = Math.Max(148f, showCount * 20f + 10f);
        _logScroll = GUI.BeginScrollView(new Rect(x + 18, cy + 4, w - 36, 152), _logScroll, new Rect(0, 0, w - 58, logH));
        float ly = 4f;
        for (int i = lines.Count - 1; i >= Math.Max(0, lines.Count - showCount); i--)
        {
            GUI.Label(new Rect(6, ly, w - 68, 20), lines[i], _labelStyle);
            ly += 20f;
        }
        GUI.EndScrollView();
    }

    private void DrawBar(Rect r, float frac, Texture2D fillTex, string text)
    {
        GUI.DrawTexture(r, _texBarBg);
        float clamped = Mathf.Clamp01(frac);
        if (clamped > 0.001f)
        {
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * clamped, r.height - 4), fillTex);
        }
        GUI.Label(new Rect(r.x + 8, r.y + 2, r.width - 16, r.height - 4), text, _monoStyle);
    }

    private void DrawLedgerRow(float x, float y, string label, double pcm, bool boldTotal = false)
    {
        string color = pcm > 1.0 ? HexOk : (pcm < -1.0 ? HexWarn : HexBody);
        if (boldTotal)
            color = Math.Abs(pcm) > 200.0 ? HexCrit : HexOk;
        GUI.Label(new Rect(x, y, 275, 22),
            string.Format(CultureInfo.InvariantCulture, "{0}: <color={1}>{2:+0;-0;0} pcm</color>", label, color, pcm),
            boldTotal ? _monoStyle : _labelStyle);
    }

    private void DrawControlSlider(float x, float y, string label, float value, float min, float max, string fmt, Action<float> onChanged)
    {
        GUI.Label(new Rect(x, y, 420, 20), $"<b>{label}:</b> {value.ToString(fmt, CultureInfo.InvariantCulture)}", _labelStyle);
        float next = GUI.HorizontalSlider(new Rect(x, y + 22, 560, 20), value, min, max);
        if (Math.Abs(next - value) > 0.003f)
        {
            onChanged?.Invoke((float)Math.Round(next, 3));
        }
    }
}
