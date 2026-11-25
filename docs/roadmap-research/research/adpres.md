# ADPRES, deep-dive research note (physics-rigor reference for reactor-sim)

**Subject:** [imronuke/ADPRES](https://github.com/imronuke/ADPRES), "An Open Nuclear Reactor Simulator and Reactor Core Analysis Tool"
**Researched:** 2026-11-25. Read-only: GitHub API via `gh.py`, a depth-1 clone into `/tmp` (deleted scratch, nothing pushed), the published paper's abstract, and KOMODO (the successor) for the obsolescence question.
**Status of the repo:** MIT, Fortran only (286,747 bytes Fortran, 1,840 shell), 30 stars, 10 forks, 2 watchers, created 2017-04-11, last push 2021-01-09. ~507 commits. README opens with: "ADPRES IS NOW OBSOLETE. USERS ARE ENCOURAGED TO VISIT THE NEWER VERSION NAMED KOMODO."

## 1. What it is

ADPRES (Abu Dhabi Polytechnic Reactor Simulator) solves the **static and transient multi-group neutron diffusion equation in 1D/2D/3D Cartesian geometry**, with optional thermal-hydraulics feedback. Single author: Muhammad Imron (Abu Dhabi Polytechnic). Explicitly a teaching/research code: "a great learning tool for reactor theory classes… make all nuclear engineering students have access [to] a reactor simulator code." Version string in the sample output is "ADPRES 1.2".

It is **not** a plant simulator. There is no secondary side, no balance of plant, no operator interface, no depletion/burnup, no xenon model, no decay-heat model, no pin-power reconstruction, no DNB/CHF or boiling model. It is a **core neutronics solver with a simplified enclosed-channel TH model bolted on**. That narrowness is exactly what makes it a useful rigor reference: everything it does, it does at solver-grade precision, and everything it omits is visible.

Published verification: Imron, M. (2019), "Development and verification of open reactor simulator ADPRES", *Annals of Nuclear Energy* 133, 580–588, doi:10.1016/j.anucene.2019.06.049. Abstract (via IAEA INIS): verified against the PWR MOX/UO2 static benchmark, the LMW 3D transient, and the NEACRP 3D PWR transient benchmarks; "generally accurate and consistent with the solutions from other computer codes."

## 2. Code structure (9,939 lines, 9 files)

| File | Lines | Role |
|---|---|---|
| `src/ADPRES.f90` | 89 | Main program: read input, dispatch on `%MODE`, print CPU-time breakdown |
| `src/mod_data.f90` | 219 | `module sdata`, all global state: XS arrays, geometry, iteration controls, TH params, transient params |
| `src/mod_io.f90` | 4,089 | Input parsing (`%`-cards), input echo, output (assembly/axial power, flux maps) |
| `src/mod_xsec.f90` | 862 | XS update: base + linear feedback coefficients, or branched-table interpolation; XS sanity checks |
| `src/mod_nodal.f90` | 1,453 | Nodal kernels: FDM, PNM (= NEM), SANM (default); transverse leakages, buckling |
| `src/mod_cmfd.f90` | 1,337 | CMFD acceleration, outer/inner iterations, BiCGSTAB linear solver, fission-source extrapolation |
| `src/mod_th.f90` | 962 | TH iteration, fuel-pin conduction, steam-table lookups, critical-boron search |
| `src/mod_trans.f90` | 749 | Rod ejection / RIA transient driver, precursor update, reactivity, beta |
| `src/mod_control.f90` | 179 | `forward`, `adjoint`, `fixedsrc` drivers |

Main dispatch (`ADPRES.f90`): `FIXEDSRC` → `fixedsrc()`; `ADJOINT` → `adjoint()`; `RODEJECT` → `rod_eject()` or `rod_eject_th()` depending on TH flag; `BCSEARCH` → `cbsearch()` / `cbsearcht()`; default → `forward()`. After the run it prints a CPU-time breakdown (input / XS / CMFD / nodal / TH) and warns if any transient outer iteration failed to converge ("YOU MAY NEED TO REDUCE TIME STEP"). The whole program is one global mutable state (`module sdata`, `SAVE` everywhere), idiomatic Fortran-90 research code, not a design to copy.

## 3. Solver architecture

### 3.1 Spatial discretisation: three kernels, SANM by default

`%KERN` selects the nodal kernel (`docs/kern.md`): FDM (finite difference), PNM (polynomial nodal method, "equivalent to Nodal Expansion Method"), SANM (semi-analytic nodal method, the default). History per `docs/method.md`: the 2017 original used NEM with response-matrix formulation and quadratic transverse leakage approximation (QTLA); it was "slow performance compared to other modern nodal simulators", so in early 2020 the code was revamped around CMFD (see 3.2), first with PNM, then SANM "for better accuracy".

What SANM actually is, in this code (`mod_nodal.f90`, `nodal_update`, `get_ABEFGH` ~line 1345): within each node the 1D transverse-integrated diffusion equation is solved **analytically**, the flux shape is a combination of hyperbolic functions, not a polynomial. The code computes buckling `B²` per group and the expansion coefficients from `sinh`/`cosh` of `alp` (e.g. `m0c = sinh(alp)/alp`, `Aa(g) = (sinh(alp) - m1s)/(alp2*m1s)`). Transverse leakages (the coupling to neighbouring directions) are carried as zeroth, first and second moments (`TLUpd1`, `TLUpd2`, `get_source`). The practical difference vs PNM: SANM captures the exponential flux shape near strong absorbers / reflectors without refining the mesh, which is why it is more accurate on coarse assembly-size nodes (~20 cm). FDM is kept as a reference kernel, on 1 cm meshes it converges to the same answer, which is itself a verification device (see §6).

### 3.2 CMFD acceleration: the performance story

The 2020 revamp's headline claim (`docs/method.md`): CMFD with two-node problems and non-linear iteration (Smith 1984; Zimin & Ninokata 1997/98) made time-dependent problems **10–20× faster** than the original NEM version. Structure (`mod_cmfd.f90`):

- A coarse-mesh finite-difference (CMFD) system is built from the high-order nodal solution's coupling coefficients (`coup_coef`, `matrix_setup`, `nodal_upd`, the "non-linear iteration": nodal update only every `NUPD` outer iterations, default `(NX+NY+NZ)/2.5`).
- The CMFD linear system is solved with **BiCGSTAB** (`bicg`, adapted from a CFD-Online sample, the code cites the URL in a comment).
- Power iteration on the fission source with **fission-source extrapolation** every `NAC` outer iterations (default 5).
- Separate outer loops for forward (`outer`), fixed-source (`outer_fs`), adjoint (`outer_ad`), TH-coupled (`outer_th`), and transient (`outer_tr`) problems.
- Convergence criteria (`%ITER`, defaults): fission-source relative error 1e-5, flux relative error 1e-5, max 500 outer iterations, max 2 inner iterations per outer.

The sample IAEA3D run in `docs/quick-guides.md` converges in 129 outer iterations to k-eff = 1.029082 in 0.1565 s total (CMFD 57%, nodal update 38%). That is the scale: a full 3D 2-group core eigenvalue solve in ~0.2 s on a laptop. The lesson is not the absolute speed, it is that the code **prints its iteration history and convergence errors**, so a wrong answer is visible.

### 3.3 Multi-group diffusion and cross sections

- Arbitrary number of energy groups (`NG`); all sample problems are 2-group. Per node and group: transport XS, absorption, νΣf, Σf, χ, scattering matrix, diffusion coefficient, removal XS.
- Two XS input paths (`mod_xsec.f90`): `%XSEC` (explicit few-group constants per material) or `%XTAB` (branched tables in the OECD MOX/UO2 benchmark format, e.g. `smpl/xsec/SERPENT_CMM/m40.tab`, branches over coolant density, boron, fuel temp, moderator temp, with rodded/unrodded sets and per-material β/λ/neutron velocity).
- Feedback without tables is **linear**: `%FTEM`, `%MTEM`, `%CDEN`, `%BCON`, `%CROD` cards each supply dΣ/dT etc. per group per material, and `XS_updt` rebuilds node XS as base + Σ (coefficient × Δparameter). With `%XTAB`, `XStab_updt` does multi-dimensional interpolation (`brInterp`) instead.
- **Assembly discontinuity factors** (`%ADF`): per-face, per-group, per-material factors with rotation rules (90°/180°/270°) for diagonally symmetric assemblies. This is the standard fix for homogenisation error at assembly interfaces, the thing that makes a nodal solution match a heterogeneous transport reference at interfaces.
- **XS sanity checks** (`check_xs`): hard `STOP` if any diffusion coefficient is ~0/negative or any removal XS is negative. There is also a "subroutine to check negative xs" (commit 2020-06-12). Input errors are caught at read time with explicit messages (`mod_io.f90`, e.g. "CBCS CARD IS NOT NECESSARY FOR THIS CALCULATION MODE"). This defensive posture, fail loudly, never silently produce a plausible wrong number, is the single most stealable habit in the repo.

### 3.4 Time discretisation and delayed neutrons

- **Theta method** (`%THET`, default θ = 1.0 = fully implicit; 0.5 = Crank–Nicolson). Fully implicit is the safe default for stiff kinetics.
- **6 delayed-neutron precursor groups** (`nf = 6`, hard-coded in `mod_data.f90`), supplied either on the `%EJCT` card (β, λ, neutron velocity per group) or per-material from the XTAB library. Precursor equations are solved **analytically** over each step (Stacey 1969): `uPden` updates per-node precursor density with the exact exponential `pxe = exp(-λ·ht)` and the a1/a2 weighting of old/new fission source. Precursors are stored **per node** (`c0(nnod, nf)`), not lumped, the spatial distribution of delayed-neutron source is retained.
- **Exponential (frequency) transformation** (`%EXTR`, Hendricks 1975): ω = ln(f0/ft)/Δt per node per group, used to factor out the fast exponential rise during a rod ejection from hot zero power. Off by default.
- **Two time-step sizes** (`%EJCT` line 2: `TTOT TSTEP1 TDIV TSTEP2`): fine steps during the ejection, coarse steps after, e.g. 0.005 s then 0.05 s in the sample. This is the standard RIA practice and directly applicable to any fast-transient scenario design.
- **Reactivity is adjoint-weighted** (`reactivity` in `mod_trans.f90`): it computes ρ from the adjoint flux, not from a k-eff ratio. Core-averaged β is likewise adjoint-weighted when XTAB data is present (`calc_beta`).
- **k-eff forcing (`KNE1`)**: if the steady state before a transient does not have k-eff = 1 within 1e-5, the code **divides νΣf by k-eff and re-solves, up to 10 times, then STOPs**. It prints a warning that it is doing this. Honest and explicit, but note it is a fudge: it bakes the steady-state error into the fission XS rather than fixing the input. A game should not copy the fudge, but should copy the *refusal to start a transient from a non-critical steady state*.

### 3.5 Rod ejection / RIA

`rod_eject()` / `rod_eject_th()` (`mod_trans.f90`): steady-state solve → adjoint solve → initial precursor densities → bank motion per `%EJCT` (final position, start time, speed in steps/s per bank) → time stepping with per-step output of time, reactivity in dollars, relative power, average/max fuel and moderator temperatures, and bank positions. Control-rod modelling (`%CROD`): banks on a 228-step scale (0 = fully inserted), a radial bank map, and XS increments per group for rodded nodes. A 2019-02-21 commit adds a **rod-cusping option**, the classic problem that a partially inserted rod in a coarse axial node is misrepresented by volume-weighting the rodded/unrodded XS. (The commit message is the evidence; the cusping implementation itself was not re-read in detail.)

### 3.6 Critical-boron search

`cbsearch()` (`mod_th.f90`, line 752): a **secant iteration** on boron concentration. First guess from the reference boron, second guess `bcon + (k-1)·bcon`, then secant updates `bcon = bc2 + (1-ke2)/(ke1-ke2)·(bc1-bc2)` until |k-1| < 1e-5 *and* both fission-source and flux errors < 1e-5. Hard guards: STOP if boron exceeds 3000 ppm, goes negative, or 20 iterations pass. `cbsearcht()` is the TH-coupled variant. Simple, robust, and a good template for any "find the control setting that makes the core exactly critical" feature, including as a scenario-setup or calibration tool.

### 3.7 Thermal-hydraulics feedback

`mod_th.f90`, `th_iter` + `th_upd` (+ a transient variant). This is the weakest part of the code physically, and the most instructive about where to draw lines:

- **Enclosed-channel model** (after NODAL3): each radial node is one coolant channel; mass and energy conservation along the channel, no cross-flow, no subchannel mixing, single-phase only.
- Coolant enthalpy marched axially from the inlet; temperature/density/Prandtl/viscosity/conductivity from a **9-point steam table** (`stab`, `ntem = 9` in `mod_data.f90`) by linear interpolation, with a 10% extrapolation clamp outside the table range. Nine points is crude, a cautionary tale, not a model. (reactor-sim's IAPWS-IF97 is strictly better; do not regress to this.)
- Fuel pin: **10 radial meshes in the fuel meat + gap + clad** (`nm = 10`, `nt = 12`), 1D radial conduction solved as a tridiagonal system (`TridiaSolve`, Thomas algorithm) with temperature-dependent UO2 conductivity (`getkf`: k = 1.05 + 2150/(T − 73.15)) and clad conductivity, **fixed gap conductance Hg = 1e4 W/m²K** (hard-coded, no gap-conductance model), Dittus–Boelter for the clad-to-coolant heat-transfer coefficient (`Nu = 0.023·Re^0.8·Pr^0.4`).
- **Lumped fuel temperature** for Doppler feedback: `ftem = 0.3·T_centerline + 0.7·T_fuel_surface` (α = 0.7 in `th_upd`). That weighting is a recognised effective-temperature convention, worth knowing it exists, and worth noting it is a *choice*, not physics.
- TH iteration: outer loop updates XS from (T_fuel, T_mod, ρ_cool, boron, rod position) → neutronics solve → power distribution → TH update → repeat until max fuel-temp change < 0.01 K and neutronics errors are within criteria, max 30 TH iterations, else STOP with "CALCULATION MIGHT BE NOT CONVERGED". `cf` = fraction of heat deposited directly in coolant.
- Transient TH (`th_upd` transient variant, ~line 442) does the same pin conduction implicitly in time.

What it does **not** have: no boiling/void model, no DNB or CHF check, no pump/coastdown, no pressure dynamics, no natural circulation, no secondary side. The TH exists only to feed Doppler + moderator feedback into the neutronics. For a game, that is the correct scope to imitate, and the code shows how little TH you need before the neutronics feedback is credible.

## 4. Verification culture, what it actually does, and how thin it is

Benchmarks shipped in `smpl/` (inputs only, no expected-output files in the repo):

- **Static:** IAEA2D, IAEA3Ds, BIBLIS, DVP (BWR, ADF demo), KOEBERG, NEACRP A1–C2 (+ `neacrp_cbcs` boron search), MOX/UO2 Parts 1b/1d/1–3 with HELIOS- and Serpent-generated XS, FDM and PNM variants of the same problems, adjoint, fixed-source.
- **Transient:** LMW (3D rod ejection), NEACRP A1t–C2t, MOX Part 4 (HELIOS + Serpent).
- **XS libraries:** `smpl/xsec/`, HELIOS 2-group files and Serpent `.tab` branch tables.

Published numbers I could verify from the repo itself (not the paywalled paper):

- **IAEA3D** (`docs/quick-guides.md`, echoed in the input header): PARCS k-eff 1.029096 vs ADPRES 1.029082, **1.4 pcm** difference.
- **BIBLIS** (header in `smpl/static/BIBLIS`): reference k-eff 1.02511, max assembly-power error 0.14%.
- The paper's abstract confirms verification against MOX/UO2 Part 1 (static), LMW and NEACRP (transient), by comparison with other codes, code-to-code verification, not plant-data validation.

CI (`.travis.yml`, now dead, Travis CI is defunct): builds with gfortran `-O4` and **runs 12 sample inputs**. That is the whole test suite, and it has **no assertions**. A case "passes" if the executable exits; nobody greps k-eff. There is a codecov badge, but coverage of a Fortran code with no unit tests is coverage of the happy path. There are no expected-output files, no tolerances, no regression harness in the repo. This is the honest limit of its verification culture: strong on *benchmark problems as fixtures* and on *comparing against reference codes in a paper*, weak on *automated regression*. (KOMODO later fixed exactly this, see §5.)

Other verification habits worth stealing:

- **Input echo**: `mod_io.f90` writes the parsed input back into the `.out` file and re-describes it; the manual warns "ADPRES may run well without any error but it gives you wrong results… if the input is not consistent with the problem specification." Echo + re-description is cheap and catches a whole class of silent errors.
- **Reference-solution cross-checks**: the same problem is shipped in FDM (fine mesh), PNM and SANM variants, three independent discretisations of one answer.
- **Iteration history printed**: every outer iteration's k-eff, fission-source error and inner error, plus explicit markers ("FISSION SOURCE EXTRAPOLATED", "NODAL COUPLING UPDATED", max coupling change and its node). Convergence is inspectable, not asserted.
- **CPU-time breakdown** per phase, so a performance regression is visible.
- **Convergence criteria are user-visible and explicit** (1e-5 defaults), not buried constants.

## 5. Why it went obsolete, what KOMODO replaced and why

KOMODO ([imronuke/KOMODO](https://github.com/imronuke/KOMODO)) is **not a rewrite and not a physics upgrade**. Per its README: "KOMODO is continuation of ADPRES. Since KOMODO name is considered more neutral and institutional-independent, it is expected that more contributors would join in this project." The obsolescence was a **rebranding + line-of-development move**, not a technical refutation. ADPRES's last two commits are literally "ADPRES is obsolete" (2020-12-26) and "ADPRES is deprecated" (2021-01-09); KOMODO was created 2020-07-13.

Measured differences (diffed the two checkouts, 2026-11-25):

- Same feature list verbatim (SANM/PNM/FDM, CMFD + BiCGSTAB, adjoint/fixed-source, boron search, RIA, ADFs, branched XS). Same input format, same sample problems, same docs (with KOMODO substituted for ADPRES).
- KOMODO is **actively maintained**: 85 stars, 20 forks, last push 2026-11-18 (a week before this research). Recent commits: "catching input error and make error message clear" (2026-11-10), "Fixed bug on heat transfer coef correlation #10" (2026-11-09), "Added HDF5 output to save phase space data. Also prevented BICGSTAB breakdown for infinite homogeneous problems" (2026-08-21).
- Additions over ADPRES: **partial OpenACC GPU acceleration** (`mod_gpu.f90`, 61 lines, allocation/initialisation wrappers), **HDF5 output** (`mod_hdf5.f90`, 554 lines, per-step phase-space data), **CMake build** (`CMakeLists.txt`), **restructured control module** (boron search moved from `mod_th` into `mod_control`), and, most importantly, **`test.sh` (15 KB) with numeric baselines**: expected k-eff/boron/peak-power/fuel-temp values per case with tolerances, run-and-compare. That is the regression harness ADPRES never had.
- External validation since the rename: Alnaqbi et al., *Nuclear Engineering and Technology* 54 (2022) 764–769, "Static and transient analyses of APR1400 initial core using KOMODO", two-step method with CASMO-4 group constants, verified against PARCS and SIMULATE-3, "excellent agreement", including an RIA simulation. So the codebase's credibility now accrues to the KOMODO name.

Net: ADPRES died of **naming, maintenance consolidation, and missing test infrastructure**, not of wrong physics. Anyone citing or building on this line should cite KOMODO and the 2019 paper, and treat ADPRES as the frozen 2021 snapshot.

## 6. Genuinely good / weak / missing, honest ledger

**Genuinely good**

- Method stack is the real one: SANM + CMFD non-linear iteration + BiCGSTAB is what production nodal codes (PARCS, SIMULATE-3) do; the 10–20× speedup claim from the CMFD revamp is plausible and the code structure shows why.
- Per-node precursor densities with analytic update, adjoint-weighted reactivity and β, exponential transformation for HZP ejections, the transient treatment is careful, not a toy.
- Branched XS tables with rodded/unrodded sets and ADFs: the full two-step-method interface, usable with Serpent/HELIOS/CASMO-generated libraries.
- Defensive input handling: card validation, XS sanity checks with hard STOP, input echo, explicit non-convergence warnings.
- Tiny dependency footprint: one Fortran compiler, no libraries. IAEA3D solves in ~0.16 s.
- Documentation is unusually complete for a one-person code: every input card documented with examples; theory page cites its sources instead of re-deriving them.

**Weak**

- **No assertion-based tests** (see §4). The CI only checked "it runs".
- TH is a single-phase enclosed-channel model with a 9-point steam table, fixed gap conductance, no boiling/DNB/void, no pressure or flow dynamics. Fine for feedback; not a TH code.
- `KNE1` silently rescales νΣf to force k-eff = 1, a fudge, honestly labelled but a fudge.
- Global mutable state everywhere (`module sdata`); no modularity that would let you reuse the neutronics without the whole program. No parallelism at all (KOMODO adds partial GPU).
- Error handling is `STOP` with a printed message, fine for batch, useless for embedding.
- Hard-coded limits scattered in code: 3000 ppm boron cap, 20 boron-search iterations, 10 k-eff-forcing iterations, 6 precursor groups, 228-step rod scale, 9-point steam table.
- Sample inputs contain absolute paths (`/home/imronuke/ADPRES/...`), they do not run as-shipped on another machine without editing.
- No releases/tags found; version is a banner string ("ADPRES 1.2").

**Missing (relevant to a game sim)**

- No xenon / samarium / depletion, a core that never changes with burnup and never has a xenon pit. (reactor-sim already has a xenon pit; that is a place the game is *ahead* of ADPRES.)
- No decay heat, after scram, power goes to the delayed-neutron tail and then nothing. (reactor-sim already has decay heat; again ahead.)
- No pin-power reconstruction, no thermal margins, no fuel-failure or clad-temperature limit model, an RIA in ADPRES ends with "max fuel temp", not with a safety verdict.
- No spatial kinetics beyond the nodal flux itself; no transport; Cartesian only (no hexagonal geometry).
- No uncertainty / sensitivity capability; no restart files (KOMODO's HDF5 output addresses part of this).

## 7. What a game sim gets wrong that ADPRES gets right

This is the useful contrast, stated bluntly:

1. **Point kinetics assumes the flux shape never changes.** ADPRES solves the 3D flux. During a rod ejection the power *tilts*, the ejected assembly's neighbours spike while distant assemblies barely move. A lumped model with one reactivity number cannot produce a hot-spot; ADPRES produces assembly power maps as its primary output. The game does not need a 3D solver, but it should know its power shape is an *assumption*, and any "hottest assembly" number it shows is only as good as that assumption.
2. **Reactivity is not a scalar you type in; it is an adjoint-weighted integral.** Rod worth, boron worth, Doppler and moderator feedback all depend on *where* the flux is. ADPRES computes rod worth from the actual flux/adjoint overlap. A game that applies a single rod-worth curve regardless of power shape or burnup is making a bigger approximation than it usually admits.
3. **Delayed neutrons are distributed, not lumped.** Per-node precursor inventories mean the delayed source follows the power history *locally*. A 6-group point-kinetics model with one precursor inventory per group is a further lumping on top of that.
4. **Feedback acts on cross sections, not on reactivity directly.** In ADPRES, hotter fuel changes Σa/νΣf node by node, which changes the flux shape, which changes the power, which changes the temperature. A game that adds "Doppler = −$X per °C" as a reactivity term gets the sign and roughly the magnitude right and the dynamics approximately right, but it cannot get xenon- or burnup-dependent feedback coefficients, or the fact that Doppler feedback is strongest where the flux is highest.
5. **Partial rod insertion is a real numerical problem (rod cusping).** Volume-weighting rodded/unrodded XS in a partially inserted node over- or under-predicts worth. ADPRES has an explicit cusping option. Any game with axial rod steps should treat a half-inserted bank as a modelling choice, not an interpolation.
6. **Boron is a search, not a slider.** Critical boron is the concentration that makes k-eff = 1 *given* the current temperatures, burnup and rod positions. ADPRES iterates to it. A game that treats boron as "−Y pcm per ppm" with a fixed coefficient is fine at one state and wrong everywhere else.
7. **Time steps are a stability choice, not a frame rate.** Fully implicit θ = 1 with two step sizes (fine during the transient, coarse after) is how you survive a prompt-critical excursion without numerical garbage. A game stepping kinetics at a fixed render dt during a fast transient is doing the opposite of this.
8. **A non-converged answer must be visible.** ADPRES prints iteration errors and warns on non-convergence. A game that silently integrates with a too-large step produces confident-looking wrong numbers.

## 8. Where the rigor/playability line should sit

Do not implement SANM, CMFD, or multi-group diffusion in the game. That is the wrong trade: ~10k lines of Fortran to replace a point-kinetics model that runs in microseconds and is *correct* for a spatially uniform core. The line:

- **Keep point kinetics** (already have 6-group + xenon + decay heat, that stack is already *more* complete than ADPRES on the transient-inventory side).
- **Buy spatial realism cheaply, offline:** precompute assembly power shapes / rod-worth curves / boron-worth curves from a reference nodal calculation (or from published benchmark data, e.g. the NEACRP/MOX cases in `smpl/`) and ship them as tables the game interpolates, exactly ADPRES's `%XTAB` philosophy, applied at the game level. The game then has credible *shape* without a solver.
- **Steal the feedback structure, not the solver:** fuel-temp → reactivity, moderator-temp/density → reactivity, boron → reactivity, rod position → reactivity, each as a *state-dependent* coefficient (function of burnup/power, even if coarsely), not a constant.
- **Steal the verification discipline, not the code:** golden cases with numeric baselines and tolerances (KOMODO's `test.sh` pattern), input echo, visible convergence/consistency checks, and a hard refusal to start a transient from an inconsistent state.
- **Do not steal:** the 9-point steam table, the fixed gap conductance, the νΣf rescaling fudge, the global-state architecture, the `STOP`-on-error handling, or the "CI runs it and hopes" test style.

## 9. What reactor-sim should steal, concrete, phased

Ordered by value per effort. All are consistent with the existing C#/.NET 8 core + Unity 6 front end and the Phase 1–8 plan; none require a solver.

1. **Two-step RIA time stepping (Phase: transient/fault scenarios).** Adopt ADPRES's `%EJCT` pattern: fine dt during the rod motion and power excursion, coarse dt after (e.g. 5 ms → 50 ms in the ADPRES sample). Expose the step schedule in scenario definitions. This is a one-day change that removes a whole class of numerical artefacts in fast transients.
2. **Rod-cusping-aware partial insertion (Phase: rod/control modelling).** When a bank is partway through an axial node, do not linearly interpolate rodded/unrodded worth. Use a cusping correction or a precomputed worth-vs-position curve with the correct S-shape. ADPRES's 2019 cusping commit is the pointer that this matters even to solver authors.
3. **State-dependent feedback coefficients (Phase: TH/kinetics coupling).** Replace any constant Doppler/moderator/boron coefficients with small tables over (power, burnup, boron), the `%XTAB` idea at game scale. Even a 3-point table per coefficient, calibrated against published PWR values, is a large credibility step over constants, and it makes the "critical boron search" (item 5) meaningful.
4. **Per-region precursor inventory (Phase: kinetics fidelity).** The game already has 6-group kinetics; ADPRES's per-node `c0` suggests the next refinement is *not* more groups but *spatial* distribution: split precursors into 2–4 radial/axial regions tracking local power history. Cheap (a few arrays), and it makes local power tilts after a rod move behave correctly instead of relaxing uniformly.
5. **Critical-boron search as a calibration/scenario tool (Phase: scenario setup / commissioning).** Implement ADPRES's secant search (mod_th.f90 `cbsearch`, ~90 lines): iterate boron until computed reactivity = 0 within a tolerance, with hard guards (0–3000 ppm, max 20 iterations, fail loudly). Use it at scenario initialisation to guarantee every scenario *starts* exactly critical, this kills the "why does my core drift at t=0" class of bug, and it is directly demo-able ("find critical boron" as a gameplay/engineering action).
6. **Adjoint-weighted rod worth (Phase: calibration data).** Do not take rod-worth curves from a textbook table at face value. The ADPRES lesson: worth = overlap of rod XS change with flux and adjoint. For the game's data tables, weight any published worth curve by the game's own assembly power shape, or at minimum document the assumed flux shape next to the number. One paragraph of provenance per table.
7. **Golden-case regression harness with numeric tolerances (Phase: now / ongoing).** Copy KOMODO's `test.sh`, not ADPRES's `.travis.yml`: a script that runs the existing golden replay + a set of scripted scenarios and asserts numbers (peak power, time-to-peak, final reactivity, max fuel temp) against baselines with tolerances. ADPRES proves that "it runs" is not a test; KOMODO proves the fix is ~15 KB of shell. The game's existing golden replay is the fixture; the missing piece is the *assertions*.
8. **Visible consistency diagnostics (Phase: UI/dev tools).** ADPRES prints k-eff, iteration errors and CPU breakdown on every run. Equivalent for the game: a dev overlay / log line per scenario start showing initial reactivity (must be ~0), feedback-coefficient set in use, time-step schedule, and any clamped/extrapolated table lookup. Fail loudly (refuse to start) on an inconsistent initial state instead of integrating through it, the `KNE1` lesson minus the fudge.
9. **Fuel-temperature model: keep IAPWS-IF97, add the lumped-temperature convention explicitly (Phase: TH).** ADPRES's `ftem = 0.3·T_center + 0.7·T_surface` is the standard effective-fuel-temperature weighting for Doppler feedback. If the game's TH already computes a radial fuel profile, expose which temperature drives Doppler and document the weighting; if it uses one fuel temperature, say so. Do not copy ADPRES's fixed 1e4 W/m²K gap conductance or its 9-point steam table, the game's IF97 + proper gap model is already better.
10. **Do not build:** a nodal solver, CMFD, BiCGSTAB, ADFs, or multi-group diffusion in-game. If spatial flux ever becomes a real requirement, the correct move is an *offline* reference calculation (KOMODO is free, MIT, and runs IAEA3D in 0.2 s) whose shapes are baked into the game's tables, not a runtime solver.

## 10. Sources actually read

- `README.md`, `docs/index.md`, `docs/method.md`, `docs/quick-guides.md`, `docs/install.md`, `docs/card-desc.md` and all card docs (`%MODE %GEOM %XSEC %XTAB %THER %THET %EJCT %CBCS %BCON %FTEM %MTEM %CDEN %CROD %ADF %ESRC %EXTR %ITER %KERN %PRNT %CASE`), imronuke/ADPRES, master at 0f45b6e.
- All of `src/*.f90` (9,939 lines): main dispatch, `mod_data` globals, `mod_cmfd` (BiCGSTAB, extrapolation), `mod_nodal` (SANM sinh/cosh coefficients, transverse leakages), `mod_th` (`th_iter`, `th_upd` pin conduction, `cbsearch` secant, 9-point steam table), `mod_trans` (`rod_eject`, `rod_eject_th`, `KNE1`, `reactivity`, `calc_beta`, `uPden`), `mod_xsec` (`XS_updt`, `XStab_updt`, `check_xs`), `mod_control`, `mod_io` (input echo, `.out` file).
- `smpl/` tree: static (IAEA2D/3D, BIBLIS, DVP, KOEBERG, NEACRP A1–C2, MOX Parts 1–3, FDM/PNM, adjoint, fixed-source), transient (LMW, NEACRP A1t–C2t, MOX Part 4), `smpl/xsec/` (HELIOS + Serpent branch tables).
- `.travis.yml` (12 run-only cases, no assertions); commit history via API (507 commits, 2017-04-11 → 2021-01-09; last two: "ADPRES is obsolete", "ADPRES is deprecated").
- KOMODO for the successor comparison: README, `src/` listing (11,917 lines; new `mod_gpu.f90`, `mod_hdf5.f90`, CMake, `test.sh` with numeric baselines), recent commits to 2026-11-18, 85 stars / 20 forks.
- Imron (2019) abstract via IAEA INIS (inis.iaea.org/records/pchen-yp729); Alnaqbi et al. (2022) APR1400/KOMODO paper via KoreaScience (NET 54, 764–769).

**Not read:** the paywalled 2019 paper full text (only the abstract); the SANM primary sources (Zimin & Ninokata 1998) beyond what ADPRES's method page cites; the `get_ABEFGH` coefficient derivation in full. No numbers above depend on those.
