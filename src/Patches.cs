using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WholeGrip
{
    internal static class Patches
    {
        internal static readonly MethodInfo Update = AccessTools.Method(typeof(Grabber), "Update");
        internal static readonly MethodInfo LateUpdate = AccessTools.Method(typeof(Grabber), "LateUpdate");
        internal static readonly FieldInfo RawPoint = AccessTools.Field(typeof(Grabber), "rawHandPlanePoint");
        private static readonly FieldInfo Held = AccessTools.Field(typeof(Grabber), "<Held>k__BackingField");
        private static readonly FieldInfo Hovered = AccessTools.Field(typeof(Grabber), "<Hovered>k__BackingField");
        private static readonly FieldInfo HeldLast = AccessTools.Field(typeof(Grabber), "heldLastFrame");
        private static readonly FieldInfo Suppress = AccessTools.Field(typeof(Grabber), "suppressSweep");
        internal static void Install(Harmony harmony)
        {
            if (Update == null || LateUpdate == null || RawPoint == null || Held == null || Hovered == null || HeldLast == null || Suppress == null)
                throw new MissingMemberException("Grabber shape differs from inspected build.");
            // Patch the small input accessors before recompiling their callers, avoiding stale
            // inlined aggregate mouse reads when Harmony generates Grabber's replacement body.
            Patch(harmony, typeof(GameInput), "Hand", nameof(Hand));
            Patch(harmony, typeof(GameInput), "HandDown", nameof(HandDown));
            Patch(harmony, typeof(GameInput), "HandUp", nameof(HandUp));
            Patch(harmony, typeof(Grabber), "Update", nameof(GrabberUpdate));
            Patch(harmony, typeof(Grabber), "LateUpdate", nameof(GrabberLate));
            Patch(harmony, typeof(Grabber), "UpdateCursor", nameof(Cursor));
            Patch(harmony, typeof(Grabber), "TryInteract", nameof(Interact));
            Patch(harmony, typeof(Grabber), "DispatchRightClick", nameof(RightClick));

            Patch(harmony, typeof(PauseController), "SetPaused", nameof(Pause));
            // This gate closes immediately when UI mutes gameplay during a later script's Update.
            harmony.Patch(AccessTools.Method(typeof(GameInput), "Mute"), postfix: new HarmonyMethod(typeof(Patches), nameof(Mute)));
        }
        private static void Patch(Harmony h, Type type, string method, string prefix)
        {
            var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.Name, method);
            h.Patch(target, prefix: new HarmonyMethod(typeof(Patches), prefix));
        }
        private static bool GrabberUpdate(Grabber __instance)
        {
            var c = DualHandController.Instance;
            return c == null || !c.Owns(__instance) || c.Invoking;
        }
        private static bool GrabberLate(Grabber __instance) => GrabberUpdate(__instance);
        private static bool Cursor(Grabber __instance)
        {
            var c = DualHandController.Instance;
            if (c == null || !c.Owns(__instance)) return true;
            c.UpdateCursor(__instance);
            return false;
        }
        private static bool Interact(Grabber __instance)
        {
            var c = DualHandController.Instance;
            return c == null || !c.Owns(__instance) || c.CanInteract(__instance, false);
        }
        private static bool RightClick(Grabber __instance)
        {
            var c = DualHandController.Instance;
            return c == null || !c.Owns(__instance) || c.CanInteract(__instance, true);
        }
        private static bool Read(int button, int kind, ref bool result)
        {
            var c = DualHandController.Instance;
            if (c == null || !c.CapturingGameplay) return true;
            var hand = c.ActiveHand;
            result = hand != null && c.ReadHand(hand.Grabber, button, kind);
            return false;
        }
        private static bool Hand(int button, ref bool __result) => Read(button, 0, ref __result);
        private static bool HandDown(int button, ref bool __result) => Read(button, 1, ref __result);
        private static bool HandUp(int button, ref bool __result) => Read(button, 2, ref __result);
        private static void Pause(bool paused) { if (paused) DualHandController.Instance?.Suspend(); }
        private static void Mute() { if (GameInput.Muted) DualHandController.Instance?.Suspend(); }
        internal static void Release(Grabber grabber)
        {
            if (!grabber) return;
            var held = grabber.Held;
            try
            {
                if (held)
                {
                    // Supported wheel/gate types inherit base CanRelease. Safety cancellation
                    // overrides blockRelease without changing steering lock state.
                    bool blocked = held.blockRelease;
                    try { held.blockRelease = false; held.Release(); }
                    finally { if (held) held.blockRelease = blocked; }
                }
            }
            finally
            {
                Held.SetValue(grabber, null); Hovered.SetValue(grabber, null);
                HeldLast.SetValue(grabber, false); Suppress.SetValue(grabber, false);
            }
        }
    }
}
