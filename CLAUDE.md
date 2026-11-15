# HalfGripWholeGrip

A Half Grip (Steam app 4163270, Unity 6000.3, Windows) mod: **two physical mice drive two cockpit hands**. The right hand is the
game's own; the left is a second one the mod builds. Current release: see `WholeGrip/mod.json`. Started by Codex (0.1.0-0.2.0,
history on `codex/wholegrip-prototype`), rewritten in 0.3.0.

## Always be ready for a context clear

This file is the next session's only memory. After every step that changes the state of things it must already say what is
needed to carry on: update **Status** in the same commit as the change; commit and push each finished step; write down what was
learned the hard way (*Game and Windows facts*, *Owner's wishes*). A fresh session starts by reading this file,
`git log --oneline -10`, `git status`, building HalfGripTools (see *Checking changes*) and reading the files named in Status.

## How the mod runs

- A **source mod** for [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools) (BepInEx 5): the loader compiles
  `WholeGrip/src/*.cs` at game start (**C# 10**, netstandard2.1, no `unsafe`). What ships is the `WholeGrip/` folder (`mod.json`,
  `icon.png`, `src/`), installed to `<game>\BepInEx\HalfGripMods\WholeGrip\`. Loader API: HalfGripTools' `docs/WRITING_MODS.md`.
  Game DLLs and scene dumps: private `htubdawg/HalfGripRefs`. House rules for all the owner's mods: `htubdawg/jMods` CLAUDE.md.
- Windows 64-bit only (P/Invoke to user32); on anything else Load logs a warning and does nothing.

## Layout of src/

| File | Role |
|---|---|
| `Mod.cs` | Platform check, `Patches.Missing()` check, the persistent `Hands` object, patches (all or none), scene and unload hooks |
| `RawMice.cs` | Input thread with a message-only window. `Take()` registers it for mouse raw input (`RIDEV_INPUTSINK|DEVNOTIFY`), keeping Unity's registration; `GiveBack()` (main thread) restores it. Reads every `WM_INPUT` into a locked list; `Drain()` once a frame. A 250 ms timer re-takes if Unity re-registers (gives up after 8). Device list/paths, `GetAsyncKeyState`, `SM_SWAPBUTTON` |
| `HandInput.cs` | Pure (no Unity; `tests/` runs it as shipped). One hand's packets → one frame of legacy-style button state: transitions queued per button, **one per frame** (a click between frames = press, then release next frame); lost releases put back; queue capped at 6 (whole clicks dropped from the middle); **physical-state safety net only for guessed holds**: held since `Resync` with no packet of this hand for that button since + nothing queued + Windows says up → release. Never for holds seen in packets (see facts) |
| `Assignment.cs` | Pure. Device path → side. No right hand: any mouse but the left moves it, first click takes it. Then the first other mouse to click takes the left. Unplugged hand = free for the next click |
| `Hands.cs` | The MonoBehaviour. Decides when to drive (`Wanted()`), takes/gives back the mice, routes packets (`EnsureFrame`, lazily once a frame from whichever entry comes first: no execution-order dependence), builds the left hand, runs its `Update`/`LateUpdate` by reflection, `MoveCursor` (the game's `UpdateCursor` with per-hand deltas), `MayUse` (ownership, left-hand allowlist), hints via `OnGUI` |
| `Patches.cs` | Harmony, installed transactionally: `Grabber.Update` prefix+finalizer (sets `Hands.Scoped`), `Grabber.UpdateCursor` prefix, `TryInteract`/`DispatchRightClick` prefixes (MayUse), `GameInput.GetKey/GetKeyDown/GetKeyUp(KeyCode)` and `GetMouseButton/Down/Up(int)` prefixes answering buttons 0/1 from the hand's `HandInput`. Everything passes through when not driving |

## Rules that matter

- **Only drive while the cockpit is live** (`Hands.Wanted`): focused, right Grabber enabled with `lockHardwareCursor`, interior
  camera on, not `InteriorMath.Paused`, not `GameInput.Muted`, not `GameManager.raceOver`, timeScale > 0, at least 2 mice. Give
  the mice back the same frame any of those stops. The left hand keeps its hold through pause/mute (as the game's hand does) and
  lets go on anything else.
- **Never patch `GameInput.Hand/HandDown/HandUp`**: 13-byte forwarders Mono can inline (checked by `tests/`); patch `GetKey*`.
- The left Grabber lives on an **inactive** GameObject so `FindFirstObjectByType<Grabber>()` (Gun, PauseController, AmmoLiftZone,
  NShell, ClapperFrame, tutorials) keeps finding the right one. Never make it active or enabled-and-run by Unity.
- The left hand may hold only `NSteeringWheel.wheel` and `NShifter.gate` (and click Clickables). Parts that read
  `GameInput.Hand(0)` in their own Update (guns, SlapPack's crank/stalks/mirror) would answer to the right mouse.
- No hotkeys (0.1.x's conflicted with other mods' F-keys), no setup screens. Hints only while a hand waits, or for 4-15 s on news.
- Logging: warnings/errors and assignments always; routine info only with `Debug.VerboseLog`.
- C# 10, tabs, `///` summaries that say why, try/catch round native callbacks (an exception must never reach Windows).
- Bump `version` in `mod.json` for every released change, README header in step; `gameVersion` = HalfGripRefs' game,
  `tools` = HalfGripTools' `Plugin.Version`. Work on a branch, open the PR; don't push to `main`.

## Game and Windows facts

- `Grabber.Update` (0.419): look-back keys, `BeginHeldLook`, `UpdateCursor` (reads `Input.mousePositionDelta` × cursorSensitivity ×
  `HandSensitivity` × height/1080, then `GamepadHand.MoveCursor`, clamp unless holding), release on `HandUp(0)`, sweep-grab on
  `Hand(0)` unless `suppressSweep`, grab/click on `HandDown(0)` via `TryInteract`, `DispatchRightClick` on `HandDown(1)`.
  `LateUpdate` = cursor pull-back + `UpdateIndicators` (poses, roll, force arrow). State is all per instance.
- `GameInput` wraps legacy `Input` (mouse buttons = `KeyCode.Mouse0 + n`, i.e. 323+n); the Input System is loaded but used for the
  gamepad. `PauseController` calls `grabber.SetHandVisible(!paused)` on the hand it found.
- SlapPack's `Grabber` patches (all scoped to its own parts, pass-through otherwise): `WiperKnobGrab` transpiles `Grabber.Update`'s
  `HandUp` calls into a helper that reads button 1 while the wiper knob (grabbed with the right button) is held, else calls
  `GameInput.HandUp` → our `GetKeyUp` patch; `WindowCrankLook` prefix on `BeginHeldLook` (cranks only); `WindowCrankIndicators`
  postfix on `UpdateIndicators` (its parts only); `MirrorHandDepth` postfix on `LateUpdate`, but `KeepHandInFront` acts only on the
  Grabber the mirror stored (the right hand), so the left hand may draw behind the mirror (cosmetic, SlapPack-side fix). All of these
  need the game's real `Update`/`LateUpdate` to run: never replace them (0.2.0 did, which skipped every other mod's Update patch).
