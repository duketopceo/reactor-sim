# Plant counterpart binding inventory

This is a source audit of the current scene, not visual or audio sign-off. The shared source for current readings is the frame-cached `PlantBridge.GetSnapshot()`. Fractions below are fractions of the simulation's rated values unless called a command. The whole-plant map is a schematic; its activity brightness is not a calibrated flow meter.

| Counterpart | Simulation source | Current representation | Remaining work |
|---|---|---|---|
| Core/fuel | `Power`, `FuelTempC` | Wall and hall use `GlowMapping`; temperature changes tint and power changes emission. | Verify visually at cold, intermediate and full power. The glow is an artistic cue, not measured light. |
| Rod banks A-D | `BankInsertion` in D/C/B/A array order | Wall and hall both use `PlantWallMapModel.BankInsertion`. | Verify all four bank movements and SCRAM against the desk. |
| Primary pumps and loop | `PrimaryFlowFrac` | Hall rotor and pump sound use flow; wall loop brightness and status use flow and shared alarm predicates. | Add directional cue and verify loss-of-flow behavior; values are aggregate, not individual pump measurements. |
| Pressurizer and primary relief | `PrimaryPressureBar`, `PrimarySatMarginC`, `HeaterPowerFrac`, `SprayFrac`, `ReliefOpen` | Wall pressurizer lamp uses pressure/margin and relief status; relief sound follows the boolean. | Separate heater, spray and relief visuals and readable values. The currently placed hall FBXs do not contain the older `PlantEquipment` relief anchors. |
| Steam generator | `SecondaryPressureBar`, `SecondaryTempC`, `SecondaryLevelFrac`, `SecondaryReliefOpen` | Wall status uses shared alarm predicates and relief state. | Add level and temperature/pressure readings and dedicated relief geometry. The snapshot exposes a shared secondary state, not independent generator readings. |
| Steam to turbine | `TurbineSteamKW` | Wall steam and exhaust brightness use heat rate; turbine steam sound uses this output instead of the throttle command. | Audition in-game and verify the full trip and no-pressure scenarios. Heat rate is not steam mass flow. |
| Steam dump | `DumpSteamKW` | Dedicated wall bypass brightness uses calculated dump heat. | Verify the bypass still indicates activity when the turbine is tripped; the hall has no separate dump geometry. |
| Feedwater | `FeedwaterDeliveredFrac`, `FeedwaterFlowFrac`, `FeedwaterAvailabilityFrac` | Wall route and summary use delivered fraction. Feed-pump status uses the command and the shared `FEED_FAIL` predicate. | Clearly distinguish pump command, delivered flow and supply availability; verify starvation. |
| Turbine and shaft | `TurbineRpm`, `TurbineTripped` | Hall rotor and turbine sound follow RPM; wall shaft brightness follows RPM and turbine lamp follows trip. | Add a clear miniature rotor cue and verify coastdown and trip. |
| Generator and grid | `GeneratorSynced`, `GeneratorKw`, `GridOutputFrac`, `GridFrequencyHz` | Wall status and electrical route, summary output/frequency and generator sound follow the snapshot. | Add visible breaker/sync state and readable generation and frequency. |
| Condenser, hotwell and cooling | `CondenserPressureBar`, `HotwellInventoryFrac`, `CoolingWaterFrac`, `VacuumPumpFrac` | Wall status and cooling path follow snapshot fields; cooling sound uses cooling and vacuum controls. | Add distinct vacuum, inventory and cooling readouts and corresponding visible equipment. |
| Chemistry | `BoronPpm`, `Ph`, `BorateRate`, `DiluteRate`, `NaohRate`, `IonExchangeRate` | Wall chemistry lamp and boron summary follow the snapshot. The drawn chemistry route is static. | Show chemistry readings and commanded rates with accurate labels; the snapshot has no measured flow for that specific drawn route. |
| Surge and safety routes | No specific measured route flow in the snapshot | Static circuit markings; wall safety lamp follows SCRAM. | Mark route flow unavailable, and add only state/paths supported by the simulation. |
| Pool atmosphere | `Power` modulates `ReactorWaterAtmosphere` particles | Bubbles are decorative, with a baseline even at zero power. | Keep them explicitly atmospheric; do not present them as a measurement of boiling or coolant flow. |
| Alarms and operating mode | Snapshot plus `LampPredicates` and desk `LampContext` | Desk and wall use shared predicates, including the desk's configured short-period context. | Verify counts and acknowledgement behavior across desk, tablet and wall; first-out is not currently exposed as a wall readout. |
| Procedure and clock | `SimSeconds`; procedure state maintained outside the snapshot | Existing clipboard/mission UI shows procedure progress; wall currently omits it. | Add an authoritative clock and procedure readout without inferring first-out or an active step. |

## Relief-anchor limitation

The old `PlantEquipment` Blender asset defines separate primary and steam-generator relief anchors, but `HallAssetPlacer` currently places `ReactorCore`, `VesselInternals` and `RotatingEquipment`, not `PlantEquipment`. Splitting the binder's signals prevents mixing the two states when those anchors are present; it does not by itself add visible relief jets to the current hall. New hall geometry or appropriate authored anchors and a visual check are still needed.

## Verification and remaining work

The grouped `ComponentReadout` model prepares labeled values for each wall component, but it is not yet connected to a visible detail view; its layout and readability need an in-game design pass. Pump and pressurizer status mappings are separate, and the safety lamp no longer implies the unmodeled safety system is healthy merely because SCRAM is clear.

Automated model tests cover pump/pressurizer independence, missing and invalid readings, and turbine steam after a trip. A Unity play-mode fixture checks the two relief signals independently against temporary authored anchor types; those anchors remain absent from the currently placed hall assets. A listening pass, custom short-period tuning check, and the full visual scenario matrix remain to be done.
