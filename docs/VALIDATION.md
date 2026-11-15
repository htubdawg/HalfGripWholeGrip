# Validation record

## Executed here

Environment: Linux x86-64; .NET SDK 8.0.427. No Windows player, live Unity window, or pair of physical mice is attached.

- HalfGripRefs' source-mod compile checker, invoked with an isolated HalfGripTools 1.0.6 copy against its private 0.419 references: **0 warnings, 0 errors**.
- Production `MouseFrames`, automatic `MouseAssignment`, and native structure checks: **pass**. Covers independent movement/button state, release isolation, down/move/up/down ordering, an entire click between frames, repeated snapshots without replay, latest menu click ordering across devices, device-specific disconnect cleanup, case-insensitive paths, motion coalescing/64-bit accumulation, clearing, overflow recovery, concurrent producer/snapshot delivery, Windows raw-structure sizes/offsets, menu-derived right assignment, automatic second-mouse assignment, same-device rejection, third-device noninterference, replacement assignment, and no-menu fallback.
- Read-only inspection of the reference game assembly, wheel/shifter paths, guns/crosshair, camera and singleton callers, loader lifecycle, and cited mod examples: complete; see INSPECTION.md.
- Reference repositories' working trees remained clean after the checks. No private assemblies, decompiled source, build outputs, or dumps are tracked here.

These checks do **not** execute Unity, Harmony detours, Win32 entry points, or actual grab/release callbacks. They cannot demonstrate independent cockpit interaction.

## 0.2.0 interaction rewrite

The user reports 0.1.3 remains jittery/laggy, with overlapping behavior and substantial missing input. Previous compile/queue-only checks did not exercise the whole hand scheduler and did not establish that the native shared input interception worked. 0.2.0 replaces that design rather than adding another accessor patch.

- `Grabber.Update` is never manually invoked and its shared mouse reads are never used for either owned hand. All `GameInput.Hand*` and UpdateCursor interception has been removed. `HandDriver` consumes DeviceFrame data explicitly; its Unity adapter calls only native geometry/callback helpers.
- Per-device movement totals are applied once per hand/frame. Ordered presses/releases remain individual actions, preserving even complete clicks between frames. Held-object target update is at most once per frame, independent of raw polling rate. Frame-total movement is applied before that frame's ordered button edges, like a frame snapshot; this does not reconstruct the exact sub-frame pointer trajectory.
- A same-thread `WH_GETMESSAGE` hook copies WM_INPUT before it returns to Unity's message retrieval code. Non-removing peeks are ignored. The window subclass no longer reads packets, preventing double delivery. A retained native buffer avoids per-sample HGlobal allocation and the usual separate size query. Original messages/hook processing remain intact.
- Automatic log summaries report packet totals and both held targets every ten seconds. No extra test controls or modes were added.

**Executed checks:** zero compiler warnings/errors; production decoder/queue/assignment/layout/concurrency checks; production HandDriver with interaction doubles at 1, 4, 8, 64 and 500 samples per mouse/frame over 200 frames each. The scheduler checks two simultaneous holds, secondary clicks on both hands, one hand releasing/regrabbing while the other continuously moves/holds, complete between-frame clicks, exact delta totals, exactly one Move and at most one held update per hand/frame, and competing ownership/release isolation. Native metadata auditing checks that all called geometry helpers exist and neither they nor the compiled controller/scheduler read shared Hand*/aggregate mouse APIs. MSG layout and the production message-removal gate are also checked.

**Limits:** the interaction doubles do not execute Unity physics/callbacks. Native hooks, Harmony prefixes, camera projection and rendering still need Windows testing. The message hook observes WM_INPUT removed by GetMessage/PeekMessage; it does not detour direct GetRawInputBuffer calls. The native engine's exact message/buffer consumption behavior cannot be measured on this Linux machine. No claim of a verified final Windows gameplay fix is made from these tests. Source and evidence are available for further review.

## User report and 0.1.2 regression fix

