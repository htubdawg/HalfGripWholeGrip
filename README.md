# HalfGripWholeGrip

**0.1.2:** fixes input resets on keyboard/HID packets and cursor-capture changes, and binds grabber button reads directly to each hand. No new controls.

Two physical mice, two cockpit hands. Windows HalfGripTools prototype for game **0.419**, loader **1.0.6**.

## Test it

1. Copy this folder into `BepInEx\HalfGripMods\HalfGripWholeGrip\` so it contains `mod.json` and `src/`. Replace the previous version, then restart the game.
2. Use your usual mouse in the main menu. That mouse becomes the **blue right hand** when you enter the cockpit.
3. Click the other mouse in the cockpit. It becomes the **orange left hand**. Grab the wheel with one and the standard H-pattern shifter with the other.

**There are no mod hotkeys, setup screens, diagnostic modes, or enable switches.** Interaction starts automatically. Old `Enabled` and `DiagnosticOnly` config values are ignored; no config cleanup is needed. Both mice use their normal left button to grab and right button for secondary actions.

A single status line shows each hand receiving input or holding an object. If input cannot attach or interaction stops, it shows the error rather than silently disappearing. Input attachment retries automatically. To disable the mod, use the loader's normal mod manager.

Menu controls remain normal. Device paths are remembered. Menu use selects the right hand each session; a missing/disconnected hand can be assigned by clicking a replacement mouse without interrupting the other hand. Per-hand sensitivity uses the game's hand sensitivity with optional multipliers in `BepInEx\config\halfgrip.wholegrip.cfg`.

## Current scope

- Vanilla steering wheel and standard H-pattern shifter only. Either hand can operate either; each object has one owner.
- Guns, SlapPack, alternate shifters and two hands sharing the same wheel are deferred.
- Raw Input runs inside the game. No vJoy, remapper, driver, helper executable, or cloned cockpit.
- Pause, focus loss, device disconnect, scenes and loader/speedrun unload clear holds and restore controls. Disconnect detection may take up to one second when Unity's registration lacks device-change notifications.
- Relative mice and a same-thread Windows input receiver are required. Unsupported receiver layouts fail without replacing Unity's registration.

**Compile and input/assignment checks pass. Actual two-mouse Windows gameplay remains unverified in this Linux environment.** See [validation](docs/VALIDATION.md) and [game inspection / exact patches](docs/INSPECTION.md).

## Development checks

With .NET 8 and the private reference/loader checkouts:

```bash
HALFGRIP_REFS=/path/to/HalfGripRefs \
HALFGRIP_TOOLS=/path/to/HalfGripTools \
./tools/check.sh
```

The script invokes HalfGripRefs' compiler checker with an isolated loader copy, then checks the production input/assignment code, mixed-device packet decoding, native structure layouts, and the actual game grabber call sites. Other repositories are not modified. `work/`, private assemblies and decompiled game code are excluded from Git and installation.
