# Roadmap

Where Reactor Sim is headed, in rough order. Shipped work is in the [releases](https://github.com/duketopceo/reactor-sim/releases); plans live in `docs/plans/`.

## Next

1. **Quick visual polish and textures.** Light texture pass on walls, floor, panels and benches so the room stops reading as flat colour. Keep it cheap: trim sheets and simple procedural materials from the Blender scripts.
2. **A simple player character.** A basic physical operator (body, hands or arms in view, a visible shadow) so the room has scale. One simple asset, no animation system yet.
3. **A better assistant and autopilot.** Make autopilot worth watching: it runs the startup procedure step by step, says what it is doing and why, and moves the real controls on the panels so you can follow along. Make the assistant's advice clearer at the lower tiers.
4. **Controls redone for UI and UX.** Rework dials, levers and buttons: clear grab affordances, consistent drag feel, visible detents and limits, feedback when a control is at a stop or rejected.
5. **Readable names, hover info and clipboards.**
   - Plain control and instrument names.
   - Tidier hover tooltips: layout, spacing, units, what the value means, what to do with it.
   - Better word spacing and layout on the clipboard and checklist.
   - Clearer dial labels.
6. **One overhead status screen.** A large retro-style display above the consoles showing the whole plant at a glance: power, reactivity, temperatures, pressures, flows, alarms, the current procedure step.

## Later

7. **Everything visible in the reactor hall.** Whatever the operator does should show up in the plant: rod motion at the head, flow and boiling in the pool, steam lines, pumps running, the glow following power, and alarms with matching lights or effects in the hall.
8. **Plant cameras.** Camera feeds of hardware around the plant (reactor head, pumps, turbine, condenser, switchyard), viewable on control-room screens.
9. **Balance of plant in 3D.** Condenser, piping runs, turbine hall and cooling towers as real spaces, built in stages. The plan appendix covers runtime cost.

## Ongoing

- Keep the startup procedure, scenarios and the `--verify` physics checks green as features land.
- The three known physics defects tracked as XFAIL (`DOM-01`, `SCR-02` SCRAM latch, `EDGE-08`) are queued for fixes.
- Releases ship with the scene rebuilt by the builder; CI fails if the committed scene drifts.
