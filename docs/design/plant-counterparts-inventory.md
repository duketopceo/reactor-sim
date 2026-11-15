# Plant counterpart binding inventory

This is a source audit of every presentation surface: the desk, the status wall, the reactor hall and the sound. It is not visual or audio sign-off. Every cue reads the frame-cached `PlantBridge.GetSnapshot()`; field names below are `PlantSnapshot` fields. Fractions are of the simulation's rated value unless labeled a command. The whole-plant map is a schematic; its activity brightness is not a calibrated flow meter.

**Unavailable rule.** A reading with no authoritative source is shown as the single token `N/M` (not measured) on every text surface, with no unit, and as `PlantWallMapModel.Status.Unavailable` on lamps and nodes. It is never approximated from a neighbouring value. `PlantWallMapModelTests` locks this in, together with the two labeling rules below.

**Labeling rules.** A demanded rate is labeled `% command` and never as delivered flow. A heat rate is labeled `kW heat` and never as steam mass flow.

## Visual and readout counterparts

| Counterpart | Snapshot field (unit) | Actual or command | States shown | Unavailable | Phase 1 owner |
|---|---|---|---|---|---|
| Core/fuel | `Power` (fraction), `FuelTempC` (C), `TotalReactivityPcm` (pcm), `PeriodSeconds` (s) | Actual | Glow tint and emission (`GlowMapping`), wall readouts | Glow is an artistic cue, not measured light | U5 visual pass |
| Rod banks A-D | `BankInsertion` (fraction, array order D, C, B, A) | Actual | Wall and hall via `PlantWallMapModel.BankInsertion` | None | U5 |
| Primary pumps and loop | `PrimaryFlowFrac` (fraction), `PrimaryTempC` (C), `PrimaryLevelFrac` (fraction) | Actual, aggregate | Hall rotor, wall loop brightness and status | Individual pump readings: `N/M` | U2 direction cue |
| Pressurizer | `PrimaryPressureBar` (bar), `PrimarySatMarginC` (C), `HeaterPowerFrac`, `SprayFrac` (fraction) | Actual | Wall lamp and readouts | None | U2 |
| Primary relief | `ReliefOpen` (bool) | Actual | Wall readout `RELIEF OPEN/CLOSED`; hall jet at `VFX_PORV_SteamJet_Anchor` (`ReliefAnchors.Primary`, containment head, pressurizer end) via `HallSteamModel.PrimaryRelief` | None | none (sound: done, U6) |
| Steam generator | `SecondaryPressureBar` (bar), `SecondaryTempC` (C), `SecondaryLevelFrac` (fraction) | Actual, one shared secondary state | Wall status and readouts | Per-generator readings: `N/M` | U2 |
| Secondary relief | `SecondaryReliefOpen` (bool, opens above 75 bar) | Actual | Wall readout; hall jets at `VFX_SGRelief_SteamJet_Anchor_1/2` (`ReliefAnchors.Secondary`, steam line) via `HallSteamModel.SecondaryRelief` | None | none (sound: done, U6) |
| Turbine throttle | `SteamDemandFrac` (fraction) | Command, labeled `% command` | Wall SG readout | None | none |
| Steam to turbine | `TurbineSteamKW` (kW heat) | Actual delivered heat rate | Wall steam and exhaust brightness, turbine steam sound | Steam mass flow: not modeled | U5 trip check |
| Bypass steam | `DumpSteamKW` (kW heat) | Actual | Wall bypass brightness and `DUMP` readout; hall jet at `VFX_Bypass_SteamJet_Anchor` (`ReliefAnchors.Bypass`, steam line) with intensity `HallSteamModel.Bypass` | None (sound: `ReactorSoundMix.DumpSteam`, bypass source at the same anchor) | none |
| Feedwater | `FeedwaterDeliveredFrac` (rated), `FeedwaterFlowFrac` (command), `FeedwaterAvailabilityFrac` (fraction) | Delivered is actual; pump is a command | Wall route and readouts, `FEED_FAIL` predicate | None | U5 starvation check |
| Turbine and shaft | `TurbineRpm` (rpm), `TurbineTripped` (bool) | Actual | Hall rotor, turbine sound, wall shaft and lamp | None | U5 coastdown check |
| Generator and grid | `GeneratorSynced` (bool), `GeneratorKw` (kW), `GridOutputFrac` (rated), `GridFrequencyHz` (Hz) | Actual | Wall status, route, readouts, generator sound | None | U2 |
| Condenser, hotwell, cooling | `CondenserPressureBar` (bar), `HotwellInventoryFrac`, `CoolingWaterFrac` (fraction), `VacuumPumpFrac` (command) | Pressure, inventory and cooling actual; vacuum pump is a command | Wall status, cooling path, cooling sound | Separate condenser equipment: none | U2 |
| Chemistry | `BoronPpm` (ppm), `Ph`, `BorateRate`, `DiluteRate`, `NaohRate`, `IonExchangeRate` (commands) | Concentrations actual; rates are commands | Wall lamp and readouts; drawn route is static | Chemistry route flow: `N/M` | U2 |
| Surge and safety routes | none | n/a | Static markings; safety lamp follows `Scrammed` | Route flow: `N/M` (`SAFETY FLOW N/M`) | none |
| Pool atmosphere | `Power` | Decorative | `ReactorWaterAtmosphere` bubble streams | Not a measurement of boiling or flow | none |
| Alarms and mode | `Mode`, alarm predicates (`LampPredicates`, desk `LampContext`), `AlarmAckSeq` | Actual | Desk and wall lamps, horn | First-out: `N/M` (alarm list is unordered) | U6 priority, U5 acknowledgement check |
| Clock and procedure | `SimSeconds` (s); procedure state lives outside the snapshot | Clock actual | Wall `CLOCK`; clipboard shows procedure | Procedure step on the wall: `N/M` | none |

