# Validation record

## Executed here

Environment: Linux x86-64; .NET SDK 8.0.427. No Windows player, live Unity window, or pair of physical mice is attached.

- HalfGripRefs' source-mod compile checker, invoked with an isolated HalfGripTools 1.0.6 copy against its private 0.419 references: **0 warnings, 0 errors**.
- Production `MouseFrames`, automatic `MouseAssignment`, and native structure checks: **pass**. Covers independent movement/button state, release isolation, down/move/up/down ordering, an entire click between frames, repeated snapshots without replay, latest menu click ordering across devices, device-specific disconnect cleanup, case-insensitive paths, motion coalescing/64-bit accumulation, clearing, overflow recovery, concurrent producer/snapshot delivery, Windows raw-structure sizes/offsets, menu-derived right assignment, automatic second-mouse assignment, same-device rejection, third-device noninterference, replacement assignment, and no-menu fallback.
- Read-only inspection of the reference game assembly, wheel/shifter paths, guns/crosshair, camera and singleton callers, loader lifecycle, and cited mod examples: complete; see INSPECTION.md.
- Reference repositories' working trees remained clean after the checks. No private assemblies, decompiled source, build outputs, or dumps are tracked here.

These checks do **not** execute Unity, Harmony detours, Win32 entry points, or actual grab/release callbacks. They cannot demonstrate independent cockpit interaction.

## Versions

| Component | Inspected / compile checked | Windows runtime tested |
| --- | --- | --- |
| Game | HalfGripRefs labeled 0.419; assembly hash in INSPECTION.md | Not tested; installed build unavailable |
| Unity | Reference README labels 6000.3.22f1 | Not tested |
| HalfGripTools | 1.0.6, commit d2c7a786b384566562cf6fcfbdb57eb36b16e351 | Not tested |
| BepInEx | Reference set labeled 5.4.23.5 | Not tested |

## Windows acceptance procedure

First use a clean vanilla game with only HalfGripTools and this mod enabled. Do not enable SlapPack or gun mods for this milestone. Record the installed game, Unity, loader, BepInEx versions and Raw Input receiver/registration mode from `BepInEx\LogOutput.log`.

Every item below is **pending** until performed on Windows. Capture a short video of the two physical mice and cockpit together for the first playable milestone.

1. **Automatic assignment.** Replace the old mod with 0.1.1 and restart, leaving any old config in place. Navigate the menu with mouse A and start gameplay. A must become the blue R hand. Click mouse B in the cockpit; it must become orange L, with interaction immediately enabled. No function keys or config changes should be needed. Repeat with saved assignments and a different menu mouse.
2. **Input visibility.** Move each mouse separately; verify its own colored hand and status reports input. If attachment fails, the status must show a reason and ordinary controls must remain available. Verify F7/F8/F9 and Shift+F8 retain their existing game/other-mod behavior.
3. **Wheel plus shifter.** Hold/move the wheel with A while B independently grabs/moves the standard H-gate. Repeat with hands swapped, including simultaneous movement/clicks and rapid clicks entirely between rendered frames.
4. **Release isolation.** Hold both objects. Release either mouse while the other continues moving/holding its object. Only the released hand may let go.
5. **Ownership.** Aim both hands at one object and press together. Only one may own it. The rejected hand must not move/release it or activate its secondary action. After owner release, the other may acquire it.
6. **Secondary actions and looking.** Confirm the owning mouse's secondary button activates vanilla steering lock/recenter. Exercise keyboard look while both objects are held, checking camera restoration and both movement planes.
7. **Pause/focus/mute.** While holding both objects, pause, Alt+Tab, minimize, or open gameplay popups. Both holds must clear; menus remain usable. On return, release and press again; no stale grab may replay.
8. **Disconnect/replacement.** Unplug either mouse while both hold. Only its hold/input must clear (within one second without device notifications). Reconnect it, or click a different replacement mouse: assignment recovers automatically and the other hand keeps its assignment/hold.
9. **Scenes and unload.** Change scene/restart with both holding. New cockpit objects must bind without stale references. Disable through HalfGripTools' normal mod manager or speedrun unload lifecycle; ordinary input, visuals and window hooks must restore. No mod-specific enable key is required.
10. **Failure/high-rate input.** Check receiver/thread/read errors and high-poll-rate mice. Refusal must preserve ordinary controls. Verify no lost release or repeated transition.

A compile-pass build is not the first playable milestone. Successful Windows input capture and wheel/shifter/release/ownership checks are still required, along with lifecycle/menu validation before treating the prototype as usable.
