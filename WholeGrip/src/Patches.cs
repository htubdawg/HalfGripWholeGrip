using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WholeGrip
{
	/// <summary>
	/// The Harmony patches, installed all together or not at all (a half-installed input coordinator would be worse than none).
	/// Every patch passes straight through unless <see cref="Hands"/> is driving.
	/// <list type="table">
	/// <item><term>Grabber.Update</term><description>Prefix/finalizer: while a driven hand's Update runs, button reads answer for it.</description></item>
	/// <item><term>Grabber.UpdateCursor</term><description>Prefix: a driven hand moves by its own mouse (<see cref="Hands.MoveCursor"/>).</description></item>
	/// <item><term>Grabber.TryInteract, DispatchRightClick</term><description>Prefix: one owner per object; the left hand keeps to the wheel and shifter.</description></item>
	/// <item><term>GameInput.GetKey/GetKeyDown/GetKeyUp, GetMouseButton/Down/Up</term><description>Prefix: mouse buttons 0 and 1 answer from the hand's <see cref="HandInput"/>.</description></item>
	/// </list>
	/// The button patches sit on GetKey and friends, not on GameInput.Hand/HandDown/HandUp: those are one-line wrappers that
	/// Mono can inline into their callers (Grabber.Update among them), and a patch on an inlined method is never reached.
	/// </summary>
	internal static class Patches
	{
		private static readonly AccessTools.FieldRef<Grabber, Vector2> rawPoint = Field<Vector2>("rawHandPlanePoint");
		private static readonly AccessTools.FieldRef<Grabber, Grabbable> held = Field<Grabbable>("<Held>k__BackingField");
		private static readonly AccessTools.FieldRef<Grabber, InteriorCollider> hovered = Field<InteriorCollider>("<Hovered>k__BackingField");
		private static readonly MethodInfo update = AccessTools.Method(typeof(Grabber), "Update");
		private static readonly MethodInfo lateUpdate = AccessTools.Method(typeof(Grabber), "LateUpdate");
		private static readonly object[] none = new object[0];

		private static AccessTools.FieldRef<Grabber, T> Field<T>(string name)
		{
			try { return AccessTools.FieldRefAccess<Grabber, T>(name); }
			catch { return null; }
		}

		internal static Vector2 RawPoint(Grabber grabber) => rawPoint(grabber);

		internal static void ClearHeld(Grabber grabber)
		{
			held(grabber) = null;
			hovered(grabber) = null;
		}

		/// <summary>Runs the game's (patched) Update of a hand Unity does not run itself.</summary>
		internal static void RunUpdate(Grabber grabber) => Invoke(update, grabber);

		internal static void RunLateUpdate(Grabber grabber) => Invoke(lateUpdate, grabber);

		private static void Invoke(MethodInfo method, Grabber grabber)
		{
			try { method.Invoke(grabber, none); }
			catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
		}

		/// <summary>Names of the game members this mod needs that are missing from the running game (empty: all there).</summary>
		internal static List<string> Missing()
		{
			var missing = new List<string>();
			if (rawPoint == null) missing.Add("Grabber.rawHandPlanePoint");
			if (held == null) missing.Add("Grabber.Held");
			if (hovered == null) missing.Add("Grabber.Hovered");
			if (update == null) missing.Add("Grabber.Update");
			if (lateUpdate == null) missing.Add("Grabber.LateUpdate");
			foreach (var target in Targets())
				if (target.Method == null) missing.Add(target.Name);
			return missing;
		}

		private struct Target
		{
			public string Name;
			public MethodBase Method;
			public string Prefix, Finalizer;
		}

		private static IEnumerable<Target> Targets()
		{
			yield return new Target { Name = "Grabber.Update", Method = update, Prefix = nameof(UpdatePrefix), Finalizer = nameof(UpdateFinalizer) };
			yield return new Target { Name = "Grabber.UpdateCursor", Method = AccessTools.Method(typeof(Grabber), "UpdateCursor"), Prefix = nameof(CursorPrefix) };
			yield return new Target { Name = "Grabber.TryInteract", Method = AccessTools.Method(typeof(Grabber), "TryInteract"), Prefix = nameof(InteractPrefix) };
			yield return new Target { Name = "Grabber.DispatchRightClick", Method = AccessTools.Method(typeof(Grabber), "DispatchRightClick"), Prefix = nameof(RightClickPrefix) };
			yield return new Target { Name = "GameInput.GetKey", Method = AccessTools.Method(typeof(GameInput), nameof(GameInput.GetKey), new[] { typeof(KeyCode) }), Prefix = nameof(KeyHeld) };
			yield return new Target { Name = "GameInput.GetKeyDown", Method = AccessTools.Method(typeof(GameInput), nameof(GameInput.GetKeyDown), new[] { typeof(KeyCode) }), Prefix = nameof(KeyDown) };
			yield return new Target { Name = "GameInput.GetKeyUp", Method = AccessTools.Method(typeof(GameInput), nameof(GameInput.GetKeyUp), new[] { typeof(KeyCode) }), Prefix = nameof(KeyUp) };
			yield return new Target { Name = "GameInput.GetMouseButton", Method = AccessTools.Method(typeof(GameInput), nameof(GameInput.GetMouseButton), new[] { typeof(int) }), Prefix = nameof(MouseHeld) };
			yield return new Target { Name = "GameInput.GetMouseButtonDown", Method = AccessTools.Method(typeof(GameInput), nameof(GameInput.GetMouseButtonDown), new[] { typeof(int) }), Prefix = nameof(MouseDown) };
			yield return new Target { Name = "GameInput.GetMouseButtonUp", Method = AccessTools.Method(typeof(GameInput), nameof(GameInput.GetMouseButtonUp), new[] { typeof(int) }), Prefix = nameof(MouseUp) };
		}

		/// <summary>Patches everything, or undoes what was done and throws.</summary>
		internal static void Install(Harmony harmony)
		{
			try
			{
				foreach (var target in Targets())
				{
					harmony.Patch(target.Method,
						prefix: target.Prefix != null ? new HarmonyMethod(typeof(Patches), target.Prefix) : null,
						finalizer: target.Finalizer != null ? new HarmonyMethod(typeof(Patches), target.Finalizer) : null);
				}
			}
			catch
			{
				harmony.UnpatchSelf();
				throw;
			}
		}

		// ---- Grabber ----

		private static void UpdatePrefix(Grabber __instance)
		{
			var hands = Hands.Instance;
			var input = hands != null ? hands.InputFor(__instance) : null;
			if (input == null) return;
			hands.EnsureFrame();
			Hands.Scoped = input;
		}

		private static void UpdateFinalizer() => Hands.Scoped = null;

		private static bool CursorPrefix(Grabber __instance)
		{
			var hands = Hands.Instance;
			var input = hands != null ? hands.InputFor(__instance) : null;
			if (input == null) return true;
			hands.MoveCursor(__instance, input);
			return false;
		}

		private static bool InteractPrefix(Grabber __instance)
		{
			var hands = Hands.Instance;
			if (hands == null || !hands.Driving) return true;
			var collider = __instance.Hovered;
			return hands.MayUse(__instance, collider ? collider.GetComponentInParent<Grabbable>() : null);
		}

		private static bool RightClickPrefix(Grabber __instance)
		{
			var hands = Hands.Instance;
			if (hands == null || !hands.Driving) return true;
			Grabbable target = __instance.Held;
			if (!target && __instance.Hovered) target = __instance.Hovered.GetComponentInParent<Grabbable>();
			return hands.MayUse(__instance, target);
		}

		// ---- GameInput ----

		private enum Read { Held, Down, Up }

		private static bool KeyHeld(KeyCode key, ref bool __result) => Answer((int)key - (int)KeyCode.Mouse0, Read.Held, ref __result);
		private static bool KeyDown(KeyCode key, ref bool __result) => Answer((int)key - (int)KeyCode.Mouse0, Read.Down, ref __result);
		private static bool KeyUp(KeyCode key, ref bool __result) => Answer((int)key - (int)KeyCode.Mouse0, Read.Up, ref __result);
		private static bool MouseHeld(int button, ref bool __result) => Answer(button, Read.Held, ref __result);
		private static bool MouseDown(int button, ref bool __result) => Answer(button, Read.Down, ref __result);
		private static bool MouseUp(int button, ref bool __result) => Answer(button, Read.Up, ref __result);

		/// <summary>Answers a read of mouse button 0 or 1 from the driven hand; anything else goes to the game.</summary>
		private static bool Answer(int button, Read read, ref bool result)
		{
			if (button < 0 || button >= HandInput.ButtonCount) return true;
			var hands = Hands.Instance;
			var input = hands != null ? hands.ButtonsNow() : null;
			if (input == null) return true;
			if (GameInput.Muted) { result = false; return false; }
			result = read == Read.Held ? input.Held(button) : read == Read.Down ? input.Down(button) : input.Up(button);
			return false;
		}
	}
}
