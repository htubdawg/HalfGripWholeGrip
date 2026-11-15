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
            if (Update == null || LateUpdate == null || RawPoint == null || Held == null || Hovered == null || HeldLast == null || Suppress == null || HoverMethod == null || InteractMethod == null || SecondaryMethod == null || MoveHeldMethod == null || BeginMethod == null || EndMethod == null)
                throw new MissingMemberException("Grabber shape differs from inspected build.");
            Patch(harmony, typeof(Grabber), "Update", nameof(GrabberUpdate));
            Patch(harmony, typeof(Grabber), "LateUpdate", nameof(GrabberLate));
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
            return c == null || !c.Owns(__instance);
        }
        private static bool GrabberLate(Grabber __instance)
        {
            var c = DualHandController.Instance;
            return c == null || !c.Owns(__instance) || c.InvokingIndicators;
        }
        private static readonly MethodInfo HoverMethod = AccessTools.Method(typeof(Grabber), "UpdateHover");
        private static readonly MethodInfo InteractMethod = AccessTools.Method(typeof(Grabber), "TryInteract");
        private static readonly MethodInfo SecondaryMethod = AccessTools.Method(typeof(Grabber), "DispatchRightClick");
        private static readonly MethodInfo MoveHeldMethod = AccessTools.Method(typeof(Grabber), "UpdateHeld");
        private static readonly MethodInfo BeginMethod = AccessTools.Method(typeof(Grabber), "BeginHeldLook");
        private static readonly MethodInfo EndMethod = AccessTools.Method(typeof(Grabber), "EndHeldLook");
        internal static void Hover(Grabber g, Ray ray) => HoverMethod.Invoke(g, new object[] { ray });
        internal static void Interact(Grabber g, bool click) => InteractMethod.Invoke(g, new object[] { click });
        internal static void Secondary(Grabber g) => SecondaryMethod.Invoke(g, null);
        internal static void MoveHeld(Grabber g, Ray ray) => MoveHeldMethod.Invoke(g, new object[] { ray });
        internal static bool BeginLook(Grabber g) => (bool)BeginMethod.Invoke(g, null);
        internal static void EndLook(Grabber g, bool begun) => EndMethod.Invoke(g, new object[] { begun });
        internal static void ClearHeld(Grabber g) { Held.SetValue(g, null); Hovered.SetValue(g, null); }
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