The supplied Windows log confirms **0.1.1** loaded on game **0.419**, Unity **6000.3.22f1**, and HalfGripTools **1.0.6.0**, attached to Unity's existing mouse registration with flags `0x0`, and assigned both mice. It then records four `Invalid RAWINPUT packet size` failures and repeated cockpit recapture. The user reports one mouse's movement blocking the other's clicks. This is a failed gameplay test, not acceptance of independent interaction.

The old reader validated every shared-window WM_INPUT against mouse-payload size before checking the packet type. A normal keyboard packet is shorter and wrongly triggers release/reset of both hands. The log did not include the rejected packet type/size, so it cannot establish that every observed reset had that cause. 0.1.2 ignores non-mouse packets by type first and includes size details for genuine failures. It also stops clearing input on ordinary WM_CAPTURECHANGED and rewrites native hand-button reads to explicit per-grabber calls rather than relying only on global accessor detours.

Added passing regressions exercise the production decoder/accumulator with 500 frames of A holding/moving while B clicks and keyboard packets interleave, then 500 frames with the devices reversed and complete between-frame clicks. Keyboard/HID packets must not reset either mouse; actual truncated mouse packets are rejected. A separate check applies the production transpiler to the real reference assembly's hand-read call sites and verifies all seven receive the correct Grabber argument. This metadata check does not execute Unity or Windows detours. The test runner uses Mono.Cecil 0.11.4 for offline reference-assembly inspection; no additional dependency is shipped in the mod.

**0.1.2 subsequently failed to initialize in the user's Windows test; see the 0.1.3 record below.** No additional hotkeys, modes or setup steps were introduced.

## 0.1.2 startup regression and 0.1.3 correction

The next supplied Windows log reports `Failed to patch void Grabber::Update()` followed by `MissingMethodException: Grabber input call sites differ from the inspected build`, and the loader switches Whole Grip off. This confirms the strict transpiler introduced in 0.1.2 prevented the mod from loading. The log does not explain why its input call-site matching differed; it is not evidence that the user's game version is wrong. The prior offline check modeled call sites using reflection metadata from the reference DLL and did not exercise the user's live HarmonyX pipeline. It therefore failed to establish runtime compatibility.

Version **0.1.3** removes that transpiler, its wrappers, and its offline binding check. It reinstates the prefix-based Grabber.Update installation that successfully loaded in the user's 0.1.1 run. Scoped button reads use the existing per-grabber input selector. The decoder/type-validation and WM_CAPTURECHANGED fixes remain. Production mixed-packet, simultaneous input, assignment, layout and concurrency checks still run; compilation remains against 0.419 references / loader 1.0.6.

**0.1.3 still needs Windows validation.** Replace the mod folder and fully restart the game; no config edits or mod hotkeys are needed. The old failed transpiler must not remain in the running process. No changes to other repositories are part of this correction.

## Versions

| Component | Inspected / compile checked | Windows runtime tested |
| --- | --- | --- |
| Game | HalfGripRefs labeled 0.419; assembly hash in INSPECTION.md | User log confirms 0.419 with 0.1.1; interaction failed; 0.2.0 pending |
| Unity | Reference README labels 6000.3.22f1 | User log confirms 6000.3.22f1 with 0.1.1 |
| HalfGripTools | 1.0.6, commit d2c7a786b384566562cf6fcfbdb57eb36b16e351 | User log confirms 1.0.6.0 with 0.1.1 |
| BepInEx | Reference set labeled 5.4.23.5 | Not tested |

## Windows acceptance procedure

First use a clean vanilla game with only HalfGripTools and this mod enabled. Do not enable SlapPack or gun mods for this milestone. Record the installed game, Unity, loader, BepInEx versions and Raw Input receiver/registration mode from `BepInEx\LogOutput.log`.

Every item below remains **pending for 0.2.0** until performed on Windows. Capture a short video of the two physical mice and cockpit together for the first playable milestone.

1. **Automatic assignment.** Replace the old mod with 0.2.0 and restart, leaving any old config in place. Navigate the menu with mouse A and start gameplay. A must become the blue R hand. Click mouse B in the cockpit; it must become orange L, with interaction immediately enabled. No function keys or config changes should be needed. Repeat with saved assignments and a different menu mouse.
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
