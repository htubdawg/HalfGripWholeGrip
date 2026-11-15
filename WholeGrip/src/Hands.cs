using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HalfGripTools;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WholeGrip
{
	/// <summary>
	/// Drives the cockpit's two hands from two mice.
	/// <para>
	/// The right hand is the game's own <see cref="Grabber"/>. The left hand is a second Grabber, built here, on a GameObject that
	/// stays inactive: Unity never runs it and the game's <c>FindFirstObjectByType&lt;Grabber&gt;()</c> lookups (pause menu, guns,
	/// ammo, tutorials) never find it, so everything that wants "the hand" keeps getting the right one. Its sprites are copies of
	/// the right hand's, mirrored, on an active sibling.
	/// </para>
	/// <para>
	/// Both hands run the game's own <c>Update</c> and <c>LateUpdate</c> once a frame, so grabbing, sweeping, releasing, held-look and
	/// the poses are the game's code, and other mods' patches to Grabber (SlapPack's) apply to both. What differs per hand is only
	/// what they read: <see cref="Patches"/> answers the game's mouse button reads with that hand's <see cref="HandInput"/> and
	/// replaces <c>UpdateCursor</c> with that hand's movement. Outside a hand's Update, the rest of the game sees the right hand's
	/// mouse as "the mouse".
	/// </para>
	/// <para>
	/// It drives only while the cockpit is live: interior camera on, not paused, input not muted, race not over, window focused,
	/// at least two mice. Otherwise the mice go back to the game and everything is vanilla.
	/// </para>
	/// </summary>
	internal sealed class Hands : MonoBehaviour
	{
		internal static Hands Instance;

		/// <summary>The hand whose Update is running, so the game's button reads during it answer for that hand.</summary>
		internal static HandInput Scoped;

		private ModContext context;
		private ConfigEntry<string> rightDevice, leftDevice;
		private ConfigEntry<float> rightSensitivity, leftSensitivity;
		private ConfigEntry<bool> hints, verbose;

		private RawMice raw;
		private readonly Assignment assignment = new Assignment();
		private readonly HandInput rightInput = new HandInput(), leftInput = new HandInput();
		private readonly Dictionary<IntPtr, string> mice = new Dictionary<IntPtr, string>(), paths = new Dictionary<IntPtr, string>();
		private readonly HashSet<string> connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly List<MousePacket> packets = new List<MousePacket>(512);
		private readonly bool[] physical = new bool[HandInput.ButtonCount];
		private readonly HashSet<Grabbable> leftMayHold = new HashSet<Grabbable>();

		private Grabber right, left;
		private GameObject leftRoot;
		private bool driving, refused, stopped;
		private int drainedFrame = -1;
		private float nextDeviceCheck, nextRightSearch, nextReport, messageUntil;
		private string message;

		internal bool Driving => driving;

		internal void Initialize(ModContext ctx)
		{
			context = ctx;
			Instance = this;
			rightDevice = ctx.Config.Bind("Devices", "Right", "", "Device path of the right hand's mouse. Empty: the first mouse to click in the cockpit.");
			leftDevice = ctx.Config.Bind("Devices", "Left", "", "Device path of the left hand's mouse. Empty: the first other mouse to click in the cockpit.");
			rightSensitivity = ctx.Config.Bind("Hands", "RightSensitivity", 1f, "Multiplies the game's hand sensitivity for the right hand.");
			leftSensitivity = ctx.Config.Bind("Hands", "LeftSensitivity", 1f, "Multiplies the game's hand sensitivity for the left hand.");
			hints = ctx.Config.Bind("Display", "Hints", true, "Show a line at the top of the screen while a hand still needs its mouse, and when one is assigned.");
			verbose = ctx.Config.Bind("Debug", "VerboseLog", false, "Write routine information (packet counts, takeovers) to the log.");
			assignment.Right = rightDevice.Value;
			assignment.Left = leftDevice.Value;
			assignment.Tidy();
			assignment.Assigned += OnAssigned;
			raw = new RawMice(ctx.Log.LogWarning);
			try { raw.Start(); }
			catch (Exception e)
			{
				raw = null;
				Say("Whole Grip is off: " + e.Message, 15f);
				ctx.Log.LogError("Mouse input could not start: " + e.Message);
			}
		}

		private void Info(string text) { if (verbose.Value) context.Log.LogInfo(text); }

		private void Say(string text, float seconds)
		{
			message = text;
			messageUntil = Time.unscaledTime + seconds;
		}

		// ---- frame ----

		private void Update()
		{
			if (stopped || context == null || raw == null) return;
			try
			{
				if (Time.unscaledTime >= nextDeviceCheck) RefreshMice();
				string fault = raw.TakeFault();
				if (fault != null)
				{
					context.Log.LogWarning(fault);
					Say("Whole Grip: " + fault, 10f);
					refused = true;
				}
				FindRight();
				bool wanted = Wanted();
				if (wanted && !driving) Begin();
				else if (!wanted && driving) End(InteriorMath.Paused || GameInput.Muted);
				if (driving && !raw.Taken) End(false); // lost the registration on the input thread (Fault says why)
				if (!driving) { HideLeft(); return; }
				EnsureFrame();
				if (LeftActive) Patches.RunUpdate(left);
				else { ReleaseLeft(); HideLeft(); }
				if (verbose.Value && Time.unscaledTime >= nextReport)
				{
					nextReport = Time.unscaledTime + 10f;
					context.Log.LogInfo("Packets so far: " + raw.Packets + "; right holds " + Name(right.Held) + ", left holds " +
						(left ? Name(left.Held) : "nothing"));
				}
			}
			catch (Exception e) { Fail(e); }
		}

		private void LateUpdate()
		{
			if (stopped || !driving) return;
			try
			{
				if (LeftActive) Patches.RunLateUpdate(left);
				else HideLeft();
			}
			catch (Exception e) { Fail(e); }
		}

		private static string Name(Grabbable held) => held ? held.name : "nothing";

		/// <summary>The cockpit is live and there are two mice to drive it with.</summary>
		private bool Wanted() =>
			!refused && Application.isFocused && right && right.isActiveAndEnabled && right.lockHardwareCursor &&
			right.viewCamera && right.viewCamera.isActiveAndEnabled && !InteriorMath.Paused && !GameInput.Muted &&
			!GameManager.raceOver && Time.timeScale > 0f && mice.Count >= 2;

		private bool LeftActive => driving && left && assignment.Complete(connected.Contains);

		private void Begin()
		{
			if (!left) BuildLeft();
			FindLeftObjects();
			rightInput.Resync(right.Held);
			leftInput.Resync(left && left.Held);
			drainedFrame = -1;
			if (!raw.Take())
			{
				string fault = raw.TakeFault() ?? "Windows refused the mice";
				context.Log.LogWarning(fault + "; Whole Grip leaves the hand to the game until the next race");
				Say("Whole Grip: " + fault, 10f);
				refused = true;
				return;
			}
			driving = true;
			Info("Driving both hands (" + mice.Count + " mice; right " + Short(assignment.Right) + ", left " + Short(assignment.Left) + ")");
		}

		/// <summary>Gives the mice back. The left hand keeps its hold through a pause or a popup, as the game's own hand does.</summary>
		private void End(bool keepLeftHold)
		{
			driving = false;
			Scoped = null;
			raw?.GiveBack();
			if (!keepLeftHold) ReleaseLeft();
			HideLeft();
			Info("Mice given back to the game");
		}

		/// <summary>
		/// Once a frame, on first use (whichever of this object, a hand's Update or a button read comes first): routes the packets
		/// read since the last frame to their hands and starts each hand's frame. No dependence on script execution order.
		/// </summary>
		internal void EnsureFrame()
		{
			if (!driving || drainedFrame == Time.frameCount) return;
			drainedFrame = Time.frameCount;
			raw.Drain(packets, out bool lost);
			// Packets and GetAsyncKeyState report physical buttons; the game's Mouse0 is the logical primary, which is the
			// physical right button when Windows' "switch primary and secondary buttons" is on.
			bool swapped = RawMice.ButtonsSwapped();
			foreach (var original in packets)
			{
				var packet = original;
				if (swapped) packet.Buttons = SwapButtons(packet.Buttons);
				switch (assignment.Route(PathOf(packet.Device), packet.AnyDown, connected.Contains))
				{
					case Side.Right: rightInput.Add(packet); break;
					case Side.Left: leftInput.Add(packet); break;
				}
			}
			if (lost)
			{
				context.Log.LogWarning("Mouse packets came faster than the game read them; both hands start again from what they hold");
				rightInput.Resync(right && right.Held);
				leftInput.Resync(left && left.Held);
			}
			physical[0] = RawMice.Physical(swapped ? 2 : 1);
			physical[1] = RawMice.Physical(swapped ? 1 : 2);
			rightInput.BeginFrame(physical);
			leftInput.BeginFrame(physical);
		}

		/// <summary>Exchanges the left (bits 0-1) and right (bits 2-3) button transitions of a packet.</summary>
		internal static ushort SwapButtons(ushort flags) =>
			(ushort)((flags & ~0xF) | ((flags & 0x3) << 2) | ((flags & 0xC) >> 2));

		private string PathOf(IntPtr device)
		{
			if (mice.TryGetValue(device, out string path)) return path;
			if (!paths.TryGetValue(device, out path))
			{
				path = RawMice.PathOf(device) ?? "";
				paths[device] = path;
				if (path.Length > 0) { mice[device] = path; connected.Add(path); }
			}
			return path;
		}

		private void RefreshMice()
		{
			nextDeviceCheck = Time.unscaledTime + 1f;
			RawMice.ListMice(mice, paths);
			connected.Clear();
			foreach (var pair in mice) { paths[pair.Key] = pair.Value; connected.Add(pair.Value); }
		}

		private void OnAssigned(Side side, string path)
		{
			if (side == Side.Right) rightDevice.Value = path; else leftDevice.Value = path;
			context.Log.LogInfo((side == Side.Right ? "Right" : "Left") + " hand: " + Short(path));
			Say("Whole Grip: " + (side == Side.Right ? "right" : "left") + " hand on " + Short(path), 4f);
			if (side == Side.Left && left && right)
				left.cursor = new Vector2(Mathf.Min(right.cursor.x, Screen.width * 0.5f) - Screen.width * 0.1f, Screen.height * 0.5f);
		}

		/// <summary>"VID_046D PID_C52B" from a device path, for the log and hints.</summary>
		internal static string Short(string path)
		{
			if (string.IsNullOrEmpty(path)) return "(none)";
			string upper = path.ToUpperInvariant();
			int vid = upper.IndexOf("VID_", StringComparison.Ordinal), pid = upper.IndexOf("PID_", StringComparison.Ordinal);
			if (vid >= 0 && pid >= 0 && vid + 8 <= upper.Length && pid + 8 <= upper.Length)
				return "mouse " + upper.Substring(vid, 8) + " " + upper.Substring(pid, 8);
			return path.Length > 40 ? "mouse …" + path.Substring(path.Length - 32) : path;
		}

		// ---- what the patches ask ----

		/// <summary>The input a Grabber reads while driving: its hand's, or null for any other Grabber or when not driving.</summary>
		internal HandInput InputFor(Grabber grabber)
		{
			if (!driving || !grabber) return null;
			if (grabber == right) return rightInput;
			if (grabber == left) return leftInput;
			return null;
		}

		/// <summary>What the game's button reads answer outside a hand's Update: the right hand's mouse. Null when not driving.</summary>
		internal HandInput ButtonsNow()
		{
			if (!driving) return null;
			EnsureFrame();
			return Scoped ?? rightInput;
		}

		/// <summary>
		/// Whether <paramref name="grabber"/> may grab, click or right click <paramref name="target"/> (null for a plain Clickable).
		/// One owner per object. The left hand only takes the wheel and the shifter: other grabbable parts (guns, SlapPack's
		/// stalks and cranks) read the game's mouse buttons themselves, which answer for the right hand.
		/// </summary>
		internal bool MayUse(Grabber grabber, Grabbable target)
		{
			if (!driving || !target) return true;
			if (grabber == right) return !(left && left.Held == target);
			if (grabber == left) return (!right || right.Held != target) && leftMayHold.Contains(target);
			return true;
		}

		/// <summary>The game's UpdateCursor for a driven hand, with that hand's own movement instead of the system mouse's.</summary>
		internal void MoveCursor(Grabber grabber, HandInput input)
		{
			var held = grabber.Held;
			bool hidden = held && held.frozen && held.hideHandWhenFrozen;
			Camera camera = grabber.viewCamera;
			if (held && !hidden) grabber.cursor = camera.WorldToScreenPoint(held.PlaneToWorld(Patches.RawPoint(grabber)));
			if (!GameInput.Muted)
			{
				float multiplier = Sanitize(grabber == left ? leftSensitivity.Value : rightSensitivity.Value);
				float scale = grabber.cursorSensitivity * Grabber.HandSensitivity * multiplier * Screen.height / 1080f;
				// Windows' Y grows downward, the screen's upward.
				grabber.cursor += new Vector2(input.X, -input.Y) * scale;
			}
			// The game's gamepad helper also remembers which Grabber is the hand; keep that the right one.
			if (grabber == right) grabber.cursor = GamepadHand.MoveCursor(grabber, grabber.cursor, hidden);
			if (!held || hidden)
			{
				Vector2 margin = new Vector2(Screen.width, Screen.height) * (Mathf.Max(1f, grabber.screenLimitScale) - 1f) * 0.5f;
				grabber.cursor = new Vector2(Mathf.Clamp(grabber.cursor.x, -margin.x, Screen.width + margin.x),
					Mathf.Clamp(grabber.cursor.y, -margin.y, Screen.height + margin.y));
			}
		}

		private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp(value, 0.05f, 20f);

		// ---- the hands ----

		private void FindRight()
		{
			if (right) return;
			if (left) DestroyLeft();
			if (Time.unscaledTime < nextRightSearch) return;
			nextRightSearch = Time.unscaledTime + 1f;
			right = Object.FindFirstObjectByType<Grabber>(); // the inactive left hand is never found
		}

		/// <summary>The wheel and the H shifter of the right hand's cockpit: what the left hand may hold.</summary>
		private void FindLeftObjects()
		{
			leftMayHold.Clear();
			var scene = right.gameObject.scene;
			foreach (var wheel in Object.FindObjectsByType<NSteeringWheel>(FindObjectsSortMode.None))
				if (wheel.wheel && wheel.gameObject.scene == scene) leftMayHold.Add(wheel.wheel);
			foreach (var shifter in Object.FindObjectsByType<NShifter>(FindObjectsSortMode.None))
				if (shifter.gate && shifter.gameObject.scene == scene) leftMayHold.Add(shifter.gate);
			Info("Left hand may hold " + leftMayHold.Count + " part(s)");
		}

		/// <summary>
		/// The left hand: tuning copied from the right one, sprites copied and mirrored. No copy of the right hand's object tree:
		/// no scripts, colliders or events come along.
		/// </summary>
		private void BuildLeft()
		{
			leftRoot = new GameObject("Whole Grip left hand");
			leftRoot.transform.SetParent(right.transform.parent, false);
			var host = new GameObject("Whole Grip left grabber (inactive)");
			host.SetActive(false);
			host.transform.SetParent(leftRoot.transform, false);
			left = host.AddComponent<Grabber>();
			left.viewCamera = right.viewCamera;
			left.lockHardwareCursor = true;
			left.cursorSensitivity = right.cursorSensitivity;
			left.screenLimitScale = right.screenLimitScale;
			left.heldRotationFromVelocity = right.heldRotationFromVelocity;
			left.heldRotationSmoothing = right.heldRotationSmoothing;
			left.handMagnetSmoothing = right.handMagnetSmoothing;
			left.indicatorDepth = right.indicatorDepth;
			left.intermediatePoses = right.intermediatePoses;
			left.beforeIntermediate = right.beforeIntermediate;
			left.afterIntermediate = right.afterIntermediate;
			left.forceDirectionMinDrag = right.forceDirectionMinDrag;
			left.forceDirectionMaxDrag = right.forceDirectionMaxDrag;
			left.idleSprite = CopySprite(right.idleSprite, true);
			left.interactibleSprite = CopySprite(right.interactibleSprite, true);
			var heldSprite = CopySprite(right.handIndicator ? right.handIndicator.GetComponent<SpriteRenderer>() : null, true);
			left.handIndicator = heldSprite ? heldSprite.transform : null;
			if (heldSprite) { heldSprite.enabled = true; heldSprite.gameObject.SetActive(false); }
			left.idleInteractibleSprite = CopySprite(right.idleInteractibleSprite, true);
			left.idleHeldSprite = CopySprite(right.idleHeldSprite, true);
			left.interactibleHeldSprite = CopySprite(right.interactibleHeldSprite, true);
			left.forceDirectionSprite = CopySprite(right.forceDirectionSprite, false);
			left.cursor = new Vector2(Screen.width * 0.4f, Screen.height * 0.5f);
			left.SetHandVisible(false);
			Info("Left hand built beside " + right.name);
		}

		private SpriteRenderer CopySprite(SpriteRenderer source, bool mirror)
		{
			if (!source) return null;
			var copy = new GameObject("Whole Grip L " + source.name) { layer = source.gameObject.layer };
			copy.transform.SetParent(leftRoot.transform, false);
			copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
			Vector3 parent = leftRoot.transform.lossyScale, wanted = source.transform.lossyScale;
			copy.transform.localScale = new Vector3(wanted.x / NonZero(parent.x), wanted.y / NonZero(parent.y), wanted.z / NonZero(parent.z));
			var sprite = copy.AddComponent<SpriteRenderer>();
			sprite.sprite = source.sprite;
			sprite.sharedMaterial = source.sharedMaterial;
			sprite.color = source.color;
			sprite.sortingLayerID = source.sortingLayerID;
			sprite.sortingOrder = source.sortingOrder;
			sprite.flipX = mirror ? !source.flipX : source.flipX;
			sprite.flipY = source.flipY;
			sprite.enabled = false;
			return sprite;
		}

		private static float NonZero(float value) => Mathf.Abs(value) < 1e-6f ? 1f : value;

		private void HideLeft() { if (left) left.SetHandVisible(false); }

		/// <summary>Lets go of whatever the left hand holds (it stops being driven, or its mouse went away).</summary>
		private void ReleaseLeft()
		{
			if (!left || !left.Held) return;
			var held = left.Held;
			// blockRelease (guns' no-fumble) would leave the part grabbed with no hand on it: lift it for this one release.
			bool blocked = held.blockRelease;
			try { held.blockRelease = false; held.Release(); }
			catch (Exception e) { context.Log.LogWarning("Left hand release: " + e.Message); }
			finally { if (held) held.blockRelease = blocked; }
			Patches.ClearHeld(left);
			leftInput.Resync(false);
		}

		private void DestroyLeft()
		{
			ReleaseLeft();
			if (leftRoot) Object.Destroy(leftRoot);
			leftRoot = null;
			left = null;
		}

		// ---- lifecycle ----

		internal void SceneChanged()
		{
			if (stopped) return;
			if (driving) End(false);
			DestroyLeft();
			right = null;
			refused = false;
			nextRightSearch = 0f;
		}

		private void OnApplicationFocus(bool focused) { if (!focused && driving) End(false); }

		private void Fail(Exception e)
		{
			context.Log.LogError("Whole Grip stopped: " + e);
			Say("Whole Grip stopped: " + (e.InnerException ?? e).Message, 15f);
			try { if (driving) End(false); } catch { }
			stopped = true;
		}

		internal void Shutdown()
		{
			if (raw == null && stopped) return;
			try { if (driving) End(false); DestroyLeft(); }
			finally
			{
				stopped = true;
				raw?.Dispose();
				raw = null;
				if (Instance == this) Instance = null;
				Scoped = null;
			}
		}

		private void OnDestroy() => Shutdown();

		// ---- hints ----

		private void OnGUI()
		{
			if (stopped && message == null) return;
			string text = null;
			if (message != null && Time.unscaledTime < messageUntil) text = message;
			else if (driving && hints.Value)
			{
				if (!connected.Contains(assignment.Right ?? "")) text = "Whole Grip: click with the mouse for your RIGHT hand";
				else if (!connected.Contains(assignment.Left ?? "")) text = "Whole Grip: click with the mouse for your LEFT hand";
			}
			if (text == null) return;
			var rect = new Rect(16, 12, Screen.width - 32, 30);
			Color before = GUI.color;
			GUI.color = new Color(0f, 0f, 0f, 0.8f);
			GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text);
			GUI.color = Color.white;
			GUI.Label(rect, text);
			GUI.color = before;
		}
	}
}
