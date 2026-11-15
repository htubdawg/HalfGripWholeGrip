using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HalfGripTools;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WholeGrip
{
    [DefaultExecutionOrder(-10000)]
    public sealed class DualHandController : MonoBehaviour
    {
        internal static DualHandController Instance;
        private readonly MouseFrames frames = new MouseFrames();
        private readonly HandState right = new HandState("R", new Color(0.2f, 0.8f, 1f));
        private readonly HandState left = new HandState("L", new Color(1f, 0.55f, 0.15f));
        private readonly Dictionary<Grabbable, HandState> owners = new Dictionary<Grabbable, HandState>();
        private readonly HashSet<Grabbable> allowed = new HashSet<Grabbable>();
        private readonly Dictionary<SpriteRenderer, Color> originalColors = new Dictionary<SpriteRenderer, Color>();
        private readonly Dictionary<GameObject, bool> originalActive = new Dictionary<GameObject, bool>();
        private readonly Dictionary<SpriteRenderer, bool> originalRenderers = new Dictionary<SpriteRenderer, bool>();
        private ModContext context;
        private RawMouseInput raw;
        private readonly MouseAssignment assignment = new MouseAssignment();
        private bool faulted;
        private ConfigEntry<float> rightSensitivity, leftSensitivity;
        private ConfigEntry<string> rightDevice, leftDevice;
        private GameObject secondary;
        private bool captured, stopped, runningLast, originalLock;
        private CursorLockMode savedCursorLock;
        private bool savedCursorVisible;
        private int frameNumber = -1;
        private float nextRefresh;
        private string status = "Starting";
        internal bool Invoking { get; private set; }
        internal HandState ActiveHand { get; private set; }
        internal bool CapturingGameplay => captured;
        internal bool CanRun => captured && !faulted && Application.isFocused && !GameInput.Muted &&
            !InteriorMath.Paused && Time.timeScale > 0 && !GameManager.raceOver && right.Grabber &&
            right.Grabber.isActiveAndEnabled && right.Grabber.viewCamera && right.Grabber.viewCamera.isActiveAndEnabled;

        internal void Initialize(ModContext ctx)
        {
            context = ctx; Instance = this;
            rightSensitivity = ctx.Config.Bind("Hands", "RightSensitivity", 1f, "Raw counts multiplier, also scaled by game hand sensitivity.");
            leftSensitivity = ctx.Config.Bind("Hands", "LeftSensitivity", 1f, "Raw counts multiplier, also scaled by game hand sensitivity.");
            rightDevice = ctx.Config.Bind("Devices", "Right", "", "Device path for the right hand. Menu mouse use selects this automatically.");
            leftDevice = ctx.Config.Bind("Devices", "Left", "", "Device path for the left hand. A click from a different mouse assigns it automatically.");
            right.Device = rightDevice.Value; left.Device = leftDevice.Value;
            if (string.Equals(right.Device, left.Device, StringComparison.OrdinalIgnoreCase)) left.Device = "";
            assignment.Right = right.Device; assignment.Left = left.Device;
            status = "Use your usual mouse in the menu, then click the other mouse in the cockpit";
        }

        private bool IsMenu => SceneManager.GetActiveScene().name == "SCENE_UI_MAIN_MENU" ||
            (!right.Grabber && !Object.FindFirstObjectByType<Grabber>());

        private void Update()
        {
            if (stopped || context == null || frameNumber == Time.frameCount) return;
            frameNumber = Time.frameCount;
            try
            {
                if (faulted) return;
                if (!Application.isFocused) { Suspend(); return; }
                if (raw == null && Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + 2f;
                    raw = new RawMouseInput(frames, context.Log.LogInfo);
                    try { raw.Attach(); }
                    catch (Exception e)
                    {
                        Detach(); status = "Input unavailable: " + e.Message;
                        context.Log.LogWarning(status); return;
                    }
                }
                if (raw == null) return;
                if (!raw.Healthy)
                {
                    status = raw.Status; StopCapture(); Detach();
                    nextRefresh = Time.unscaledTime + 2f;
                    context.Log.LogWarning(status + "; retrying automatically"); return;
                }
                if (Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + 1f;
                    raw.RefreshDevices(); raw.VerifyReceiver();
                    if (!raw.Healthy) { Suspend(); return; }
                    foreach (var hand in Hands())
                        if (!string.IsNullOrEmpty(hand.Device) && !raw.Connected(hand.Device))
                        { Release(hand); hand.Rearmed = false; status = hand.Name + " disconnected; reconnect or click a replacement mouse"; }
                }
                bool menu = IsMenu;
                if (menu && captured) StopCapture();
                if (!menu && !captured) TryCapture();
                bool running = CanRun;
                if (!running && runningLast) Suspend();
                runningLast = running;
                // Recording menu Raw Input leaves Unity's own cursor/buttons and message handling untouched.
                raw.AcceptInput = menu || running;
                var snapshot = frames.Snapshot(out bool lost);
                if (lost) { Suspend(); status = "Raw Input queue overflow: holds cleared; release both buttons"; return; }
                if (menu) assignment.ObserveMenu(snapshot);
                if (!running) return;
                assignment.ObserveGameplay(snapshot, raw.Connected);
                ApplyAssignment(right, assignment.Right, rightDevice);
                ApplyAssignment(left, assignment.Left, leftDevice);
                foreach (var hand in Hands()) Tick(hand, snapshot);
            }
            catch (Exception e)
            {
                status = "Whole Grip stopped: " + (e.InnerException ?? e).Message;
                context.Log.LogError(status);
                StopCapture(); Detach(); faulted = true;
            }
        }

        private IEnumerable<HandState> Hands() { yield return right; yield return left; }

        private void TryCapture()
        {
            var original = Object.FindFirstObjectByType<Grabber>();
            if (!original || !original.isActiveAndEnabled || !original.viewCamera || !original.viewCamera.isActiveAndEnabled ||
                GameManager.raceOver || InteriorMath.Paused || GameInput.Muted || Time.timeScale <= 0) return;
            if (original.Held) { status = "Release the existing hand before Whole Grip starts"; return; }
            right.Grabber = original;
            allowed.Clear();
            // Only inspected vanilla objects. No guns, ammo, modded Grabbables or Clickables yet.
            foreach (var wheel in Object.FindObjectsByType<NSteeringWheel>(FindObjectsSortMode.None))
                if (wheel.isActiveAndEnabled && wheel.wheel && wheel.gameObject.scene == original.gameObject.scene) allowed.Add(wheel.wheel);
            foreach (var shifter in Object.FindObjectsByType<NShifter>(FindObjectsSortMode.None))
                if (shifter.isActiveAndEnabled && shifter.gate && shifter.gameObject.scene == original.gameObject.scene) allowed.Add(shifter.gate);
            if (allowed.Count == 0) { right.Grabber = null; return; }
            assignment.EnterGameplay(raw.Connected);
            ApplyAssignment(right, assignment.Right, rightDevice);
            ApplyAssignment(left, assignment.Left, leftDevice);
            originalLock = original.lockHardwareCursor;
            savedCursorLock = UnityEngine.Cursor.lockState; savedCursorVisible = UnityEngine.Cursor.visible;
            secondary = new GameObject("WholeGrip left hand");
            secondary.transform.SetParent(original.transform.parent, false);
            left.Grabber = secondary.AddComponent<Grabber>();
            left.Grabber.enabled = false; // driven explicitly; no cloned cockpit or native Start/Update
            CopySettings(original, left.Grabber);
            Patches.Release(original);
            original.lockHardwareCursor = true;
            right.Grabber.cursor = new Vector2(Screen.width * 0.6f, Screen.height * 0.5f);
            left.Grabber.cursor = new Vector2(Screen.width * 0.4f, Screen.height * 0.5f);
            captured = true;
            TintOriginal(original);
            HideIndicators(original); HideIndicators(left.Grabber);
            UnityEngine.Cursor.lockState = CursorLockMode.Locked; UnityEngine.Cursor.visible = false;
            frames.Clear(); right.Rearmed = left.Rearmed = false;
            status = "Active — steer with one mouse and shift with the other";
            context.Log.LogInfo("Captured vanilla wheel/shifter; interaction enabled automatically.");
        }

        private void ApplyAssignment(HandState hand, string device, ConfigEntry<string> setting)
        {
            if (string.Equals(hand.Device, device, StringComparison.OrdinalIgnoreCase)) return;
            Release(hand);
            hand.Device = device; hand.Rearmed = true; hand.SeenInput = false;
            hand.Frame = null; hand.Input = default;
            setting.Value = device;
            context.Log.LogInfo("Assigned " + hand.Name + " hand automatically.");
        }

        private void Tick(HandState hand, Dictionary<string, DeviceFrame> snapshot)
        {
            if (!hand.Grabber) return;
            if (string.IsNullOrEmpty(hand.Device) || !raw.Connected(hand.Device))
            { Release(hand); hand.Rearmed = false; hand.Frame = null; hand.Input = default; return; }
            if (!snapshot.TryGetValue(hand.Device, out var frame)) frame = new DeviceFrame();
            hand.Frame = frame;
            if (frame.X != 0 || frame.Y != 0 || frame.Down != 0 || frame.Up != 0) hand.SeenInput = true;
            if (!hand.Rearmed)
            {
                // After suspension/setup do not turn a still-held physical button into a sweep-grab.
                // A fresh down event is sufficient evidence after the first neutral frame.
                if (frame.Held == 0 && frame.Down == 0) hand.Rearmed = true;
                else return;
            }
            if (hand.Grabber.Held && !hand.Grabber.Held.IsGrabbed) Release(hand);
            var steps = frame.Steps;
            if (steps.Count == 0) steps.Add(new MouseStep(0, 0, frame.Held));
            foreach (var step in steps)
            {
                hand.Input = step;
                Invoke(hand, Patches.Update);
                ReconcileOwner(hand);
            }
            // A reconnect can replace a handle between polls without a button-up packet. The
            // accumulator forgets that device; neutral input must not leave its previous hold stuck.
            if (hand.Grabber.Held && (frame.Held & 1) == 0 && (frame.Up & 1) == 0) Release(hand);
        }
        private void ReconcileOwner(HandState hand)
        {
            var stale = new List<Grabbable>();
            foreach (var pair in owners)
                if (pair.Value == hand && pair.Key != hand.Grabber.Held) stale.Add(pair.Key);
            foreach (var key in stale) owners.Remove(key);
            if (hand.Grabber.Held)
            {
                if (!owners.ContainsKey(hand.Grabber.Held))
                    context.Log.LogInfo(hand.Name + " grabbed " + hand.Grabber.Held.name +
                        (owners.Count > 0 ? " while the other hand remains holding its object." : "."));
                owners[hand.Grabber.Held] = hand;
            }
        }
        private void Invoke(HandState hand, MethodInfo method)
        {
            Invoking = true; ActiveHand = hand;
            try { method.Invoke(hand.Grabber, null); }
            finally { ActiveHand = null; Invoking = false; }
        }
        internal bool Owns(Grabber grabber) => captured && (grabber == right.Grabber || grabber == left.Grabber);
        internal bool ReadHand(Grabber grabber, int button, int kind)
        {
            if (!CanRun || button < 0 || button >= 5) return false;
            var hand = grabber == right.Grabber ? right : grabber == left.Grabber ? left : null;
            if (hand == null || !hand.Rearmed) return false;
            int bits = kind == 0 ? hand.Input.Held : kind == 1 ? hand.Input.Down : hand.Input.Up;
            return (bits & (1 << button)) != 0;
        }
        internal bool CanInteract(Grabber grabber, bool secondaryAction)
        {
            if (!CanRun || ActiveHand == null || ActiveHand.Grabber != grabber) return false;
            Grabbable target = secondaryAction && grabber.Held ? grabber.Held :
                grabber.Hovered ? grabber.Hovered.GetComponentInParent<Grabbable>() : null;
            if (!target || !allowed.Contains(target) || target.grabRedirect != null) return false;
            if (owners.TryGetValue(target, out var owner) && owner != ActiveHand) return false;
            return !target.IsGrabbed || (secondaryAction && grabber.Held == target);
        }
        internal void UpdateCursor(Grabber grabber)
        {
            if (!CanRun || ActiveHand == null || ActiveHand.Grabber != grabber) return;
            bool free = !grabber.Held || (grabber.Held.frozen && grabber.Held.hideHandWhenFrozen);
            if (!free) grabber.cursor = grabber.viewCamera.WorldToScreenPoint(grabber.Held.PlaneToWorld((Vector2)Patches.RawPoint.GetValue(grabber)));
            AddDelta(ActiveHand, free);
        }
        private void AddDelta(HandState hand, bool clamp)
        {
            var grabber = hand.Grabber;
            float setting = hand == right ? rightSensitivity.Value : leftSensitivity.Value;
            float sensitivity = float.IsNaN(setting) || float.IsInfinity(setting) ? 1f : Mathf.Clamp(setting, 0.01f, 20f);
            float scale = sensitivity * grabber.cursorSensitivity * Grabber.HandSensitivity * Screen.height / 1080f;
            // Windows relative Y increases downwards; Unity screen Y increases upwards.
            grabber.cursor += new Vector2(hand.Input.X, -hand.Input.Y) * scale;
            if (clamp)
            {
                Vector2 margin = new Vector2(Screen.width, Screen.height) * (Mathf.Max(1f, grabber.screenLimitScale) - 1f) * 0.5f;
                grabber.cursor = new Vector2(Mathf.Clamp(grabber.cursor.x, -margin.x, Screen.width + margin.x),
                    Mathf.Clamp(grabber.cursor.y, -margin.y, Screen.height + margin.y));
            }
        }

        private void LateUpdate()
        {
            if (stopped || !captured) return;
            if (!CanRun)
            { HideIndicators(right.Grabber); HideIndicators(left.Grabber); return; }
            try
            {
                foreach (var hand in Hands())
                    if (hand.Grabber && hand.Rearmed && raw != null && raw.Connected(hand.Device)) Invoke(hand, Patches.LateUpdate);
                    else HideIndicators(hand.Grabber);
            }
            catch (Exception e)
            { status = "Stopped: " + (e.InnerException ?? e).Message; context.Log.LogError(e); StopCapture(); Detach(); faulted = true; }
        }

        internal void Suspend()
        {
            if (raw != null) raw.AcceptInput = false;
            frames.Clear();
            foreach (var hand in Hands())
            {
                try { Release(hand); }
                catch (Exception e) { context?.Log.LogError("Release cleanup: " + e); }
                hand.Rearmed = false; hand.Frame = null; hand.Input = default; HideIndicators(hand.Grabber);
            }
            owners.Clear();
        }
        private void Release(HandState hand)
        {
            if (hand.Grabber) Patches.Release(hand.Grabber);
            var stale = new List<Grabbable>();
            foreach (var pair in owners) if (pair.Value == hand) stale.Add(pair.Key);
            foreach (var key in stale) owners.Remove(key);
        }
        internal void SceneChanged() { StopCapture(); frames.Clear(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Suspend(); }
        private void OnApplicationPause(bool paused) { if (paused) Suspend(); }
        private void StopCapture()
        {
            bool wasCaptured = captured;
            Suspend(); captured = false; runningLast = false;
            if (right.Grabber && wasCaptured) right.Grabber.lockHardwareCursor = originalLock;
            foreach (var pair in originalColors) if (pair.Key) pair.Key.color = pair.Value;
            foreach (var pair in originalRenderers) if (pair.Key) pair.Key.enabled = pair.Value;
            foreach (var pair in originalActive) if (pair.Key) pair.Key.SetActive(pair.Value);
            originalColors.Clear(); originalRenderers.Clear(); originalActive.Clear();
            if (right.Grabber && (!right.Grabber.isActiveAndEnabled || InteriorMath.Paused || Time.timeScale <= 0))
                HideIndicators(right.Grabber);
            if (secondary) { secondary.SetActive(false); Object.Destroy(secondary); }
            secondary = null; right.Grabber = left.Grabber = null; allowed.Clear();
            // Paused/menu code owns the cursor there. Restore only during active gameplay takeover.
            if (wasCaptured && Application.isFocused && !InteriorMath.Paused && Time.timeScale > 0)
            { UnityEngine.Cursor.lockState = savedCursorLock; UnityEngine.Cursor.visible = savedCursorVisible; }
        }
        private void Detach() { if (raw != null) { raw.Dispose(); raw = null; } }
        internal void Shutdown()
        {
            if (stopped) return;
            stopped = true;
            try { StopCapture(); }
            finally { Detach(); Instance = null; }
        }
        private void OnDestroy() { Shutdown(); }

        private static IEnumerable<SpriteRenderer> Sprites(Grabber grabber)
        {
            yield return grabber.idleSprite; yield return grabber.interactibleSprite;
            yield return grabber.handIndicator ? grabber.handIndicator.GetComponent<SpriteRenderer>() : null;
            yield return grabber.idleInteractibleSprite; yield return grabber.idleHeldSprite; yield return grabber.interactibleHeldSprite;
            yield return grabber.forceDirectionSprite;
        }
        private void TintOriginal(Grabber grabber)
        {
            foreach (var sprite in Sprites(grabber))
                if (sprite) { originalColors[sprite] = sprite.color; originalRenderers[sprite] = sprite.enabled; originalActive[sprite.gameObject] = sprite.gameObject.activeSelf; sprite.color = right.Color; }
        }
        private static void HideIndicators(Grabber grabber)
        {
            if (!grabber) return;
            var held = grabber.handIndicator ? grabber.handIndicator.GetComponent<SpriteRenderer>() : null;
            foreach (var sprite in Sprites(grabber)) if (sprite && sprite != held) sprite.enabled = false;
            if (grabber.handIndicator) grabber.handIndicator.gameObject.SetActive(false);
            if (grabber.ghostIndicator) grabber.ghostIndicator.gameObject.SetActive(false);
            if (grabber.mouseGhostIndicator) grabber.mouseGhostIndicator.gameObject.SetActive(false);
        }
        private void CopySettings(Grabber from, Grabber to)
        {
            // Copy public tuning values, then create renderer-only visuals. No GameObject.Instantiate
            // of the grabber/player hierarchy and no copied scripts, colliders or UnityEvent listeners.
            to.viewCamera = from.viewCamera; to.lockHardwareCursor = true;
            to.cursorSensitivity = from.cursorSensitivity; to.screenLimitScale = from.screenLimitScale;
            to.heldRotationFromVelocity = from.heldRotationFromVelocity; to.heldRotationSmoothing = from.heldRotationSmoothing;
            to.handMagnetSmoothing = from.handMagnetSmoothing; to.indicatorDepth = from.indicatorDepth;
            to.intermediatePoses = from.intermediatePoses; to.beforeIntermediate = from.beforeIntermediate; to.afterIntermediate = from.afterIntermediate;
            to.forceDirectionMinDrag = from.forceDirectionMinDrag; to.forceDirectionMaxDrag = from.forceDirectionMaxDrag;
            to.idleSprite = CloneSprite(from.idleSprite); to.interactibleSprite = CloneSprite(from.interactibleSprite);
            var held = CloneSprite(from.handIndicator ? from.handIndicator.GetComponent<SpriteRenderer>() : null);
            to.handIndicator = held ? held.transform : null;
            if (held) { held.enabled = true; held.gameObject.SetActive(false); }
            to.idleInteractibleSprite = CloneSprite(from.idleInteractibleSprite); to.idleHeldSprite = CloneSprite(from.idleHeldSprite);
            to.interactibleHeldSprite = CloneSprite(from.interactibleHeldSprite); to.forceDirectionSprite = CloneSprite(from.forceDirectionSprite);
        }
        private SpriteRenderer CloneSprite(SpriteRenderer source)
        {
            if (!source) return null;
            var go = new GameObject("WholeGrip L " + source.name); go.layer = source.gameObject.layer;
            go.transform.SetParent(secondary.transform, false);
            go.transform.position = source.transform.position; go.transform.rotation = source.transform.rotation;
            go.transform.localScale = source.transform.lossyScale;
            // Compensate for parent scale: renderer transforms must match the original in world space.
            var scale = secondary.transform.lossyScale;
            go.transform.localScale = new Vector3(source.transform.lossyScale.x / scale.x, source.transform.lossyScale.y / scale.y, source.transform.lossyScale.z / scale.z);
            var sprite = go.AddComponent<SpriteRenderer>(); sprite.sprite = source.sprite;
            sprite.sharedMaterial = source.sharedMaterial; sprite.sortingLayerID = source.sortingLayerID;
            sprite.sortingOrder = source.sortingOrder + 1; sprite.color = left.Color;
            sprite.flipX = !source.flipX; sprite.flipY = source.flipY; sprite.enabled = false;
            return sprite;
        }
        private string HandStatus(HandState hand)
        {
            if (raw == null || !raw.Connected(hand.Device))
                return hand.Name + ": click " + (hand == right ? "your usual mouse" : "the other mouse");
            if (hand.Grabber && hand.Grabber.Held) return hand.Name + ": " + hand.Grabber.Held.name;
            return hand.Name + (hand.SeenInput ? ": receiving input" : ": move mouse to check input");
        }
        private void OnGUI()
        {
            if (stopped || context == null || !Application.isFocused) return;
            Color previous = GUI.color;
            GUI.color = faulted || raw == null ? new Color(1f, 0.65f, 0.3f) : Color.white;
            string message = captured && !faulted ? HandStatus(right) + " | " + HandStatus(left) : status;
            GUI.Label(new Rect(12, 12, Screen.width - 24, 28), "Whole Grip: " + message);
            GUI.color = previous;
        }
    }
}
