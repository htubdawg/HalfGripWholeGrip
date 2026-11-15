using System;
using System.Collections.Generic;

namespace WholeGrip
{
    // No Unity types: the same queue and transition logic is exercised by the console checks.
    internal struct MousePacket
    {
        public string Device;
        public int X, Y;
        public ushort Flags;
        public MousePacket(string device, int x, int y, ushort flags)
        { Device = device; X = x; Y = y; Flags = flags; }
    }

    internal struct MouseStep
    {
        public long X, Y;
        public int Held, Down, Up;
        public MouseStep(long x, long y, int held, int down = 0, int up = 0)
        { X = x; Y = y; Held = held; Down = down; Up = up; }
    }

    internal sealed class DeviceFrame
    {
        public readonly List<MouseStep> Steps = new List<MouseStep>();
        public int Held, Down, Up;
        public long X, Y;
        public int PrimaryDownOrder;
    }

    internal sealed class MouseFrames
    {
        private readonly object gate = new object();
        private readonly List<MousePacket> pending = new List<MousePacket>();
        private readonly Dictionary<string, int> held = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool overflow;
        public void Enqueue(MousePacket packet)
        {
            lock (gate)
            {
                // Overflow invalidates the entire frame. Never silently drop a button-up.
                if (pending.Count >= 8192) { pending.Clear(); overflow = true; }
                if (!overflow) pending.Add(packet);
            }
        }
        public void Forget(string device)
        {
            lock (gate)
            {
                held.Remove(device);
                pending.RemoveAll(packet => string.Equals(packet.Device, device, StringComparison.OrdinalIgnoreCase));
            }
        }
        public void Clear()
        {
            lock (gate) { pending.Clear(); held.Clear(); overflow = false; }
        }
        public Dictionary<string, DeviceFrame> Snapshot(out bool lost)
        {
            lock (gate)
            {
                lost = overflow;
                overflow = false;
                if (lost) { held.Clear(); pending.Clear(); }
                var frames = new Dictionary<string, DeviceFrame>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in held) frames[pair.Key] = new DeviceFrame { Held = pair.Value };
                int primaryDownOrder = 0;
                foreach (var packet in pending)
                {
                    if (!frames.TryGetValue(packet.Device, out var frame))
                        frames[packet.Device] = frame = new DeviceFrame();
                    frame.X += packet.X; frame.Y += packet.Y;
                    // Coalesce consecutive motion; retain every button boundary, including down/up/down.
                    if (packet.X != 0 || packet.Y != 0)
                    {
                        int last = frame.Steps.Count - 1;
                        if (last >= 0 && frame.Steps[last].Down == 0 && frame.Steps[last].Up == 0)
                        {
                            var step = frame.Steps[last]; step.X += packet.X; step.Y += packet.Y;
                            frame.Steps[last] = step;
                        }
                        else frame.Steps.Add(new MouseStep(packet.X, packet.Y, frame.Held));
                    }
                    for (int button = 0; button < 5; button++)
                    {
                        int mask = 1 << button;
                        if ((packet.Flags & (1 << (button * 2))) != 0 && (frame.Held & mask) == 0)
                        {
                            frame.Held |= mask; frame.Down |= mask;
                            if (button == 0) frame.PrimaryDownOrder = ++primaryDownOrder;
                            frame.Steps.Add(new MouseStep(0, 0, frame.Held, mask));
                        }
                        if ((packet.Flags & (2 << (button * 2))) != 0 && (frame.Held & mask) != 0)
                        {
                            frame.Held &= ~mask; frame.Up |= mask;
                            frame.Steps.Add(new MouseStep(0, 0, frame.Held, 0, mask));
                        }
                    }
                    held[packet.Device] = frame.Held;
                }
                pending.Clear();
                foreach (var frame in frames.Values)
                    if (frame.Steps.Count == 0) frame.Steps.Add(new MouseStep(0, 0, frame.Held));
                return frames;
            }
        }
    }
}
