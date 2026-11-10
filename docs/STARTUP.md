# Startup: cold shutdown to power on the grid

One ordered checklist takes the sandbox plant from cold shutdown to a synced generator carrying load. It lives in
data, `ReactorSim.Unity/Assets/StreamingAssets/procedures.json` (procedure `cold-start-to-grid`, 13 steps), and is
evaluated against live plant state by Core `Procedures/Procedure.cs`.

A step is ticked only when the plant state meets its done-condition, never by a click. A step unticks when its
condition stops holding, and every step after it unticks with it. The current step is the first unticked one.
A SCRAM from power therefore sends you back to the rod step; the lineup steps (1 to 3) stay ticked.

## Where to see it

- **Checklist panel.** In the sandbox a compact panel sits top-left from the start: phase, step number of 13,
  the action, the control, the target and the live value. It is off in missions. Press **P** to expand it to the
  full list with ticks, and use "Show me" on a step to outline the controls involved. The panel hides while the
  tablet or the Escape menu is open, and it shows a caution line naming the step that unticked after a SCRAM or trip.
- **Reactor window.** The control room has a wide, low window in the south wall beside the door. It is a gallery
  about 2 m over the water of a deep, circular reactor pool, looking down at a small modular reactor module (see
  "Reactor hall" in [PLAYTEST.md](PLAYTEST.md)). It is behind the seated operator, so stand up: from the standing spot
  (and from the seat position at standing height) you see the pool and the module head under the surface, and the crane
  parked behind it; walk up to the rail to look down onto the module. The blue glow comes from the spent-fuel racks on the pool ledge and follows
  core power (a visual cue), so you can watch the approach to criticality from the window as well as on the flux
  instruments.

## Phase table

Game minutes are from `dotnet run --project ReactorSim.Runner -- --scenario startup-grid` (a scripted operator
using Player intents only, from the sandbox start). "At" is the game minute the step first ticked; "+" is the
time since the previous step ticked. Total: **67.3 game minutes** to grid output at or above target.
Steps that tick on the same minute as the one before them were already satisfied by the earlier action.

| # | Step | Action | Control | Target and done-condition | Why it comes here | At (min) | + (min) |
|---|---|---|---|---|---|---|---|
| 1 | start-rcps | Start the reactor coolant pumps so water moves through the core before anything heats. | Primary flow control | Flow 80 % or more | Heat from the core has to be carried away from the first moment; heaters and rods assume moving water. | 0.1 | 0.1 |
| 2 | pressurize | Heaters on. Bring primary pressure up so the water stays liquid when it warms. | Pressurizer heater control | Primary pressure 60 bar or more | Pressure must be up before the primary warms, or the water would boil in the core. | 0.5 | 0.4 |
| 3 | draw-vacuum | Cooling water on and vacuum pump running, so the condenser is ready before any steam exists. | Vacuum pump knob, cooling water knob | Condenser 0.3 bar or lower | The condenser must be able to take steam before the first steam is made; it is slow to pull down. | 0.6 | 0.1 |
| 4 | pull-shutdown-banks | Pull shutdown banks D, C and B fully out. Boron still holds the core subcritical. | Bank D, C and B levers | All three banks out (under 5 % inserted) | Takes the big rod worth out first, while the core is still safely held down by boron. | 2.3 | 1.8 |
| 5 | dilute-boron | Dilute boron down to about 1200 ppm, then stop the dilution. Boron is the slow, coarse control. | Dilute rate knob | Boron 1200 ppm or lower, then dilute to zero | Boron sets the coarse reactivity budget so that bank A alone can finish the approach. | 5.1 | 2.8 |
| 6 | withdraw-bank-a | Withdraw bank A to about half. Move slowly and watch flux between pulls. | Bank A lever | Bank A about 50 % inserted or less | Gets close to critical in a coarse move, leaving only fine trimming. | 6.1 | 1.0 |
| 7 | reach-critical | Trim bank A until flux holds steady with a long period. Critical, power above 2 %. | Bank A lever | Critical lamp on, power 2 % or more | The fine approach: small pulls, then wait and watch the period. | 7.4 | 1.3 |
| 8 | heat-up | Heat the primary with the reactor. Keep the steam draw below reactor power while it warms. | Bank A lever, throttle | Primary 250 C or hotter | The reactor is the only heat source; a small steam draw lets steam exist without cooling the primary. | 42.6 | 35.2 |
| 9 | auto-load | Turbine governor on auto load, so it follows the grid once synced. | Auto/manual load switch | Auto load on | A one-switch step set before the roll so the governor is ready at speed. | 42.6 | 0.0 |
| 10 | roll-turbine | Open the throttle in small steps and bring the turbine to rated speed. Trim bank A as you go so the primary holds its heat. | Throttle handwheel, bank A lever | Rotor 2940 rpm or more | Steam to the turbine pulls heat from the primary, so rods must follow. | 47.3 | 4.8 |
| 11 | close-breaker | Close the generator breaker inside the sync band. Red lamp on means synced. | Generator breaker switch | Breaker closed (red lamp) | Only at rated speed and frequency can the generator join the grid. | 47.3 | 0.0 |
| 12 | raise-power | With the generator on the grid, raise power with bank A and boron, and open the throttle in step so steam matches power. | Bank A lever, dilute knob, throttle | Power about 40 % | Power can rise only as fast as steam is taken. | 67.3 | 19.9 |
| 13 | take-load | Take load on the grid. | Grid demand knob | Grid output 20 % or more | Last, once there is steam and power to carry it. | 67.3 | 0.0 |

