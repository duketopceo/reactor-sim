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
| Primary relief | `ReliefOpen` (bool) | Actual | Wall readout `RELIEF OPEN/CLOSED`; hall jet when an anchor exists | Hall anchor absent in the placed hall | U3 anchor, U6 sound |
| Steam generator | `SecondaryPressureBar` (bar), `SecondaryTempC` (C), `SecondaryLevelFrac` (fraction) | Actual, one shared secondary state | Wall status and readouts | Per-generator readings: `N/M` | U2 |
| Secondary relief | `SecondaryReliefOpen` (bool, opens above 75 bar) | Actual | Wall readout; hall jet when an anchor exists | Hall anchor absent in the placed hall | U3 anchor, U6 sound |
| Turbine throttle | `SteamDemandFrac` (fraction) | Command, labeled `% command` | Wall SG readout | None | none |
| Steam to turbine | `TurbineSteamKW` (kW heat) | Actual delivered heat rate | Wall steam and exhaust brightness, turbine steam sound | Steam mass flow: not modeled | U5 trip check |
| Bypass steam | `DumpSteamKW` (kW heat) | Actual | Wall bypass brightness and `DUMP` readout | Hall visual and sound: none yet | U3 visual, U6 sound |
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
| Pump hum | `PrimaryFlowFrac` | `ProceduralPlantAudio`, not hall-positioned | U6 positioning |
| Turbine and steam | `TurbineRpm`, `TurbineSteamKW` | Follows delivered steam, so a tripped turbine is silent | U6 |
| Relief steam | `ReliefOpen`, `SecondaryReliefOpen` | One merged level in `ReactorSoundMix.Relief`; only primary relief stings (`AlarmFeedback`) | U6 splits into primary and secondary |
| Bypass steam | `DumpSteamKW` | No sound | U6 |
| Generator and cooling | `GeneratorSynced`, `GeneratorKw`, `CoolingWaterFrac`, `VacuumPumpFrac` | Procedural loops | U6 categories |
| Horn and stings | Horn alarms (`scram`, `bop-trip`, `high-pressure`, `high-fuel-temp`) via `AlarmTracker`, acknowledged by `AlarmAckSeq` | Works; no category volume | U6 categories and priority |
| Voice | `VoiceAnnouncer` edge-triggered keys | Only SCRAM interrupts; no priority queue | U6 priority |
| Volume | `AudioListener.volume` from the pause menu | Master only, not persisted; alarm mute toggle | U6 categories and persistence |

## Relief-anchor limitation

`HallAssetPlacer` places `ReactorCore`, `VesselInternals` and `RotatingEquipment`. The legacy `PlantEquipment` asset holds old relief anchors but is a separate whole plant and is never placed in the hall (see `docs/design/reactor-core-assets.md`). `PlantEquipmentBinder` already binds the two relief signals separately to any `VFX_PORV_SteamJet*` and `VFX_SGRelief*` anchors under the hall root, so the remaining work is authoring those anchors into the hall (Phase 1 U3).

## Verification

The `LiveDetail` plate shows the per-component `PlantWallMapModel.ComponentReadout` of the wall component the operator clicked (`WallDetailModel`, Phase 1 U2), or a select-a-component prompt when nothing is selected; `GroupedReadout` is no longer displayed (its values are in the mounted summary and the per-component readouts, and it stays in the model and its tests). Route direction cues (`PlantWallMapModel.RouteDirection`) exist only for the eight routes whose flow sign the model knows (primary hot and cold, steam header, steam, steam dump, turbine exhaust, feedwater, grid) and only while their activity is above `FlowDirectionMin`; the cooling circuit, shaft, surge, chemistry and safety routes have none. Model tests cover pump and pressurizer independence, missing and invalid readings, turbine steam after a trip, the unavailable rule and both labeling rules. A Unity play-mode fixture checks the two relief signals against temporary anchors. A listening pass and the full visual scenario matrix remain (Phase 1 U5).
