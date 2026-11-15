using UnityEngine;

namespace WholeGrip
{
    // Geometry/callback adapter only. It never reads GameInput.Hand or Unity mouse input, and
    // never calls Grabber.Update/UpdateCursor. The shared camera is restored in a finally block.
    internal sealed class VanillaHandInteraction : IHandInteraction
    {
        private readonly DualHandController controller;
        private readonly HandState hand;
        private Grabber Grabber => hand.Grabber;
        private Ray ray;
        private bool hoverReady, lookingBack, lookBackFromHold;
        private Vector2 lookBackCursor;
        public VanillaHandInteraction(DualHandController controller, HandState hand)
        { this.controller = controller; this.hand = hand; }
        public bool HasHold => Grabber && Grabber.Held;
        public bool HoldIsUsable => HasHold && Grabber.Held.isActiveAndEnabled && Grabber.Held.IsGrabbed;
        public void CancelHold() { Patches.Release(Grabber); hoverReady = false; }
        public void Move(long x, long y)
        {
            var g = Grabber;
            bool back = GameInput.GetKey(KeyCode.Alpha1) || GameInput.GetKey(KeyCode.Alpha3);
            if (back && !lookingBack) { lookBackFromHold = HasHold; lookBackCursor = g.cursor; }
            lookingBack = back;
            bool begun = Patches.BeginLook(g);
            try
            {
                controller.MoveCursor(hand, x, y);
                ray = g.viewCamera.ScreenPointToRay(g.cursor);
            }
            finally { Patches.EndLook(g, begun); }
            if (begun && HasHold && g.Held.RaycastMovementPlane(ray, out var point))
                g.cursor = g.viewCamera.WorldToScreenPoint(g.Held.PlaneToWorld(point));
            hoverReady = false;
        }
        public void Hover()
        {
            if (HasHold || hoverReady) return;
            Patches.Hover(Grabber, ray); hoverReady = true;
        }
        public void Grab(bool allowClick)
        {
            if (!controller.CanInteract(Grabber, false)) return;
            Patches.Interact(Grabber, allowClick);
            if (HasHold)
            {
                // Native grab snaps its cursor to the grip. Rebuild the ray at that snapped cursor.
                ray = Grabber.viewCamera.ScreenPointToRay(Grabber.cursor);
                hoverReady = false;
            }
        }
        public void ReleasePrimary()
        {
            var g = Grabber;
            if (!g.Held) return;
            var target = g.Held;
            Vector2 released = Vector2.Lerp(g.viewCamera.WorldToScreenPoint(target.GripWorldPoint()),
                g.cursor, Mathf.Clamp01(target.releaseCursorBlend));
            if (!target.Release()) return;
            if (lookingBack && lookBackFromHold) { released = lookBackCursor; lookBackFromHold = false; }
            Patches.ClearHeld(g);
            g.cursor = released;
            ray = g.viewCamera.ScreenPointToRay(g.cursor);
            hoverReady = false;
        }
        public void Secondary()
        {
            if (controller.CanInteract(Grabber, true)) Patches.Secondary(Grabber);
        }
        public void UpdateHeld()
        {
            if (HasHold) Patches.MoveHeld(Grabber, ray);
        }
    }
}
