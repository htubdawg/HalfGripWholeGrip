using System;
using System.Collections.Generic;
using WholeGrip;

internal static class TestHands
{
    private sealed class Hand : IHandInteraction
    {
        private readonly Dictionary<string, Hand> owners;
        private readonly string target;
        public bool HasHold { get; private set; }
        public bool HoldIsUsable { get; set; } = true;
        public int Moves, Grabs, Releases, HeldUpdates, SecondaryClicks;
        public long X, Y;
        public Hand(Dictionary<string, Hand> owners, string target) { this.owners = owners; this.target = target; }
        public void CancelHold() => ReleasePrimary();
        public void Move(long x, long y) { Moves++; X += x; Y += y; }
        public void Hover() { }
        public void Grab(bool click)
        {
            if (owners.TryGetValue(target, out var owner) && owner != this) return;
            if (!HasHold) { owners[target] = this; HasHold = true; Grabs++; }
        }
        public void ReleasePrimary()
        {
            if (!HasHold) return;
            HasHold = false; owners.Remove(target); Releases++;
        }
        public void Secondary() { if (!owners.TryGetValue(target, out var owner) || owner == this) SecondaryClicks++; }
        public void UpdateHeld() { HeldUpdates++; }
    }
    public static void Run(Action<bool, string> check)
    {
        // Use production frames AND production scheduling, rather than just checking the queue.
        foreach (int samples in new[] { 1, 4, 8, 64, 500 })
        {
            var queue = new MouseFrames();
            var owners = new Dictionary<string, Hand>();
            var a = new Hand(owners, "wheel"); var b = new Hand(owners, "shifter");
            var da = new HandDriver(); var db = new HandDriver();
            queue.Enqueue(new MousePacket("A", 0, 0, 1));
            queue.Enqueue(new MousePacket("B", 0, 0, 1));
            var snapshot = queue.Snapshot(out _); da.Step(snapshot["A"], a); db.Step(snapshot["B"], b);
            check(a.HasHold && b.HasHold && owners.Count == 2, "Both hands must grab in the same frame");
            for (int frame = 0; frame < 200; frame++)
            {
                for (int sample = 0; sample < samples; sample++)
                {
                    queue.Enqueue(new MousePacket("A", 2, -1, 0));
                    queue.Enqueue(new MousePacket("B", -3, 4, 0));
                }
                // Repeated B release/down while A continuously holds and moves, plus full right clicks.
                queue.Enqueue(new MousePacket("B", 0, 0, 2));
                queue.Enqueue(new MousePacket("B", 0, 0, 1));
                queue.Enqueue(new MousePacket("A", 0, 0, 12));
                queue.Enqueue(new MousePacket("B", 0, 0, 12));
                snapshot = queue.Snapshot(out bool lost);
                check(!lost, "High-rate input overflowed unexpectedly");
                da.Step(snapshot["A"], a); db.Step(snapshot["B"], b);
                check(a.HasHold && b.HasHold && a.Releases == 0, "B click interrupted A hold");
                check(a.Moves == frame + 2 && b.Moves == frame + 2, "Movement repeated based on packet/edge count");
                check(a.HeldUpdates == frame + 2 && b.HeldUpdates == frame + 2, "Held movement updated more than once per Unity frame");
            }
            check(a.X == 400L * samples && a.Y == -200L * samples && b.X == -600L * samples && b.Y == 800L * samples,
                "One hand lost or received the other hand's motion");
            check(a.SecondaryClicks == 200 && b.SecondaryClicks == 200 && b.Grabs == 201 && b.Releases == 200,
                "Ordered button events were lost while both hands moved");
            queue.Enqueue(new MousePacket("A", 0, 0, 2));
            snapshot = queue.Snapshot(out _); da.Step(snapshot["A"], a); db.Step(snapshot["B"], b);
            check(!a.HasHold && b.HasHold && owners.Count == 1, "Releasing A released B");
            queue.Enqueue(new MousePacket("A", 0, 0, 3)); // Complete A click while B holds.
            snapshot = queue.Snapshot(out _); da.Step(snapshot["A"], a); db.Step(snapshot["B"], b);
            check(a.Grabs == 2 && a.Releases == 2 && !a.HasHold && b.HasHold, "Complete click was lost or changed the other hand");
            queue.Enqueue(new MousePacket("B", 0, 0, 2));
            snapshot = queue.Snapshot(out _); da.Step(snapshot["A"], a); db.Step(snapshot["B"], b);
            check(!b.HasHold && owners.Count == 0, "Final hand release left stale ownership");
        }
        var same = new Dictionary<string, Hand>();
        var first = new Hand(same, "wheel"); var second = new Hand(same, "wheel");
        var d1 = new HandDriver(); var d2 = new HandDriver();
        var down = new DeviceFrame { Held = 1, Down = 1 };
        down.Steps.Add(new MouseStep(0, 0, 1, 1));
        d1.Step(down, first); d2.Step(down, second);
        check(first.HasHold && !second.HasHold && same.Count == 1, "Competing grabs allowed two owners");
        var release = new DeviceFrame { Up = 1 }; release.Steps.Add(new MouseStep(0, 0, 0, 0, 1));
        d2.Step(release, second);
        check(first.HasHold && same.Count == 1, "Rejected hand released the owner's hold");
        d1.Step(release, first); d2.Step(down, second);
        check(second.HasHold && !first.HasHold, "Ownership did not transfer after release");
    }
}
