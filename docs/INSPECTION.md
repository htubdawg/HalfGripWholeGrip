# Inspection record

Inspected on 2026-11-15. Menu use and ordinary clicks assign hands automatically; there are no mod hotkeys or mode switches. Version 0.1.2 additionally binds native hand reads directly to the Grabber instance, accepts non-mouse packets on the shared receiver without a mouse-size check, and preserves input across cursor-capture changes. Only HalfGripWholeGrip was changed. Other repositories were read-only; the compile checker builds an isolated loader copy under this repository's ignored `work/` folder.

## Evidence and versions

- HalfGripRefs checkout: `aa6d5013ab6131fe0cc635029baa60af64b21390`. Its README labels the references **0.419**, Unity **6000.3.22f1**, BepInEx **5.4.23.5**. The Tokyo 0.419 scene dump was inspected to confirm the wheel, shifter, grabber, camera, and indicator wiring.
- Inspected `game/HalfGrip_Data/Managed/Assembly-CSharp.dll` SHA-256: `09fa41dc14e93ee76374070c3a4b9b836f193f8aac59c72be413794b696f89a9`.
- HalfGripTools: `d2c7a786b384566562cf6fcfbdb57eb36b16e351`, version **1.0.6**. Read `docs/WRITING_MODS.md`, the loader/API, compiler, unload implementation, and example manifest.
- Read HalfGripMods' current README and `mods/InfinitePlayground/src/Mod.cs` as a gameplay/lifecycle example. No changes there.
- Read jMods SlapPack's `AdjustableMirror.cs`, `WindowCrank.cs`, and `WiperStalk.cs`. No changes there or in its release-synchronized `mods/` directory.
- Decompiled the private game assembly with the preinstalled ILSpy CLI into ignored local scratch. No game code or reference binaries are shipped.

**Installed Windows build is not available in this environment.** The version above describes the inspected reference set, not a verified local game installation. The runtime logs the installed game/Unity/loader versions and refuses game versions other than 0.419. Unity's live registration also remains unverified until Windows execution; the raw reader queries it before choosing how to attach.

## Interaction findings

| Path | Finding and implementation consequence |
| --- | --- |
| `Grabber.Start()` | Sets camera fallback, cursor center, and cursor lock. The second component is disabled and explicitly initialized, so it never runs an independent native Start or locks/unlocks the cursor itself. |
| `Grabber.Update()` | Calls held-look begin, cursor update, ray construction, held-look end, release, held/hover update, primary grab/click, secondary dispatch. Held/hover/cursor and transition state are instance fields/properties, so two coordinators can reuse this path. |
| `Grabber.UpdateCursor()` | Reads aggregated `Input.mousePositionDelta` or `Input.mousePosition`, and calls `GamepadHand.MoveCursor`. Replaced for owned grabbers with device-relative deltas, game sensitivity, plane rebasing, and screen limits. No aggregated mouse or gamepad movement reaches these cursors. |
| `Grabber.UpdateHover(Ray)` | Iterates shared `InteriorCollider.Active`, testing collider movement planes, tolerance, sorting layer and distance. The candidate result and hover plane point are per grabber. Shared collider discovery can be reused. |
| `Grabber.TryInteract(bool)` | Resolves a grabbable, may redirect it, computes grip/plane points, calls `Grab`, and sets Held. The prototype gates this method before it runs: only allowed vanilla wheel/gate targets, no redirects, no already-held or foreign-owned targets. Clickables are deferred. |
| `Grabber.DispatchRightClick()` | Sends the secondary action to held/hovered grabbables or Clickables. The same ownership/allowlist gate prevents the other hand from operating an owned object. |
| `Grabber.LateUpdate()` / `UpdateIndicators()` | Pull the virtual cursor toward the grip and update poses, held roll, force arrows and debug ghosts. State belongs to the instance; the second hand gets separate renderer-only visuals. |
| `Grabbable` | Owns a single `IsGrabbed` flag, grabbed point, constrained hand position, blockRelease flag, movement plane, and physics state per object. `Grab`, `UpdateGrab`, `Release`, `OnGrab`, `OnHandMoved`, `OnRelease`, and UnityEvents are retained. Two owners cannot safely share one object's native state. |
| `NSteeringWheel` / `HingedObject` | The wheel component consumes the hinged object's value/physics and uses its callbacks for wheel grabs and secondary steering lock/recenter. No per-frame global hand-button read in `NSteeringWheel`. Suitable for single-owner reuse. |
| `NShifter` / `DoubleLinearObject` / `ShifterBase` | The H-gate's movement/value feeds gear latching, spring targets, visual stick and gear callbacks. No global hand-button reads in the inspected NShifter path. Suitable for an independent owner. |
| Legacy `SteeringWheel`, `ShifterAssembly`, `HandBrakeAssembly` | Older systems coexist in the assembly. SteeringWheel and HandBrakeAssembly use global HandUp/HandDown. They are not the selected 0.419 vanilla interaction path and are outside this prototype. |
| `GameInput.Hand(int)` and edge accessors | Map to primary/secondary mouse-key codes, also merging pad input. The integer is a button index. Native grabber call sites are rewritten to read their explicit grabber instance; callback/global accessor reads remain scoped to the current hand during a driven update; outside that scope gameplay sees false while captured. Normal menus retain their original button/key APIs. |
| `Grabber.BeginHeldLook()` / `EndHeldLook(bool)` | Temporarily remove held-camera yaw for ray construction and then restore camera rotation. Invoking one hand at a time preserves this pair. Keyboard look behavior remains native; no combined mouse motion drives it. |
| `CameraSwitch` / `OutsideCamera` | Cockpit look uses Q/E and shoulder-look keys; outside camera has aggregate mouse axis input. No attempt is made to add dual mouse camera control. Capture requires the interior grabber and its view camera to be active. |
| `Gun` / `NAutomaticGun` / `Crosshair` | Gun caches a discovered grabber, uses global HandUp(0)/HandDown(1), and transitions shared aim/frozen/body/crosshair state. Automatic guns add continuous Hand(1) checks; crosshair reads a particular grabber's cursor. Guns need explicit owner-aware aim/crosshair/input adaptation and are deferred. |
| `GamepadHand`, `PauseController`, tutorial/recorder helpers, `NShell`, ammo-lift paths | Retain static or discovered single-grabber references. No general multi-grabber compatibility is claimed. Pause is explicitly intercepted to release both hands. |

