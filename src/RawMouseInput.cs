using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WholeGrip
{
    // Reads WM_INPUT on Unity's own receiver. Never uses NOLEGACY, INPUTSINK or a helper window.
    internal sealed class RawMouseInput : IDisposable
    {
        internal sealed class Device
        {
            public IntPtr Handle;
            public string Path;
            public uint Buttons, SampleRate;
        }
        private const uint MouseType = 0, InputCommand = 0x10000003, DeviceName = 0x20000007,
            DeviceInfo = 0x2000000b, Remove = 1, DevNotify = 0x2000;
        private const uint WmInput = 0xff;
        private readonly MouseFrames frames;
        private readonly Action<string> log;
        private readonly Dictionary<IntPtr, Device> devices = new Dictionary<IntPtr, Device>();
        // Root delegates for as long as a subclass is installed (including removal failure).
        private static readonly HashSet<RawMouseInput> roots = new HashSet<RawMouseInput>();
        private readonly SubclassProc callback;
        private readonly UIntPtr subclassId = new UIntPtr(0x57475250);
        private IntPtr window;
        private bool registered, hooked;
        public bool AcceptInput;
        public bool Healthy { get; private set; } = true;
        public string Status { get; private set; } = "not attached";
        public IEnumerable<Device> Devices => devices.Values;

        public RawMouseInput(MouseFrames frames, Action<string> log)
        { this.frames = frames; this.log = log; callback = WindowProc; }

        public void Attach()
        {
            RefreshDevices();
            // Registration is process-wide. Honor Unity's receiver and flags if it already registered mice.
            var current = MouseRegistration();
            IntPtr foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out uint process);
            if (process != GetCurrentProcessId()) throw new InvalidOperationException("Focus the game before enabling Whole Grip.");
            window = current.HasValue && current.Value.Target != IntPtr.Zero ? current.Value.Target : foreground;
            uint thread = GetWindowThreadProcessId(window, out process);
            if (process != GetCurrentProcessId() || thread != GetCurrentThreadId())
                throw new InvalidOperationException("Unity Raw Input receiver is not on this Unity thread; refusing to replace its registration.");
            if (!SetWindowSubclass(window, callback, subclassId, UIntPtr.Zero)) throw new Win32Exception();
            hooked = true; roots.Add(this);
            try
            {
                if (!current.HasValue)
                {
                    var registration = new Registration { Page = 1, Usage = 2, Flags = DevNotify, Target = window };
                    if (!RegisterRawInputDevices(new[] { registration }, 1, (uint)Marshal.SizeOf<Registration>())) throw new Win32Exception();
                    registered = true;
                    Status = "registered foreground mouse input on Unity window";
                }
                else Status = "cooperating with Unity registration (flags 0x" + current.Value.Flags.ToString("X") + ")";
                log(Status + "; receiver=0x" + window.ToInt64().ToString("X"));
            }
            catch { Dispose(); throw; }
        }

        public void RefreshDevices()
        {
            uint count = 0, size = (uint)Marshal.SizeOf<DeviceList>();
            if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue) throw new Win32Exception();
            var list = new DeviceList[count];
            uint read = GetRawInputDeviceList(list, ref count, size);
            if (read == uint.MaxValue) throw new Win32Exception();
            var found = new Dictionary<IntPtr, Device>();
            for (int i = 0; i < read; i++)
            {
                if (list[i].Type != MouseType) continue;
                IntPtr handle = list[i].Handle;
                uint length = 0;
                if (GetRawInputDeviceInfoSize(handle, DeviceName, IntPtr.Zero, ref length) == uint.MaxValue || length == 0) continue;
                var path = new StringBuilder((int)length + 1);
                if (GetRawInputDeviceInfoName(handle, DeviceName, path, ref length) == uint.MaxValue) continue;
                var info = new Info { Size = (uint)Marshal.SizeOf<Info>() };
                uint infoSize = info.Size;
                if (GetRawInputDeviceInfoMouse(handle, DeviceInfo, ref info, ref infoSize) == uint.MaxValue) continue;
                found[handle] = new Device { Handle = handle, Path = path.ToString(), Buttons = info.Buttons, SampleRate = info.SampleRate };
            }
            foreach (var pair in devices)
                if (!found.ContainsKey(pair.Key)) frames.Forget(pair.Value.Path);
            devices.Clear();
            foreach (var pair in found) devices.Add(pair.Key, pair.Value);
        }

        public bool Connected(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            foreach (var device in devices.Values)
                if (string.Equals(device.Path, path, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Poll registration too: if Unity changes receivers, do not silently hijack it back.
        public void VerifyReceiver()
        {
            var registration = MouseRegistration();
            if (!registration.HasValue || (registration.Value.Target != IntPtr.Zero && registration.Value.Target != window))
            { Healthy = false; Status = "Unity changed Raw Input receiver; disable and re-enable Whole Grip"; }
        }

        private IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
        {
            try
            {
                if (message == 0x0008 || message == 0x0215) // focus/capture lost
                { AcceptInput = false; frames.Clear(); }
                if (message == 0x0082) // WM_NCDESTROY; comctl removes the subclass when the window dies
                { Healthy = false; AcceptInput = false; hooked = false; roots.Remove(this); frames.Clear(); }
                if (message == 0x00fe && wParam.ToUInt64() == 2 && devices.TryGetValue(lParam, out var removed))
                { devices.Remove(lParam); frames.Forget(removed.Path); }
                if (message == WmInput && AcceptInput)
                {
                    uint bytes = 0, headerSize = (uint)Marshal.SizeOf<Header>();
                    if (GetRawInputData(lParam, InputCommand, IntPtr.Zero, ref bytes, headerSize) == uint.MaxValue)
                        throw new Win32Exception();
                    if (bytes < headerSize + 24 || bytes > 65536) throw new InvalidOperationException("Invalid RAWINPUT packet size.");
                    IntPtr buffer = Marshal.AllocHGlobal((int)bytes);
                    try
                    {
                        uint read = GetRawInputData(lParam, InputCommand, buffer, ref bytes, headerSize);
                        if (read == uint.MaxValue || read < headerSize + 24) throw new Win32Exception();
                        var header = Marshal.PtrToStructure<Header>(buffer);
                        if (header.Type == MouseType && devices.TryGetValue(header.Device, out var device))
                        {
                            var mouse = Marshal.PtrToStructure<Mouse>(IntPtr.Add(buffer, (int)headerSize));
                            if ((mouse.Flags & 1) != 0)
                                throw new InvalidOperationException("Absolute-position mouse packets are unsupported; assign two relative mice.");
                            frames.Enqueue(new MousePacket(device.Path, mouse.X, mouse.Y, mouse.ButtonFlags));
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            catch (Exception e)
            { AcceptInput = false; Healthy = false; Status = e.Message; frames.Clear(); }
            // Original Unity processing must run even for foreground WM_INPUT cleanup or reader failure.
            return DefSubclassProc(hwnd, message, wParam, lParam);
        }

        private static Registration? MouseRegistration()
        {
            uint count = 0, size = (uint)Marshal.SizeOf<Registration>();
            if (GetRegisteredRawInputDevices(null, ref count, size) == uint.MaxValue) throw new Win32Exception();
            var list = new Registration[count];
            uint read = GetRegisteredRawInputDevices(list, ref count, size);
            if (read == uint.MaxValue) throw new Win32Exception();
            foreach (var item in list)
            {
                // Page-only/exclusion registrations need a more careful integration, so fail closed.
                if (item.Page == 1 && ((item.Flags & 0x30) == 0x20 || (item.Flags & 0x30) == 0x10))
                    throw new InvalidOperationException("Unity uses page/exclusion Raw Input registration; unsupported by this prototype.");
                if (item.Page == 1 && item.Usage == 2) return item;
            }
            return null;
        }

        public void Dispose()
        {
            AcceptInput = false; frames.Clear();
            if (registered)
            {
                try
                {
                    var current = MouseRegistration();
                    if (current.HasValue && current.Value.Target == window && current.Value.Flags == DevNotify)
                    {
                        var remove = new Registration { Page = 1, Usage = 2, Flags = Remove };
                        if (!RegisterRawInputDevices(new[] { remove }, 1, (uint)Marshal.SizeOf<Registration>()))
                            log("Failed to remove Whole Grip Raw Input registration: " + Marshal.GetLastWin32Error());
                    }
                }
                catch (Exception e) { log("Registration cleanup: " + e.Message); }
                registered = false;
            }
            if (hooked)
            {
                if (RemoveWindowSubclass(window, callback, subclassId)) { hooked = false; roots.Remove(this); }
                else log("Window subclass removal failed; callback retained and inert until window destruction.");
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct DeviceList { public IntPtr Handle; public uint Type; }
        [StructLayout(LayoutKind.Sequential)] private struct Registration { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public IntPtr Device; public UIntPtr Param; }
        // RAWMOUSE's button union is DWORD-aligned, even though usFlags is a USHORT.
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct Mouse
        {
            [FieldOffset(0)] public ushort Flags;
            [FieldOffset(4)] public ushort ButtonFlags;
            [FieldOffset(6)] public ushort ButtonData;
            [FieldOffset(12)] public int X;
            [FieldOffset(16)] public int Y;
        }
        // RID_DEVICE_INFO's union has 24 bytes (HID is largest), total size 32.
        [StructLayout(LayoutKind.Explicit, Size = 32)] private struct Info
        {
            [FieldOffset(0)] public uint Size;
            [FieldOffset(4)] public uint Type;
            [FieldOffset(12)] public uint Buttons;
            [FieldOffset(16)] public uint SampleRate;
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList([Out] DeviceList[] list, ref uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRegisteredRawInputDevices([Out] Registration[] list, ref uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Registration[] list, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfoSize(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfoName(IntPtr device, uint command, StringBuilder data, ref uint size);
        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)] private static extern uint GetRawInputDeviceInfoMouse(IntPtr device, uint command, ref Info data, ref uint size);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll", SetLastError = true)] private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
        [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam);
    }
}
