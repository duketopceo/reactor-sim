# Playtest guide (Windows build)

The sandbox is the front door: launch, pick **Sandbox**, and run the plant from cold shutdown. Missions stay
reachable from the menu but are optional.

## Launch

```
<repo>\out\build\ReactorSim.exe
```

(Built on a Windows machine with Unity with `BuildScript.BuildWindows`; output is `out/build/ReactorSim.exe` under the repo root.)

Menu: **Sandbox** (cold shutdown, primary pumps off, default pace, assistant hints on), **Missions** (the guided M1
path, same cold start), **Quit**.

## Checklist (one session)

Note the real time you start and anything that feels confusing, fun or broken as you go.

The startup order is the checklist in [STARTUP.md](STARTUP.md). In the sandbox the checklist panel is on screen
top-left from the start and shows the current step; press **P** to expand the full list, and stand up from the
seat to see the reactor window in the south wall. Follow the panel and note where its wording or targets mislead you.

1. Start Sandbox. Confirm the plant is cold and quiet and the panel shows step 1 of 13 (start the primary pumps).
2. Lineup: start the primary pumps (flow 80 % or more), heaters on (60 bar or more), cooling water and vacuum
   pump on (condenser 0.3 bar or lower).
3. Pull shutdown banks D, C and B fully out. Boron still holds the core subcritical.
4. Dilute boron to about 1200 ppm (diluting, not borating), then stop the dilution.
5. Withdraw bank A to about half, then trim it a little at a time until the critical lamp is on and power is
   above 2 %. Watch flux and period between pulls.
6. Heat the primary to 250 C with a small steam draw, turbine governor on auto load, then open the throttle in
   small steps to 2940 rpm, trimming bank A.
7. Close the generator breaker (red lamp), raise power to about 40 % with bank A, boron and the throttle, then
   take 20 % or more grid load. Expect about an hour of game time; raise the pace only during heat-up.
8. Try the clock: pause, pace up, pace down, skip ahead. Check the plant behaves while paused and after a skip.
9. Press **Escape**: resume, volume slider, alarm mute, then **Reset sandbox** and confirm it is cold again.
10. Take the plant up again and **SCRAM** it (two clicks) once. The panel should name the step that unticked.
11. Quit to menu from Escape; confirm it says the sandbox is not saved.

Write down: what confused you, what was fun, what broke (include the approximate time so it can be found in the log).

## Controls

| Input | Action |
|---|---|
| Left-drag on a dial or lever | turn the dial / move the lever |
| Left-click a button | press it |
| SCRAM | needs two clicks (arm, then confirm) |
| Right mouse (hold) + move | look around |
| Mouse wheel | zoom (drifts toward the hovered control) |
| C or middle mouse | recentre view and zoom |
| P | expand or collapse the startup checklist panel |
| Escape | menu: resume, reset sandbox, master volume, alarm mute, quit to menu (pauses the sim while open) |

The sandbox is not saved. Quitting to the menu discards the plant.

## Where the logs live

On the Windows box, under `%USERPROFILE%\AppData\LocalLow\duketopceo\ReactorSim\`:

- `Player.log` and `Player-prev.log` (Unity's player log, current and previous run)
- `sessions\<yyyyMMdd-HHmmss>\` one folder per session (each menu entry or reset starts a new one):
  - `log.jsonl` one line per sim tick (power, reactivity, temperatures, pressures, alarms, intents applied)
  - `telemetry.csv` the same ticks as a CSV
  - `intent-log.jsonl` every intent applied or rejected, with issuer and class
  - `session.json` build version, start time, tuning values, tick counts, and `"ended":true` after a clean quit

The files are flushed as they are written and the intent log and header are refreshed every 30 seconds, so a
crash still leaves a usable record.

## Getting the logs back (from a Linux or macOS machine over ssh)

```
export REACTOR_SIM_WIN_HOST=my-windows-box          # ssh host of the machine that ran the build
bash scripts/fetch-playtest-logs.sh                 # everything
bash scripts/fetch-playtest-logs.sh --latest 3      # only the three newest sessions
bash scripts/fetch-playtest-logs.sh --host other --dest /tmp/pt
```

Agent smoke run (no clicking, works over ssh): `ReactorSim.exe -batchmode -nographics -smoke 30` skips the menu,
runs the sandbox for 30 real seconds and quits normally, which writes a session folder and `Player.log` to fetch.

Files land in `out/playtest/<timestamp>/`. The script exits 1 and names what is missing when a folder or log does
not exist, and exits 2 when the host is unreachable. It only reads from the Windows box.

## Not in this build

- Dev mode (U9) is not wired yet: the local HTTP/MCP dev API is off in the player and dev presets are
  unreachable. Use the in-room controls and the Escape menu only.
- No chaos panel, casualty events, quips or grid desk yet (U3, U4, U5, U8, U12).

## Reporting something odd

Tell the agent roughly when it happened and what you did. The session folder and `Player.log` are enough to
find it; if you are in dev mode, save a repro bundle as well and mention it.