The scenario groups them into four phases (the panel shows the phase): A. Lineup (1 to 3), B. Approach (4 to 7),
C. Heat-up and sync (8 to 11), D. Power on the grid (12 to 13). Measured: approach to critical about 7.4 min,
heat-up about 35 min at about 15 % power, turbine roll about 5 min, raise power about 20 min.

The scenario holds step targets and policies against each other: a step cap of 45 game minutes fails the run
naming the step and live values, and the run ends with no SCRAM and no balance-of-plant trip. `--verify` check
`STARTUP-01` runs it twice and compares the per-step minutes and the final state hash.

## Pace

One game minute is 60 simulated seconds. The player scale ranges 0.5 to 60 sim-seconds per real second
(`Tuning.cs`, default 10), so at the default one game minute takes 6 real seconds (about 6 minutes for the whole
startup) and at 60 it takes 1 real second.

- **Safe to run fast.** The lineup (1 to 3) is over in about half a minute of game time. Heat-up (step 8) is
  35 minutes of waiting with nothing to decide, so it is the natural place to raise the scale, as long as steam
  demand stays below reactor power.
- **Go slow.** Steps 5 to 7 (dilute, withdraw, reach critical) and the turbine roll and breaker close
  (10, 11). Pulls and throttle moves need a look at the flux, period, rotor speed and primary temperature in
  between.
- **Raise power (12).** Moderate: rods, boron and the throttle move together and the plant needs time between
  steps.
- **Auto-slow.** `SimClock` caps the effective scale at 3 (`AutoSlowScale`) when the reactor period drops below
  60 s (`AutoSlowPeriodSeconds`, while it lasts) and, held for 20 real seconds (`AutoSlowHoldSeconds`), after a SCRAM
  or a balance-of-plant trip. So a fast run slows itself on a short period near criticality, on a runaway,
  and after a trip, then the scale you chose applies again. A skip-ahead (runs at the top of the range) also stops
  on a short period, a trip, a SCRAM or a new caution-or-worse alarm. Slowing down yourself before the final
  trims in step 7 is still better than relying on it.

## Playtest failure modes

These come from a recorded playtest session and are covered by `STARTUP-01`.

- **Steam demand first.** Opening the turbine throttle (0.05 then 0.11) before anything else does nothing useful:
  the plant stays cold and subcritical with step 1 still current. The checklist keeps pointing at the primary
  pumps.
- **Boron raised instead of diluted.** Boron is the dilution knob, not a borate knob. Pushing boron up adds
  negative reactivity, so bank withdrawal never reaches critical. Target is 1200 ppm or lower, which means
  diluting, then stopping the dilution at zero.
- **Too much steam in heat-up.** A draw at or above reactor power cools the primary and stalls step 8; keep
  steam below power until 250 C.
- **Rods left alone in the roll.** Steam pulls heat from the primary, so bank A has to be trimmed in step with the
  throttle, small moves, or temperature and power sag.
