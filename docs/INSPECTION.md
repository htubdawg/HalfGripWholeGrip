# Inspection record

Inspected on 2026-11-15. Current implementation: **0.2.0**. Menu use and ordinary clicks assign hands automatically; there are no mod hotkeys or mode switches. Only HalfGripWholeGrip was changed. Other repositories are read-only; the checker builds an isolated loader copy under ignored `work/`.

The old approach replayed native `Grabber.Update` for input steps while intercepting shared button getters. It has been replaced: `HandDriver` schedules explicit per-hand actions, and `VanillaHandInteraction` uses the native geometry/callback helpers without ever executing native Update/UpdateCursor or reading aggregate mouse buttons. Raw Input is copied at thread-message removal rather than window dispatch. No per-packet unmanaged allocation, dual capture paths, instruction rewriting or GameInput.Hand patches remain.

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
| `Grabber.Update()` | Calls held-look begin, cursor update, ray construction, held-look end, release, held/hover update, primary grab/click, secondary dispatch. Held/hover/cursor and transition state are per instance, but the method reads shared button input. 0.2.0 suppresses this method for owned grabbers and calls only its geometry/callback helpers. |
| `Grabber.UpdateCursor()` | Reads aggregated `Input.mousePositionDelta` or `Input.mousePosition`, and calls `GamepadHand.MoveCursor`. Not invoked for owned grabbers; the controller applies device-relative deltas, game sensitivity, plane rebasing, and screen limits. No aggregated mouse or gamepad movement reaches these cursors. |
| `Grabber.UpdateHover(Ray)` | Iterates shared `InteriorCollider.Active`, testing collider movement planes, tolerance, sorting layer and distance. The candidate result and hover plane point are per grabber. Shared collider discovery can be reused. |
| `Grabber.TryInteract(bool)` | Resolves a grabbable, may redirect it, computes grip/plane points, calls `Grab`, and sets Held. The prototype gates this method before it runs: only allowed vanilla wheel/gate targets, no redirects, no already-held or foreign-owned targets. Clickables are deferred. |
| `Grabber.DispatchRightClick()` | Sends the secondary action to held/hovered grabbables or Clickables. The same ownership/allowlist gate prevents the other hand from operating an owned object. |
| `Grabber.LateUpdate()` / `UpdateIndicators()` | Pull the virtual cursor toward the grip and update poses, held roll, force arrows and debug ghosts. State belongs to the instance; the second hand gets separate renderer-only visuals. |
| `Grabbable` | Owns a single `IsGrabbed` flag, grabbed point, constrained hand position, blockRelease flag, movement plane, and physics state per object. `Grab`, `UpdateGrab`, `Release`, `OnGrab`, `OnHandMoved`, `OnRelease`, and UnityEvents are retained. Two owners cannot safely share one object's native state. |
| `NSteeringWheel` / `HingedObject` | The wheel component consumes the hinged object's value/physics and uses its callbacks for wheel grabs and secondary steering lock/recenter. No per-frame global hand-button read in `NSteeringWheel`. Suitable for single-owner reuse. |
| `NShifter` / `DoubleLinearObject` / `ShifterBase` | The H-gate's movement/value feeds gear latching, spring targets, visual stick and gear callbacks. No global hand-button reads in the inspected NShifter path. Suitable for an independent owner. |
| Legacy `SteeringWheel`, `ShifterAssembly`, `HandBrakeAssembly` | Older systems coexist in the assembly. SteeringWheel and HandBrakeAssembly use global HandUp/HandDown. They are not the selected 0.419 vanilla interaction path and are outside this prototype. |
| `GameInput.Hand(int)` and edge accessors | Map to primary/secondary mouse-key codes, also merging pad input. The integer is a button index. Not used or patched by 0.2.0. Each HandDriver consumes its own DeviceFrame directly. Ordinary menu/global input APIs remain native. |
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
| `Grabber.Update()` | Prefix | Suppress native automatic Update for both owned grabbers. Never bypassed or manually invoked. |
| `Grabber.LateUpdate()` | Prefix | Suppress automatic indicators; allow exactly one explicitly invoked indicator pass per hand/frame. |
| `PauseController.SetPaused(bool paused)` | Prefix | Synchronously suspend both hands before native pause handling. |
| `GameInput.Mute(UnityEngine.Object owner, bool muted)` | Postfix | Suspend immediately when gameplay input becomes muted. |

No patches to `Grabbable` physics, callbacks, `NSteeringWheel`, `NShifter`, guns, the game's version, saves, speedrun setting, or menu input methods.

Native helpers called without patching: `UpdateHover(Ray)`, `TryInteract(bool)`, `DispatchRightClick()`, `UpdateHeld(Ray)`, `BeginHeldLook()`, `EndHeldLook(bool)`, and `LateUpdate()`. The controller checks allowlist/ownership before invoking grab/secondary helpers. Movement is integrated once per hand/frame; ordered button edges are replayed as actions, without running the whole native Update repeatedly. Grabbable physics remains once per Unity frame.

The adapter reflects `Grabber.rawHandPlanePoint`, backing fields for Held/Hovered, `heldLastFrame`, and `suppressSweep` for cursor rebasing and cancellation. Those field names and the patched methods are checked before installation. Runtime method-detour and callback behavior still need Windows validation.

## Raw Input contract

- Enumerate with `GetRawInputDeviceList`, inspect names and mouse capabilities with `GetRawInputDeviceInfo`, and associate every packet's `RAWINPUTHEADER.hDevice` with its device path.
- Query `GetRegisteredRawInputDevices` before hooking. Reuse the existing mouse receiver/flags when supported; if absent, register foreground mouse input with DEVNOTIFY on Unity's own window.
- Install a same-thread `WH_GETMESSAGE` hook. Copy only removed `WM_INPUT` messages (`PM_REMOVE`), ignoring repeated non-removing peeks; never change or swallow MSG and always call `CallNextHookEx`. This is the sole packet capture path. Reuse one native buffer, growing only for an unusually large packet. A `SetWindowSubclass` hook handles focus/device/window lifecycle, and does not read mouse packets. Always call `DefSubclassProc`, including after errors. Never request NOLEGACY, INPUTSINK, exclusive capture, or a new helper window.
- Read with `GetRawInputData`; check header type before enforcing the mouse payload size. Keyboard/HID messages reaching the shared Unity receiver are ignored, not treated as broken mouse packets. Cursor-capture changes do not clear input; actual focus loss still does. Preserve each device's event order. Coalesce consecutive movement while retaining every button boundary. One snapshot is drained per Unity frame and then consumed by both hands. Windows relative Y is converted to Unity screen Y.
- Store paths rather than handles. Drop a disconnected device's queued/history state without clearing the other device. A bounded-queue overflow invalidates all input and releases both holds instead of silently losing button-up events.
- Refuse cross-thread receivers and page/exclusion registrations. If the process changes receivers, disable rather than re-register over it. Unhook the thread observer and window subclass, free the reusable buffer, and remove only a still-matching registration created by this mod. Inert callbacks remain rooted if native unhook fails.

Microsoft references: [Raw Input overview](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-raw-input), [registration constraints](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerrawinputdevices), [message-hook timing](https://learn.microsoft.com/en-us/windows/win32/winmsg/getmsgproc), [subclass thread restrictions](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass).