## SlapPack assumptions left for later

- AdjustableMirror and WindowCrank use a stored grabber/camera and cancel holds with `!GameInput.Hand(0)` from their own Update. A new hand component alone cannot fix their ownership.
- WindowCrank additionally patches held-look and indicators; preserving those method calls is useful but does not solve its stored references or input lifetime.
- WiperStalk stores its initialized grabber and reads `Hand(RotatingKnob ? 1 : 0)`. Its secondary-button knob grab also needs the initiating hand and correct button ownership.
- No “either button is held” fallback is provided. It would allow one device to sustain another device's hold.

## Exact Harmony patches

All targets are in the inspected Assembly-CSharp. Installation is transactional: any failure unpatches this mod and aborts takeover.

| Method | Patch | Behavior |
| --- | --- | --- |
| `Grabber.Update()` | Prefix + transpiler | Suppress automatic updates; drive each hand explicitly. Replace all seven `Hand`/`HandDown`/`HandUp` call sites with direct per-grabber reads, passing `this`. Refuse installation if any button-read family is missing. |
| `Grabber.LateUpdate()` | Prefix | Suppress automatic indicator updates; allow explicitly driven updates. |
| `Grabber.UpdateCursor()` | Prefix | Supply the scoped device's movement instead of aggregate input. |
| `Grabber.TryInteract(bool allowClick)` | Prefix | Gate supported targets and enforce one owner per object. |
| `Grabber.DispatchRightClick()` | Prefix | Gate secondary actions by target and owner. |
| `GameInput.Hand(int button)` | Prefix | Read the current hand's held bits during capture; no gameplay hand input outside that scope. |
| `GameInput.HandDown(int button)` | Prefix | Read the current ordered step's press bits. |
| `GameInput.HandUp(int button)` | Prefix | Read the current ordered step's release bits. |
| `PauseController.SetPaused(bool paused)` | Prefix | Synchronously suspend both hands before native pause handling. |
| `GameInput.Mute(UnityEngine.Object owner, bool muted)` | Postfix | Suspend immediately when gameplay input becomes muted. |

No patches to `Grabbable` physics, callbacks, `NSteeringWheel`, `NShifter`, guns, the game's version, saves, speedrun setting, or menu input methods.

The adapter reflects `Grabber.rawHandPlanePoint`, backing fields for Held/Hovered, `heldLastFrame`, and `suppressSweep` for cursor rebasing and cancellation. Those field names and the patched methods are checked before installation. Runtime method-detour and callback behavior still need Windows validation.

## Raw Input contract

- Enumerate with `GetRawInputDeviceList`, inspect names and mouse capabilities with `GetRawInputDeviceInfo`, and associate every packet's `RAWINPUTHEADER.hDevice` with its device path.
- Query `GetRegisteredRawInputDevices` before hooking. Reuse the existing mouse receiver/flags when supported; if absent, register foreground mouse input with DEVNOTIFY on Unity's own window.
- Use `SetWindowSubclass` on the receiver's own thread. Always call `DefSubclassProc`, including after errors. Never request NOLEGACY, INPUTSINK, exclusive capture, or a new helper window.
- Read with `GetRawInputData`; check header type before enforcing the mouse payload size. Keyboard/HID messages reaching the shared Unity receiver are ignored, not treated as broken mouse packets. Cursor-capture changes do not clear input; actual focus loss still does. Preserve each device's event order. Coalesce consecutive movement while retaining every button boundary. One snapshot is drained per Unity frame and then consumed by both hands. Windows relative Y is converted to Unity screen Y.
- Store paths rather than handles. Drop a disconnected device's queued/history state without clearing the other device. A bounded-queue overflow invalidates all input and releases both holds instead of silently losing button-up events.
- Refuse cross-thread receivers and page/exclusion registrations. If the process changes receivers, disable rather than re-register over it. Remove only this subclass, and only a still-matching registration created by this mod.

Microsoft references: [Raw Input overview](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input), [registration constraints](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerrawinputdevices), [subclass thread restrictions](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass).
