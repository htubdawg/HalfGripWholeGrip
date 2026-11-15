namespace WholeGrip
{
    // The production scheduler has no Unity/global mouse dependency. Each hand owns one instance.
    internal interface IHandInteraction
    {
        bool HasHold { get; }
        bool HoldIsUsable { get; }
        void CancelHold();
        void Move(long x, long y);
        void Hover();
        void Grab(bool allowClick);
        void ReleasePrimary();
        void Secondary();
        void UpdateHeld();
    }

    internal sealed class HandDriver
    {
        private bool heldLastFrame, suppressSweep;
        public void Reset() { heldLastFrame = suppressSweep = false; }
        public void Step(DeviceFrame frame, IHandInteraction hand)
        {
            if (hand.HasHold && !hand.HoldIsUsable) hand.CancelHold();
            if (heldLastFrame && !hand.HasHold && (frame.Held & 1) != 0) suppressSweep = true;
            // Integrate the complete device delta exactly once, independent of polling rate and
            // the number of clicks. Never replay a full game Update for raw movement samples.
            hand.Move(frame.X, frame.Y);
            hand.Hover();
            foreach (var edge in frame.Steps)
            {
                if (((edge.Down | edge.Up) & 1) != 0) suppressSweep = false;
                if ((edge.Down & 1) != 0 && !hand.HasHold)
                { hand.Hover(); hand.Grab(true); }
                if ((edge.Up & 1) != 0 && hand.HasHold)
                { hand.ReleasePrimary(); hand.Hover(); }
                if ((edge.Down & 2) != 0) { hand.Hover(); hand.Secondary(); }
            }
            // Also handles a disconnected/replaced handle without a delivered release packet.
            if (hand.HasHold && (frame.Held & 1) == 0 && (frame.Up & 1) == 0) hand.ReleasePrimary();
            if (!hand.HasHold && (frame.Held & 1) != 0 && !suppressSweep)
            { hand.Hover(); hand.Grab(false); }
            if (hand.HasHold) hand.UpdateHeld();
            heldLastFrame = hand.HasHold;
        }
    }
}