## Audio layers

| Layer | Source | Current state | Phase 1 owner |
|---|---|---|---|
| Pump hum | `PrimaryFlowFrac` | `ProceduralPlantAudio` (non-spatial), Plant category gain | Hall positioning left to U5 listening pass |
| Turbine and steam | `TurbineRpm`, `TurbineSteamKW` | Follows delivered steam, so a tripped turbine is silent; Plant category gain | none |
| Relief steam | `ReliefOpen`, `SecondaryReliefOpen` | `ReactorSoundMix.PrimaryRelief` and `SecondaryRelief` each drive their own positioned source in `ReactorSoundscape`; each relief lift has its own sting (`AlarmFeedback`, Alarms category) | none |
| Bypass steam | `DumpSteamKW` | `ReactorSoundMix.DumpSteam` drives a positioned bypass source; sounds while the turbine is tripped and bypass steam flows | none |
| Generator and cooling | `GeneratorSynced`, `GeneratorKw`, `CoolingWaterFrac`, `VacuumPumpFrac` | Procedural loops, Plant category gain | none |
| Horn and stings | Horn alarms (`scram`, `bop-trip`, `high-pressure`, `high-fuel-temp`) via `AlarmTracker`, acknowledged by `AlarmAckSeq` | Horn volume is the Alarms category gain (0 while alarm mute is on); `AudioPriority` ranks the horn names, SCRAM highest | none |
| Voice | `VoiceAnnouncer` edge-triggered keys | Lines in one tick are collected and `AudioPriority.Pick` plays the highest rank; a higher-ranked alarm line interrupts a lower one; Voice category gain | none |
| Volume | `AudioListener.volume` (Master) and per-source category gains from the pause menu | Master, Plant, Alarms and Voice sliders persisted in `UserSettings` (`reactorsim.volume.*`, full when absent); alarm mute toggle overrides Alarms for the session | none |

## Relief anchors

`HallBuilder.BuildReliefAnchors` authors the anchors into the hall (Phase 1 U3); the legacy `PlantEquipment` asset is a separate whole plant and is never placed (see `docs/design/reactor-core-assets.md`). Positions come from `ReliefAnchors` (UnityEngine-free, tested): the PORV on the containment head at the pressurizer end, opposite the steam lines; two SG relief valves and the bypass tee on the steam line that runs from the head to the pool wall at `HallDimensions.PipeRunHeight`. `PlantEquipmentBinder` binds them by name prefix (`VFX_PORV_SteamJet`, `VFX_SGRelief`, `VFX_Bypass_SteamJet`) and `ReactorSoundscape` puts the matching sounds at the same points. The anchors sit about 21.9 m above the pool floor, under the surface; whether the jets show through the observation window is recorded with the U3 renders, not assumed.

## Verification

`PlantWallMapModel.GroupedReadout` is shown on the `LiveDetail` plate and was confirmed live in play mode. The per-component `ComponentReadout` text is not yet displayed (Phase 1 U2). Model tests cover pump and pressurizer independence, missing and invalid readings, turbine steam after a trip, the unavailable rule and both labeling rules. A Unity play-mode fixture checks the two relief signals against temporary anchors. A listening pass and the full visual scenario matrix remain (Phase 1 U5).
