using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using WholeGrip;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    private static Dictionary<string, DeviceFrame> Take(MouseFrames queue)
    { var frame = queue.Snapshot(out bool lost); Check(!lost, "Unexpected overflow"); return frame; }
    public static void Main()
    {
        var rawType = typeof(RawMouseInput);
        Type Layout(string name) => rawType.GetNestedType(name, BindingFlags.NonPublic);
        Check(Marshal.SizeOf(Layout("Mouse")) == 24 && Marshal.OffsetOf(Layout("Mouse"), "ButtonFlags").ToInt32() == 4 &&
            Marshal.OffsetOf(Layout("Mouse"), "X").ToInt32() == 12, "RAWMOUSE does not match Windows ABI");
        Check(Marshal.SizeOf(Layout("Header")) == (IntPtr.Size == 8 ? 24 : 16), "RAWINPUTHEADER ABI mismatch");
        Check(Marshal.SizeOf(Layout("Registration")) == (IntPtr.Size == 8 ? 16 : 12), "RAWINPUTDEVICE ABI mismatch");
        Check(Marshal.SizeOf(Layout("Info")) == 32 && Marshal.OffsetOf(Layout("Info"), "Buttons").ToInt32() == 12,
            "RID_DEVICE_INFO ABI mismatch");
        var queue = new MouseFrames();
        queue.Enqueue(new MousePacket("A", 8, -3, 1));
        queue.Enqueue(new MousePacket("B", -7, 4, 4));
        var frame = Take(queue);
        Check(frame["A"].Held == 1 && frame["B"].Held == 2, "Device buttons crossed");
        Check(frame["A"].X == 8 && frame["B"].X == -7, "Device deltas crossed");
        queue.Enqueue(new MousePacket("A", 0, 0, 2));
        frame = Take(queue);
        Check(frame["A"].Held == 0 && frame["A"].Up == 1, "A did not release");
        Check(frame["B"].Held == 2 && frame["B"].Up == 0, "A released B");
        queue.Clear();
        queue.Enqueue(new MousePacket("A", 0, 0, 1));
        queue.Enqueue(new MousePacket("A", 4, 5, 0));
        queue.Enqueue(new MousePacket("A", 0, 0, 2));
        queue.Enqueue(new MousePacket("A", 0, 0, 1));
        frame = Take(queue);
        var steps = frame["A"].Steps;
        Check(steps.Count == 4 && steps[0].Down == 1 && steps[1].Held == 1 && steps[1].X == 4 &&
            steps[2].Up == 1 && steps[3].Down == 1 && frame["A"].Held == 1, "Subframe transition ordering lost");
        queue.Clear();
        queue.Enqueue(new MousePacket("A", 0, 0, 3));
        frame = Take(queue);
        Check(frame["A"].Steps.Count == 2 && frame["A"].Down == 1 && frame["A"].Up == 1 && frame["A"].Held == 0,
            "Whole click in one packet/frame was lost");
        Check(Take(queue)["A"].Down == 0, "Snapshot replayed press");
        queue.Clear();
        queue.Enqueue(new MousePacket("A", 0, 0, 1));
        queue.Enqueue(new MousePacket("B", 0, 0, 1));
        queue.Enqueue(new MousePacket("A", 0, 0, 2));
        queue.Enqueue(new MousePacket("A", 0, 0, 1));
        frame = Take(queue);
        Check(frame["A"].PrimaryDownOrder > frame["B"].PrimaryDownOrder, "Latest menu mouse ordering lost");
        queue.Enqueue(new MousePacket("A", 100, 1, 0));
        queue.Enqueue(new MousePacket("B", 50, 0, 0));
        queue.Forget("a");
        frame = Take(queue);
        Check(!frame.ContainsKey("A") && frame["B"].Held == 1 && frame["B"].X == 50,
            "Disconnect failed to clear only the affected mouse");
        queue.Clear();
        queue.Enqueue(new MousePacket("A", int.MaxValue, 0, 0));
        queue.Enqueue(new MousePacket("a", int.MaxValue, 0, 0));
        frame = Take(queue);
        Check(frame.Count == 1 && frame["A"].X == (long)int.MaxValue * 2 && frame["A"].Steps.Count == 1,
            "Motion coalescing overflow/case identity");
        queue.Clear();
        Check(Take(queue).Count == 0, "Clear retained input");
        for (int i = 0; i < 8193; i++) queue.Enqueue(new MousePacket("A", 1, 0, 1));
        frame = queue.Snapshot(out bool lost);
        Check(lost && frame.Count == 0, "Overflow must invalidate button state");
        queue.Enqueue(new MousePacket("B", 1, 0, 1));
        Check(Take(queue)["B"].Down == 1, "Queue failed to recover after overflow");
        queue.Clear();
        // Producer and frame consumer race on the actual production queue. Sum must be exact.
        var writer = Task.Run(() => { for (int i = 0; i < 4000; i++) queue.Enqueue(new MousePacket("A", 1, -1, 0)); });
        long total = 0;
        while (!writer.IsCompleted) foreach (var device in Take(queue).Values) total += device.X;
        writer.GetAwaiter().GetResult();
        foreach (var device in Take(queue).Values) total += device.X;
        Check(total == 4000, "Concurrent snapshot lost or replayed movement");
        // Exercise the production Win32 decoder with packets from one shared receiver window.
        bool Decode(uint type, int payloadBytes, int device, ushort buttons, int x, int y,
            out RawMouseInput.MouseReport report)
        {
            int headerBytes = IntPtr.Size == 8 ? 24 : 16;
            int length = headerBytes + payloadBytes;
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.Copy(new byte[length], 0, buffer, length);
                Marshal.WriteInt32(buffer, 0, (int)type);
                Marshal.WriteInt32(buffer, 4, length);
                Marshal.WriteIntPtr(buffer, 8, new IntPtr(device));
                if (type == 0 && payloadBytes >= 24)
                {
                    Marshal.WriteInt16(buffer, headerBytes + 4, (short)buttons);
                    Marshal.WriteInt32(buffer, headerBytes + 12, x);
                    Marshal.WriteInt32(buffer, headerBytes + 16, y);
                }
                return RawMouseInput.DecodeMousePacket(buffer, (uint)length, out report);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        void Feed(uint type, int payload, int device, ushort buttons = 0, int x = 0, int y = 0)
        {
            if (Decode(type, payload, device, buttons, x, y, out var report))
                queue.Enqueue(new MousePacket(report.Device.ToInt64() == 1 ? "A" : "B", report.X, report.Y, report.ButtonFlags));
        }
        queue.Clear();
        Check(!Decode(1, 16, 9, 0, 0, 0, out _), "Keyboard packet must be ignored, not rejected as a short mouse");
        Check(!Decode(2, 8, 9, 0, 0, 0, out _), "HID packet must not reset mouse input");
        bool rejected = false;
        try { Decode(0, 16, 1, 0, 0, 0, out _); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Actual truncated mouse packet was accepted");
        Feed(0, 24, 1, 1); // A holds wheel throughout, B clicks while A moves.
        Take(queue);
        for (int i = 0; i < 500; i++)
        {
            Feed(0, 24, 1, 0, 12, -3);
            Feed(1, 16, 9); // keyboard through Unity's shared receiver, e.g. throttle/look
            Feed(0, 24, 2, 1);
            Feed(0, 24, 1, 0, -4, 2);
            frame = Take(queue);
            Check(frame["A"].Held == 1 && frame["A"].X == 8 && frame["B"].Down == 1 && frame["B"].Held == 1,
                "Moving A or keyboard input blocked B's press");
            Feed(0, 24, 2, 2);
            Feed(0, 24, 1, 0, 1, 1);
            frame = Take(queue);
            Check(frame["A"].Held == 1 && frame["B"].Up == 1 && frame["B"].Held == 0,
                "Releasing B interrupted A's continuous hold");
        }
        queue.Clear();
        Feed(0, 24, 2, 1); // Repeat the opposite direction.
        Take(queue);
        for (int i = 0; i < 500; i++)
        {
            Feed(0, 24, 2, 0, 3, 9);
            Feed(1, 16, 9);
            Feed(0, 24, 1, 3); // complete A click within one frame
            frame = Take(queue);
            Check(frame["B"].Held == 1 && frame["B"].Y == 9 && frame["A"].Down == 1 && frame["A"].Up == 1,
                "Moving B or keyboard input blocked A's complete click");
        }
        Dictionary<string, DeviceFrame> Clicks(params string[] devices)
        {
            var input = new MouseFrames();
            foreach (string device in devices) input.Enqueue(new MousePacket(device, 0, 0, 3));
            return Take(input);
        }
        var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A", "B", "C" };
        bool Connected(string path) => path != null && connected.Contains(path);
        var assignment = new MouseAssignment { Right = "stale", Left = "B" };
        assignment.ObserveMenu(Clicks("C", "A"));
        assignment.EnterGameplay(Connected);
        Check(assignment.Right == "A" && assignment.Left == "B", "Menu mouse did not override stale right assignment");
        assignment.Left = "";
        assignment.ObserveGameplay(Clicks("A"), Connected);
        Check(assignment.Left == "", "Same mouse assigned to both hands");
        assignment.ObserveGameplay(Clicks("B"), Connected);
        Check(assignment.Right == "A" && assignment.Left == "B", "Ordinary second-mouse click did not assign left");
        assignment.ObserveGameplay(Clicks("C"), Connected);
        Check(assignment.Right == "A" && assignment.Left == "B", "Third device stole an assigned hand");
        connected.Remove("B");
        assignment.ObserveGameplay(Clicks("C"), Connected);
        Check(assignment.Right == "A" && assignment.Left == "C", "Replacement mouse changed the unaffected hand");
        var fresh = new MouseAssignment();
        connected.Add("B");
        fresh.ObserveGameplay(Clicks("B", "A"), Connected);
        Check(fresh.Right == "B" && fresh.Left == "A", "No-menu fallback lost ordered same-frame assignments");
        fresh.Left = "B";
        fresh.MenuMouse = "B";
        fresh.EnterGameplay(Connected);
        Check(fresh.Right != fresh.Left, "Menu selection left duplicate device ownership");
        Console.WriteLine("PASS: production input, automatic assignment, native layout and concurrency checks (" + assertions + " assertions).");
    }
}