- **Raw input is one window per process per device type.** Unity registers mice (page 1, usage 2, flags 0x0, its own window, on its
  main thread: seen in the owner's 0.2.0 log) and drains packets with `GetRawInputBuffer`, which bypasses `WH_GETMESSAGE` hooks.
  0.2.0 hooked the queue and logged ~100-135 packets/s with both mice moving (two mice give 250-2000/s): most were lost,
  which inverted button states (stuck holds, presses read as releases). Hence the take-over while driving.
- **`GetAsyncKeyState` is one state for all mice, not a count**: any mouse's button-up clears it while another mouse still
  holds. 0.3.0 used it as a safety net for every hold, so releasing one mouse released both hands (owner's report). It says
  nothing about a hand whose own packets showed the button; 0.3.1 uses it only for holds guessed at `Resync`.
- Raw mouse button flags and `GetAsyncKeyState(VK_LBUTTON)` are **physical**; the game's Mouse0 is the logical primary. With
  `SM_SWAPBUTTON` on, `Hands.EnsureFrame` swaps left/right before use.
- `hDevice` is 0 for injected input (SendInput, remote desktop): ignored. Absolute packets (tablets, touch): buttons only.
- Windows often lists extra mouse devices (receivers' second interface, `RDP_MOU`), so "2 mice" can be true with one physical
  mouse. Harmless: the right hand is then driven by raw input instead of Unity's, the left never gets a click.

## Owner's wishes

- Author credit: `Claude - Codex - Oretsej` (owner, 2026-11-15).
- Two physical mice, two independent cockpit hands; wheel in one, shifter in the other. No hotkeys or modes (Codex 0.1.1 removed
  them after they clashed with other mods' keys).
- 0.2.0 report (log + words, 2026-11-15): "sort of works but its very jittery and misses a lot of inputs. Click releases almost
  never work and moving both at the same time exacerbates the issues." Asked for it to work properly and to meet jMods' standards
  (own repo layout, CLAUDE.md, versions, checks, PR).

## Checking changes

Repos side by side: `HalfGripRefs` (`/home/user/HalfGripRefs`), HalfGripTools (clone `4l3x4nd3r0/HalfGripTools`, build it once).

- Compile and tests (before every push):
  ```
  dotnet build <HalfGripTools>/HalfGripTools/HalfGripTools.csproj -c Release -p:GameDir=<halfgriprefs>/game
  tools/check.sh <halfgriprefs>/game <HalfGripTools>/dist/BepInEx/plugins/HalfGripTools/HalfGripTools.dll
  ```
  Compiles `WholeGrip/src` with warnings as errors, then `tests/`: `HandInput` scenarios (clicks between frames, lost releases,
  safety net, click spam, two hands at 16 packets/frame for 500 frames), `Assignment` scenarios, and a metadata scan of
  `Assembly-CSharp.dll` (fields/methods named by string, `Hand*` IL sizes, what `Grabber.Update`/`UpdateCursor` call).
- Not checkable here: Win32 behaviour, Harmony on Mono, Unity rendering. Only the owner's Windows test shows those.
- Game code: `DOTNET_ROLL_FORWARD=Major ilspycmd -p -o <dir> <refs>/game/HalfGrip_Data/Managed/Assembly-CSharp.dll` (ilspycmd
  8.2.0.7535 via `dotnet tool install --tool-path`).
- In game (the owner tests): `Debug.VerboseLog=true`, read `BepInEx\LogOutput.log` for `Driving both hands`, `Right hand:`,
  `Left hand:`, `Packets so far:` (should climb by hundreds a second while both mice move), `Mice given back`, and any warning.

## Status (keep current)

- **0.1.0-0.2.0** (Codex): queue hook on Unity's window, then a re-implemented Grabber.Update. Owner's 0.2.0 test: jittery, lost
  inputs, releases almost never work, worse with both moving. Causes found: packet loss (see facts), button state built from
  transitions with no recovery, `Hand*` patches in 0.1.x likely inlined away, guns and every other part off-limits even to the
  right hand.
- **0.3.0** (branch `claude/jolly-ritchie-e6wh7t`, on top of Codex's history): rewrite. Mouse take-over on its own thread while
  driving; the game's own Grabber Update/LateUpdate for both hands with per-hand buttons (GetKey* patches) and movement
  (UpdateCursor patch); one transition per frame; physical-state safety net; right hand unrestricted, left hand wheel/shifter/
  Clickables; inactive left Grabber; assignment by first clicks in the cockpit (0.2.0's "menu mouse is the right hand" is gone:
  menus don't take the mice). Repo moved to the jMods layout (`WholeGrip/` folder, icon, README, this file, `tools/check.sh`,
  `tests/`); Codex's `docs/` folded in here. Checked: `tools/check.sh` (0 warnings, all tests pass) against HalfGripRefs 0.419
  and HalfGripTools 1.0.6 built from source. **Untested in game: all of it.**
- **0.3.1** (branch `claude/vibrant-mendel-fshbj5`, on top of 0.3.0's branch): owner's 0.3.0 test: "works much better", but
  holding both buttons and releasing one released both. Cause: the `GetAsyncKeyState` safety net (shared state, see facts).
  Fix: net only for holds guessed at `Resync`. Tests: the owner's case (both hold, one lets go), seen press ignores Windows' up;
  a mutation (net for every hold) fails 4 checks. Author set to `Claude - Codex - Oretsej` (owner's wish). `tools/check.sh` passes.
- **Owner to test** (with `Debug.VerboseLog=true`): both hands holding wheel+shifter while moving both mice fast; rapid clicks;
  releasing either while the other moves; pause/unpause while holding; alt-tab; outside camera; a scene change; a gun in the
  right hand while the left steers; SlapPack parts with the right hand. Send the log.
- **Open questions:** does Unity re-register mice during a race (log `keeps taking the mice back`)? Is Unity's mouse back in the
  menus after every give-back (it should be: same window and flags restored)? Should the left hand be allowed more parts?
- **PR:** [htubdawg/HalfGripWholeGrip#3](https://github.com/htubdawg/HalfGripWholeGrip/pull/3) (`claude/vibrant-mendel-fshbj5` → `main`,
  0.3.1, contains Codex's and 0.3.0's history), open. #1 (Codex) and #2 (0.3.0) closed as superseded at the owner's request; their
  branches are left on the remote. Before pushing more, check #3 is still open; if merged, restart the branch from `main`.
- **Told the owner, awaiting answers:** asked whether to watch #3. Owner asked about SlapPack's Grabber patches;
  answered (see *Game and Windows facts*): harmless to other mods, the transpiler is justified, only 0.2.0's Update replacement broke them.
- **Session notes:** HalfGripTools built from source this session (clone to `/home/user/htubdawg/HalfGripTools`); jMods is
  read-only from here (public clone only), so its README/table can't be edited from this repo's sessions without push access.
- **Next step:** owner tests 0.3.1 (release one of two held buttons; the other must keep holding) and merges, sends `LogOutput.log` (VerboseLog on); fix from the log. After a release,
  list Whole Grip in jMods (README section + CLAUDE.md table + sync workflow entry).
