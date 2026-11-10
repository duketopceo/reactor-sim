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
seat and walk to the reactor window in the south wall (see "Reactor hall" below). Follow the panel and note where its wording or targets mislead you.

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

**Dials and levers are turned by click, hold, drag up or down, release.** A press with no drag changes nothing; the dial
starts to move once the pointer has travelled a few pixels, up raises and down lowers. While you drag, the pointer is
locked so it cannot leave the window, a readout shows the label, the value with its unit and the change since the press,
and the knob turns live. Release commits. Clicking a gauge, an out-of-service row or a control that is out of reach shows a
one-line note saying what it is and what to do.

What each key does in each state:

| Input | Walking | Seated | Dragging a dial | In a menu |
|---|---|---|---|---|
| W A S D | walk, with a short ramp up and down | nothing | nothing | nothing |
| Shift (held) | sprint | nothing | nothing | nothing |
| Ctrl (held) | nothing | nothing | fine adjustment: the drag moves 5x slower | nothing |
| F | sit on the chair in front of you (a prompt shows near it) | stand up | nothing | nothing |
| Q / E | nothing | swivel the chair left / right | nothing | nothing |
| 1 to 6 | go to a section of the board | go to a section of the board | nothing | nothing |
| Left mouse | press a button; start a dial drag | same | the drag | menu buttons |
| Right mouse | hold to look around; click on a gauge to pin it | same (yaw limited) | click cancels the drag and restores the value | nothing |
| Mouse wheel | zoom (drifts toward the hovered control) | same | nothing | nothing |
| C or middle mouse | recentre view and zoom | same | nothing | nothing |
| P | expand or collapse the startup checklist | same | nothing | nothing |
| Escape | open the menu | open the menu | cancels the drag and restores the value; the menu stays closed | closes the menu |

SCRAM needs two clicks (arm, then confirm). Losing window focus or opening the menu during a drag cancels it the same way
as Escape. Mouse sensitivity, field of view, text scale and invert-Y are in the Escape menu. Walk speed, sprint, the
ramp times and the drag feel (gain, dead zone, fine factor, pixels per sweep) are in
`ReactorSim.Unity/Assets/StreamingAssets/input-tuning.json`; edit it and restart, no rebuild needed.

The sandbox is not saved. Quitting to the menu discards the plant.

## Where the logs live

On the Windows box, under `%USERPROFILE%\AppData\LocalLow\duketopceo\ReactorSim\`:

- `Player.log` and `Player-prev.log` (Unity's player log, current and previous run)
- `sessions\<yyyyMMdd-HHmmss>\` one folder per session (each menu entry or reset starts a new one):
  - `log.jsonl` one line per sim tick (power, reactivity, temperatures, pressures, alarms, intents applied)
  - `telemetry.csv` the same ticks as a CSV
  - `intent-log.jsonl` every intent applied or rejected, with issuer and class
  - `input.jsonl` input telemetry: dial hover, press, rejected press with its reason, drag start / move / commit / cancel, and
    walking samples (position, speed, distance asked versus achieved)
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

## Reactor hall (the window in the south wall)

The window looks down from a gallery about 9 m over the water into a circular hall with a 22 m deep pool. Walk up to the
rail (within about a metre) to see the pool; from across the room the window shows the far wall and the crane.

- **The module.** A small modular reactor of the pumped integral type: a 2.7 m wide, 18.8 m tall reactor vessel standing in
  a 4.5 m wide containment shell, in the pool with the containment head just under the water. The steam generators and the
  pressurizer are inside the vessel, so there is no external primary loop. What you see outside the vessel is what such a
  design has: canned recirculation pump motors on the vessel shoulder, chemical-and-volume-control, safety-injection and vent
  penetrations through the containment wall, and the steam and feedwater lines that leave through the containment head and run
  to the pool wall.
- **Cutaway.** The containment is drawn as a cutaway: an opaque steel shell with a vertical slot facing the window, closed in
  its upper part by a translucent skin, so you can see the vessel and pump housings. This is a depiction choice; a real
  containment is an opaque shell.
- **The glow is a cue.** The blue glow over the spent-fuel racks on the pool ledge follows reactor power (dim at shutdown, bright
  at rated power). Real spent-fuel Cherenkov light comes from the fuel's own decay, not from current reactor power, so this
  scaling is a visual cue for the player, not a physics claim.
- **Performance.** The hall switches itself off when the window is out of view. Its budget (checked against a baseline run, see
  below, for every captured size) is at most 4 ms of added frame time at p95, at most 120 added draw calls, a p99 of 12 ms or
  less in the gallery pose, and at most 1 ms of added p95 while the window is out of view.

### Capturing the hall

On the Windows machine (graphics session):

```
bash scripts/capture-playtest.sh --sizes 1920x1080 --poses gallery,away,hall,glowoff,glowidle,glowmid,glowfull
bash scripts/check-captures.sh <dir> --only glowoff,glowidle,glowmid,glowfull --allow-skip gallery,away,hall   # rack glow rises with power
bash scripts/check-captures.sh <dir> --baseline <baseline dir> --budget-only                                   # runtime budget
```

The baseline is a run of the same poses on a build with the old hall: the commit before the hall build, with the capture poses
(`gallery`, `away`) added to it, because those poses do not exist in older builds. Frame time on a shared machine is noisy (the
same build has measured between 5.7 and 15.6 ms at p95 in different runs), so run the budget check on a quiet machine, and
compare runs made back to back.

## Not in this build

- Dev mode (U9) is not wired yet: the local HTTP/MCP dev API is off in the player and dev presets are
  unreachable. Use the in-room controls and the Escape menu only.
- No chaos panel, casualty events, quips or grid desk yet (U3, U4, U5, U8, U12).

## Reporting something odd

Tell the agent roughly when it happened and what you did. The session folder and `Player.log` are enough to
find it; if you are in dev mode, save a repro bundle as well and mention it.
