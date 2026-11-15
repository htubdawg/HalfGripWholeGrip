# Validation record

## Executed here

Environment: Linux x86-64; .NET SDK 8.0.427. No Windows player, live Unity window, or pair of physical mice is attached.

- HalfGripRefs' source-mod compile checker, invoked with an isolated HalfGripTools 1.0.6 copy against its private 0.419 references: **0 warnings, 0 errors**.
- Production `MouseFrames` and native structure checks: **pass**. Covers independent movement/button state, release isolation, down/move/up/down ordering, an entire click between frames, repeated snapshots without replay, latest menu click ordering across devices, device-specific disconnect cleanup, case-insensitive paths, motion coalescing/64-bit accumulation, clearing, overflow recovery, concurrent producer/snapshot delivery, and Windows raw-structure sizes/offsets.
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

1. **Normal menu and inferred right mouse.** Navigate the main menu using mouse A without any setup click sequence. Confirm the menu behaves normally. Start gameplay; verify A is inferred as R. Press F8 and click mouse B; release both. Confirm B becomes L. Check saved device paths and ensure enumeration order does not determine the assignment. On a later start verify saved identities persist.
2. **Independent diagnostic input.** Keep DiagnosticOnly on. Move A while B is stationary, then reverse. Hold/release each button separately. Make fast clicks that finish between rendered frames: that device's `down` and `up` must both appear, without changing the other's indicators. Move/click both mice together, including on the same rendered frame. Check that both colors remain independent.
3. **Vanilla wheel plus shifter.** Press F9. With A, hold and move the wheel. With B, independently grab and move the standard H-gate shifter into/out of gear while A continues steering. Repeat with the hands swapped. Confirm both native object motion and visible grip points follow the intended devices.
4. **Release isolation.** Hold wheel and shifter together. Release A while continuing to move/hold B; B must keep its object. Repeat with B releasing first. No input from the other device may sustain or release the wrong hand's object.
5. **Simultaneous transitions.** Press both primary buttons within one frame over different supported objects. Both must grab independently. Release and click both rapidly in either order; neither hand may inherit the other's held, down, or up state.
6. **Ownership conflict.** Aim both hands at the same wheel or gate and press together. Exactly one may grab. Move/release the rejected hand: it must not move/release the owner's object or activate its secondary action. After owner release, the other hand must be able to acquire it normally. Repeat many times.
7. **Secondary actions and looking.** Hold the wheel with one hand; press that mouse's secondary button and confirm vanilla lock/recenter behavior. The other mouse must not lock an owned wheel by hovering it. Exercise Q/E and shoulder look while both objects are held; check camera restoration and both hand/cursor planes.
8. **Pause, focus and mute.** While both hands hold, pause; verify both release and neither acts through the pause UI. Resume while buttons remain pressed: there must be no synthetic grab. Release and press again. Repeat with Alt+Tab, minimize, and gameplay popups/input mute. Menus must accept normal aggregate cursor/button input.
9. **Device disconnect/reconnect.** While both hold objects, unplug A; its hold/pending input must clear (within one second if Unity's existing registration lacks device notifications), while B continues holding. Reconnect A; no stale grab/press may replay. Repeat for B and both together. If the device path changes, F8/Shift+F8 must recover assignments.
10. **Scene transitions.** Hold both objects and quit/restart/change scene, including any additive UI loads. Confirm no stale references, held objects, or cursors survive; the next cockpit must bind fresh native objects and keep the device assignments.
11. **Disable, unload and reload.** While holding both objects, use F7. Confirm the original mouse hand controls the game again with no extra cursor, input suppression, or second hold. Repeat while paused/unfocused. Re-enable with F7 and verify diagnostics/interaction. Trigger HalfGripTools' speedrun disable/unload lifecycle and verify callbacks, hook/registration, scene objects and controls restore; no game speedrun setting is changed by this mod.
12. **Failure paths.** Review logs for duplicate registration, unsupported absolute devices, receiver thread mismatch, receiver changes or read errors. Refusal must preserve ordinary menu/game controls; it is not a passing Raw Input test. Test high-poll-rate mice and confirm no lost release or repeated transition.

A compile-pass build is not the first playable milestone. That milestone requires successful Windows diagnostic capture and at least checks 3–6, with the lifecycle/menu checks also completed before treating the prototype as usable.
