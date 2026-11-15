using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WholeGrip
{
	/// <summary>
	/// Every packet from every mouse, with the device it came from, read on a thread of its own.
	/// <para>
	/// Why it takes the registration: Windows sends a process's raw mouse input to one window only, the one named in the
	/// process's last RegisterRawInputDevices call. Unity registers its own window and drains the packets in bulk
	/// (GetRawInputBuffer) as part of its frame, so nothing else in the process can watch them reliably: 0.2.0 read them as they
	/// left the message queue and, by its own log, saw a small part of them (about 120 a second from two moving mice). A lost
	/// press or release inverts a hand's button for good, which is what "releases almost never work" was.
	/// </para>
	/// <para>
	/// So while both hands are being driven (and only then), this class registers its own message-only window on its own thread
	/// for mouse input, reads every WM_INPUT there, and gives the registration back to whatever Unity had (window, flags) the
	/// moment it is done: pause, menus, popups, focus loss, another camera. Unity's keyboard input is never touched, and Windows'
	/// ordinary mouse messages (cursor, clicks for the menus) keep coming because nothing asks for RIDEV_NOLEGACY.
	/// </para>
	/// <para>
	/// Each registration call is made on the thread that owns its target window: taking on this class's thread, giving back on
	/// Unity's. A timer checks the registration four times a second while taken; if Unity has registered again, its new
	/// registration becomes the one to give back and the mice are taken again (a few times at most, then it gives up and says so).
	/// </para>
	/// </summary>
	internal sealed class RawMice : IDisposable
	{
		private const uint WmInput = 0x00FF, WmInputDeviceChange = 0x00FE, WmTimer = 0x0113, WmClose = 0x0010, WmDestroy = 0x0002;
		private const uint WmTake = 0x8000 + 0x5747; // WM_APP + 'WG'
		private const uint RidInput = 0x10000003, RidiDeviceName = 0x20000007;
		private const uint RidevRemove = 0x1, RidevInputSink = 0x100, RidevDevNotify = 0x2000;
		private const int MaxQueue = 20000, MaxRetakes = 8;
		private static readonly IntPtr MessageParent = new IntPtr(-3); // HWND_MESSAGE
		private static readonly List<RawMice> Stuck = new List<RawMice>();

		private readonly object queueGate = new object(), registrationGate = new object();
		private List<MousePacket> incoming = new List<MousePacket>(256), spare = new List<MousePacket>(256);
		private readonly Action<string> warn;
		private readonly WndProc procedure;
		private readonly string className = "WholeGripRawMice" + Guid.NewGuid().ToString("N");
		private readonly ManualResetEvent ready = new ManualResetEvent(false);
		private Thread thread;
		private IntPtr window, buffer;
		private Registration? original;
		private volatile bool taken, overflowed;
		private int retakes;
		private string startError;

		/// <summary>Set (from the input thread) when the mice could not be taken or kept; read and cleared on the main thread.</summary>
		public string Fault { get; private set; }

		/// <summary>True while this class holds the mouse registration.</summary>
		public bool Taken => taken;

		/// <summary>Packets read since start, for the verbose log.</summary>
		public long Packets;

		public RawMice(Action<string> warn)
		{
			this.warn = warn;
			procedure = Procedure;
		}

		/// <summary>Starts the input thread and its window. Throws with the reason if Windows refuses.</summary>
		public void Start()
		{
			buffer = Marshal.AllocHGlobal(256);
			thread = new Thread(Run) { IsBackground = true, Name = "Whole Grip mice" };
			thread.Start();
			if (!ready.WaitOne(3000)) throw new InvalidOperationException("the mouse thread did not start");
			if (startError != null) throw new InvalidOperationException(startError);
		}

		/// <summary>Takes the mouse registration (main thread). False if Windows refused; <see cref="Fault"/> says why.</summary>
		public bool Take()
		{
			if (window == IntPtr.Zero) return false;
			lock (registrationGate) retakes = 0;
			SendMessageTimeout(window, WmTake, UIntPtr.Zero, IntPtr.Zero, 0x0002 | 0x0001, 1000, out UIntPtr result);
			return taken;
		}

		/// <summary>Gives the mouse registration back to what Unity had (main thread, which owns Unity's window).</summary>
		public void GiveBack()
		{
			lock (registrationGate)
			{
				bool was = taken;
				taken = false;
				if (!was && window == IntPtr.Zero) return;
				try
				{
					var current = Current();
					if (current.HasValue && current.Value.Target != window) return; // already someone else's: leave it
					if (!current.HasValue && !was) return;
					var restore = original ?? new Registration { Page = 1, Usage = 2, Flags = RidevRemove, Target = IntPtr.Zero };
					if (!Register(restore))
					{
						// Unity's own window may be gone (quitting). Removing ours is the next best thing.
						var remove = new Registration { Page = 1, Usage = 2, Flags = RidevRemove, Target = IntPtr.Zero };
						if (!Register(remove)) warn("Could not give the mice back to the game (error " + Marshal.GetLastWin32Error() + ")");
					}
				}
				catch (Exception e) { warn("Giving the mice back: " + e.Message); }
			}
		}

		/// <summary>Moves the packets read since the last call into <paramref name="into"/> (cleared first).</summary>
		public void Drain(List<MousePacket> into, out bool lost)
		{
			into.Clear();
			lock (queueGate)
			{
				var full = incoming;
				incoming = spare;
				spare = full;
				lost = overflowed;
				overflowed = false;
			}
			into.AddRange(spare);
			spare.Clear();
		}

		/// <summary>Takes and clears <see cref="Fault"/>.</summary>
		public string TakeFault()
		{
			string fault = Fault;
			Fault = null;
			return fault;
		}

		public void Dispose()
		{
			GiveBack();
			if (window != IntPtr.Zero) PostMessage(window, WmClose, UIntPtr.Zero, IntPtr.Zero);
			if (thread != null && !thread.Join(2000))
			{
				// Its window may still call the procedure: keep this object (and so the delegate Windows points at) alive.
				lock (Stuck) Stuck.Add(this);
				warn("The mouse thread did not stop; it stays idle until the game closes.");
			}
			else if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
			thread = null;
		}

		// ---- input thread ----

		private void Run()
		{
			try
			{
				var cls = new WindowClass
				{
					Size = (uint)Marshal.SizeOf<WindowClass>(),
					Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
					Instance = GetModuleHandle(null),
					ClassName = className
				};
				if (RegisterClassEx(ref cls) == 0) throw new Win32Exception();
				window = CreateWindowEx(0, className, "Whole Grip", 0, 0, 0, 0, 0, MessageParent, IntPtr.Zero, cls.Instance, IntPtr.Zero);
				if (window == IntPtr.Zero) throw new Win32Exception();
			}
			catch (Exception e)
			{
				startError = "the mouse window could not be made: " + e.Message;
				ready.Set();
				return;
			}
			ready.Set();
			try
			{
				while (GetMessage(out Message message, IntPtr.Zero, 0, 0) > 0)
				{
					TranslateMessage(ref message);
					DispatchMessage(ref message);
				}
			}
			catch (Exception e) { Fault = "The mouse thread stopped: " + e.Message; taken = false; }
			finally
			{
				UnregisterClass(className, GetModuleHandle(null));
				window = IntPtr.Zero;
			}
		}

		private IntPtr Procedure(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam)
		{
			try
			{
				switch (message)
				{
					case WmInput:
						if (taken) Read(lParam);
						break; // DefWindowProc below does Windows' cleanup of the packet
					case WmTake:
						TakeHere();
						return IntPtr.Zero;
					case WmTimer:
						Watch();
						return IntPtr.Zero;
					case WmInputDeviceChange:
						return IntPtr.Zero;
					case WmClose:
						KillTimer(hwnd, (UIntPtr)1);
						DestroyWindow(hwnd);
						return IntPtr.Zero;
					case WmDestroy:
						PostQuitMessage(0);
						return IntPtr.Zero;
				}
			}
			catch (Exception e) { Fault = "Mouse input: " + e.Message; }
			// Never let an exception reach Windows: this runs inside a native callback.
			return DefWindowProc(hwnd, message, wParam, lParam);
		}

		/// <summary>Registers this window for mouse input, keeping what was there to give back later.</summary>
		private void TakeHere()
		{
			lock (registrationGate)
			{
				var current = Current();
				if (!current.HasValue || current.Value.Target != window) original = current;
				var mine = new Registration { Page = 1, Usage = 2, Flags = RidevInputSink | RidevDevNotify, Target = window };
				if (!Register(mine))
				{
					Fault = "Windows refused the mouse registration (error " + Marshal.GetLastWin32Error() + ")";
					taken = false;
					return;
				}
				lock (queueGate) incoming.Clear();
				taken = true;
				SetTimer(window, (UIntPtr)1, 250, IntPtr.Zero);
			}
		}

		/// <summary>While taken: if Unity registered its window again, keep its new registration for later and take the mice back.</summary>
		private void Watch()
		{
			lock (registrationGate)
			{
				if (!taken) { KillTimer(window, (UIntPtr)1); return; }
				var current = Current();
				if (current.HasValue && current.Value.Target == window) return;
				original = current;
				if (++retakes > MaxRetakes)
				{
					taken = false;
					Fault = "The game keeps taking the mice back; Whole Grip stops driving the hands until the next race";
					return;
				}
				var mine = new Registration { Page = 1, Usage = 2, Flags = RidevInputSink | RidevDevNotify, Target = window };
				if (!Register(mine)) { taken = false; Fault = "Windows refused the mouse registration (error " + Marshal.GetLastWin32Error() + ")"; }
			}
		}

		private void Read(IntPtr handle)
		{
			uint size = 256, header = (uint)Marshal.SizeOf<Header>();
			uint read = GetRawInputData(handle, RidInput, buffer, ref size, header);
			if (read == uint.MaxValue || read < header + 24) return;
			if (Marshal.ReadInt32(buffer, 0) != 0) return; // RIM_TYPEMOUSE
			IntPtr device = Marshal.ReadIntPtr(buffer, 8);
			if (device == IntPtr.Zero) return; // injected input (SendInput, remote desktop): no physical mouse to tie it to
			int mouse = (int)header;
			ushort flags = (ushort)Marshal.ReadInt16(buffer, mouse);
			ushort buttons = (ushort)Marshal.ReadInt16(buffer, mouse + 4);
			int x = Marshal.ReadInt32(buffer, mouse + 12), y = Marshal.ReadInt32(buffer, mouse + 16);
			var packet = new MousePacket(device, x, y, buttons, (flags & 1) != 0);
			lock (queueGate)
			{
				if (incoming.Count >= MaxQueue) { incoming.Clear(); overflowed = true; }
				incoming.Add(packet);
			}
			Interlocked.Increment(ref Packets);
		}

		// ---- registration helpers ----

		/// <summary>The process's current registration for mice (usage page 1, usage 2), if any.</summary>
		private static Registration? Current()
		{
			uint count = 0, size = (uint)Marshal.SizeOf<Registration>();
			GetRegisteredRawInputDevices(null, ref count, size);
			if (count == 0) return null;
			var list = new Registration[count];
			uint read = GetRegisteredRawInputDevices(list, ref count, size);
			if (read == uint.MaxValue) throw new Win32Exception();
			for (int i = 0; i < read; i++)
				if (list[i].Page == 1 && list[i].Usage == 2) return list[i];
			return null;
		}

		private static bool Register(Registration registration) =>
			RegisterRawInputDevices(new[] { registration }, 1, (uint)Marshal.SizeOf<Registration>());

		// ---- devices (any thread) ----

		/// <summary>Handles and paths of the mice Windows knows now.</summary>
		public static void ListMice(Dictionary<IntPtr, string> into, Dictionary<IntPtr, string> known)
		{
			into.Clear();
			uint count = 0, size = (uint)Marshal.SizeOf<DeviceListItem>();
			if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue || count == 0) return;
			var list = new DeviceListItem[count + 4];
			count = (uint)list.Length;
			uint read = GetRawInputDeviceList(list, ref count, size);
			if (read == uint.MaxValue) return;
			for (int i = 0; i < read; i++)
			{
				if (list[i].Type != 0) continue; // RIM_TYPEMOUSE
				IntPtr handle = list[i].Handle;
				if (!known.TryGetValue(handle, out string path) || string.IsNullOrEmpty(path)) path = PathOf(handle);
				if (!string.IsNullOrEmpty(path)) into[handle] = path;
			}
		}

		/// <summary>The device path Windows gives a raw input handle, or null.</summary>
		public static string PathOf(IntPtr handle)
		{
			uint length = 0;
			if (GetRawInputDeviceInfo(handle, RidiDeviceName, null, ref length) == uint.MaxValue || length == 0) return null;
			var name = new StringBuilder((int)length + 1);
			if (GetRawInputDeviceInfo(handle, RidiDeviceName, name, ref length) == uint.MaxValue) return null;
			return name.ToString();
		}

		/// <summary>Windows' "switch primary and secondary buttons" setting (SM_SWAPBUTTON).</summary>
		public static bool ButtonsSwapped() => GetSystemMetrics(23) != 0;

		/// <summary>True if Windows says the button is down on any mouse (VK_LBUTTON 1, VK_RBUTTON 2; physical buttons).</summary>
		public static bool Physical(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

		// ---- Win32 ----

		[StructLayout(LayoutKind.Sequential)] private struct Registration { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
		[StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public IntPtr Device; public IntPtr WParam; }
		[StructLayout(LayoutKind.Sequential)] private struct DeviceListItem { public IntPtr Handle; public uint Type; }
		[StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct WindowClass
		{
			public uint Size, Style;
			public IntPtr Procedure;
			public int ClassExtra, WindowExtra;
			public IntPtr Instance, Icon, Cursor, Background;
			public string MenuName, ClassName;
			public IntPtr SmallIcon;
		}

		[UnmanagedFunctionPointer(CallingConvention.Winapi)]
		private delegate IntPtr WndProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Registration[] devices, uint count, uint size);
		[DllImport("user32.dll", SetLastError = true)] private static extern uint GetRegisteredRawInputDevices([Out] Registration[] devices, ref uint count, uint size);
		[DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
		[DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList([Out] DeviceListItem[] list, ref uint count, uint size);
		[DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);
		[DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
		[DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
		[DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass cls);
		[DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, IntPtr instance);
		[DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
		[DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
		[DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
		[DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
		[DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
		[DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern IntPtr DispatchMessage(ref Message message);
		[DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
		[DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
		private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
		[DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
		[DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint ms, IntPtr callback);
		[DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, UIntPtr id);
		[DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
	}
}
