# HalfGripWholeGrip

A Windows HalfGripTools source mod: one physical mouse per cockpit hand. The first prototype supports **vanilla steering wheel and standard H-pattern shifter** interaction. It uses Windows Raw Input inside the game; no driver, remapper, vJoy, or helper executable.

**Status: compile checked; Windows input capture and simultaneous gameplay are not yet validated.** Diagnostic mode is on by default. This is a prototype for testing, not a claim that the first playable milestone has passed.

## Install and assign

1. Use the 64-bit Windows game **0.419**, BepInEx **5.4.23.5**, and HalfGripTools **1.0.6**. Other game versions are refused at runtime. These are reference/compile targets, not tested Windows runtime versions.
2. Copy this repository's `mod.json` and `src/` into `BepInEx\HalfGripMods\HalfGripWholeGrip\`. Do not copy `work/`, reference assemblies, tests, or build output into the game.
3. Navigate and click through the main menu with your usual mouse. The mod passively records which physical device supplied the latest primary-button click. It leaves menu controls alone. With no saved right-hand assignment, that observed menu mouse becomes the **right hand** when cockpit control starts. It is inferred from usage, not from device enumeration or a Windows primary-device flag.
4. In the cockpit, press **F8** and click the second mouse to assign the **left hand**. If no right-hand mouse was inferred, F8 asks for right first, then left. **Shift+F8** explicitly reassigns both. Release buttons after assignment.
5. In diagnostic mode, verify the blue `R` and orange `L` cursors and independent `buttons`, `down`, and `up` indicators. Test fast clicks and simultaneous movement/clicks before enabling interaction.
6. Press **F9** to switch to wheel/shifter interaction. Either hand can grab either supported object; only one hand may own each object. Press **F7** to disable and restore ordinary input. F7 can re-enable it.

Each mouse's left button grabs/releases and its right button sends the vanilla secondary action, such as steering lock. `Hand(0)` and `Hand(1)` remain primary/secondary **buttons**, not right/left hands.

Assignments and per-hand sensitivity are saved through HalfGripTools in `BepInEx\config\halfgrip.wholegrip.cfg`. The assignment stores Raw Input device paths, never transient handles. Reconnect to the same port to retain the path where Windows permits; use F8 or Shift+F8 if the path changes. Two HID collections from one physical device cannot be reliably recognized as two physical mice: assign real distinct devices.

The mod records menu input only until a right-hand assignment has been saved. To choose a different right mouse later, use Shift+F8. Menu usage never silently replaces an existing saved assignment. A second mouse bumped during menu navigation does not take over the right hand unless it supplies the latest primary click before assignment.

## Scope and lifecycle

The existing grabber becomes the right hand. A bare second `Grabber` and renderer-only hand visuals provide the left hand; the player, cameras, cockpit, physics objects, colliders, and event listeners are not cloned. The controller runs each grabber with its own ordered input stream and retains native hover raycasts, movement planes, grab/release callbacks, held-look correction, cursor pullback, and pose updates.

Outside a scoped hand update, gameplay `GameInput.Hand*` reads are suppressed while the controller owns gameplay. The original grabber cannot also consume Unity's aggregated mouse deltas/buttons. Menu `GetMouseButton*`, `GetKey*`, cursor position, and Unity's original Windows message processing remain available. The mod does not globally substitute an “either mouse held” value.

Focus loss, pause, gameplay input mute, scene callbacks, assignment, mode switch, queue overflow, device loss, disable, and unload cancel the relevant holds/input. Button input must rearm after suspension. Device-change notifications are used if the current registration supplies them; otherwise device enumeration polls once per second, so disconnect cancellation can take up to that interval. Losing one device does not clear the other device's queue or hold. A receiver/read failure disables the prototype and returns to ordinary input.

HalfGripTools owns the Harmony patches. `Unload()` synchronously cancels holds, restores original visuals/settings, removes the window subclass, unregisters only a registration this mod created and still owns, and removes its scene objects before loader cleanup. Speedrun mode is never changed by this mod. If subclass removal fails, its callback remains rooted but inert until the window dies, avoiding a dangling native callback.

## Limitations

- **Windows game testing is still required.** The current machine runs Linux with reference assemblies only. Installed game version and Unity Raw Input registration could not be measured here. Runtime startup logs `Application.version`, Unity, loader, receiver window, and registration mode before attempting control.
- Guns, ammo handling, SlapPack, alternate shifter types, gamepad hand control, legacy cockpit components, and modded cockpit objects are outside this prototype. Grabs and secondary actions are restricted to the active scene's vanilla `NSteeringWheel.wheel` and `NShifter.gate`, with grab redirects rejected. Use F7 to return to ordinary interaction with other objects.
- Two hands on the **same** steering wheel are deferred. `Grabbable` has one grip point/held flag; sharing it needs a separate force/grip model. Either hand can steer, and the other can shift.
- Native systems such as guns, crosshairs, tutorials, and pause UI retain references to one grabber or discover one by type. This prototype does not claim broad multi-grabber compatibility. The pause patch explicitly cancels both hands.
- Only relative mouse devices are supported. Absolute-position packets stop the reader. Some virtual devices or remote-desktop environments cannot expose two physical mouse identities.
- The Raw Input receiver must be on Unity's current thread for safe `SetWindowSubclass` use. Existing page/exclusion registrations or a receiver on another thread are refused. If Unity changes its receiver, the mod disables instead of taking registration back. Live message-hook behavior remains untested.
- Hotkeys currently use F7/F8/F9 directly. They are not added to the game's rebind list and may conflict with another mod's keys.

See [inspection and exact patches](docs/INSPECTION.md) and [validation record and Windows acceptance procedure](docs/VALIDATION.md).

## Compile and check

The private HalfGripRefs repository and a HalfGripTools checkout are required; neither is included or modified. With .NET 8 installed:

```bash
HALFGRIP_REFS=/path/to/HalfGripRefs \
HALFGRIP_TOOLS=/path/to/HalfGripTools \
./tools/check.sh
```

This invokes HalfGripRefs' `tools/check-mod.sh` using an isolated copy of the loader under ignored `work/`, then runs console checks against the production input accumulator and Raw Input structure definitions. Compilation does not establish simultaneous cockpit interaction.

On Windows, use HalfGripTools' checker against your installed game:

```powershell
.\tools\check-mods.ps1 -ModsPath <parent-of-HalfGripWholeGrip> -Mod HalfGripWholeGrip -GameDir <installed-game>
```

Keep private game DLLs, decompiled code, and scene dumps out of this public repository. Only source, manifest, docs, and test/build scripts belong here.
