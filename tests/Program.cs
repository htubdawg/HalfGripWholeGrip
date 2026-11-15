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
        Console.WriteLine("PASS: production MouseFrames transition, isolation, overflow, clear and concurrency checks (" + assertions + " assertions).");
    }
}
