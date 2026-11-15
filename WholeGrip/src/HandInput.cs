using System.Collections.Generic;

namespace WholeGrip
{
	/// <summary>
	/// One mouse packet as Windows reports it: the device it came from, its movement in counts (Windows' Y grows downward) and its
	/// button transition flags (RI_MOUSE_*: bit 2n is button n going down, bit 2n+1 is it coming up). No Unity types here, so the
	/// checks in tests/ run this file as it ships.
	/// </summary>
	internal struct MousePacket
	{
		public System.IntPtr Device;
		public int X, Y;
		public ushort Buttons;
		public bool Absolute;

		public MousePacket(System.IntPtr device, int x, int y, ushort buttons, bool absolute = false)
		{
			Device = device; X = x; Y = y; Buttons = buttons; Absolute = absolute;
		}

		/// <summary>True if any button goes down in this packet: the click that assigns a mouse to a hand.</summary>
		public bool AnyDown => (Buttons & 0x155) != 0;
	}

	/// <summary>
	/// The buttons and movement of one hand, turned into what the game's legacy input would have said for one frame.
	/// <para>
	/// The game reads a hand through GetKey/GetKeyDown/GetKeyUp, which can only say one press or one release per frame. Packets
	/// arrive far faster than frames, and a whole click often lands between two frames: read as a frame total, the press and the
	/// release cancel out and the game sees neither (or a press without its release, which leaves the hand holding). So every
	/// transition is queued and handed out one per frame and button, in order. A click between frames becomes a press this frame
	/// and a release the next.
	/// </para>
	/// <para>
	/// The physical button state that Windows keeps for all mice together (GetAsyncKeyState) is the safety net: if it says no
	/// mouse holds the button while this hand still does, a release was lost somewhere, and the hand lets go.
	/// </para>
	/// </summary>
	internal sealed class HandInput
	{
		/// <summary>Buttons the game reads: 0 left (grab), 1 right (secondary).</summary>
		public const int ButtonCount = 2;

		/// <summary>Queued transitions per button beyond which the middle is dropped, keeping latency short under click spam.</summary>
		public const int MaxQueued = 6;

		private readonly Queue<bool>[] queued = { new Queue<bool>(), new Queue<bool>() };
		private readonly bool[] reported = new bool[ButtonCount];
		private readonly bool[] held = new bool[ButtonCount], down = new bool[ButtonCount], up = new bool[ButtonCount];
		private long pendingX, pendingY;

		/// <summary>Movement this frame in counts, Windows' axes (Y down).</summary>
		public long X { get; private set; }
		public long Y { get; private set; }

		/// <summary>Set whenever this hand's mouse sent anything since the last <see cref="ClearSeen"/>.</summary>
		public bool Seen { get; private set; }

		public bool Held(int button) => button >= 0 && button < ButtonCount && held[button];
		public bool Down(int button) => button >= 0 && button < ButtonCount && down[button];
		public bool Up(int button) => button >= 0 && button < ButtonCount && up[button];

		public void ClearSeen() => Seen = false;

		/// <summary>Adds a packet from this hand's mouse. Movement adds up; button transitions queue in order.</summary>
		public void Add(MousePacket packet)
		{
			Seen = true;
			if (!packet.Absolute) { pendingX += packet.X; pendingY += packet.Y; }
			for (int button = 0; button < ButtonCount; button++)
			{
				bool pressed = (packet.Buttons & (1 << (button * 2))) != 0;
				bool released = (packet.Buttons & (2 << (button * 2))) != 0;
				// A packet can carry both: a press and release reported together are a whole click, in that order.
				if (pressed)
				{
					// A second press without a release in between means the release got lost: put it back first.
					if (reported[button]) Queue(button, false);
					Queue(button, true);
					reported[button] = true;
				}
				if (released && reported[button])
				{
					Queue(button, false);
					reported[button] = false;
				}
			}
		}

		/// <summary>
		/// Starts a frame: takes this frame's movement and at most one transition per button.
		/// <paramref name="physicallyHeld"/>[b] is Windows' own state of button b over all mice.
		/// </summary>
		public void BeginFrame(bool[] physicallyHeld)
		{
			X = pendingX; Y = pendingY;
			pendingX = pendingY = 0;
			for (int button = 0; button < ButtonCount; button++)
			{
				down[button] = up[button] = false;
				var queue = queued[button];
				if (queue.Count > 0)
				{
					bool press = queue.Dequeue();
					if (press && !held[button]) { held[button] = true; down[button] = true; }
					else if (!press && held[button]) { held[button] = false; up[button] = true; }
				}
				else if (held[button] && physicallyHeld != null && button < physicallyHeld.Length && !physicallyHeld[button])
				{
					// No mouse holds the button and nothing is queued: a release never arrived.
					held[button] = false; up[button] = true; reported[button] = false;
				}
			}
		}

		/// <summary>
		/// Forgets queued input and starts again from what the game shows: <paramref name="holding"/> is whether this hand's grabber
		/// holds something now. A hand that holds something is taken to have its button down, so it lets go through the normal path
		/// (a release, or the physical-state check the next frame) instead of being stuck with no release to come.
		/// </summary>
		public void Resync(bool holding)
		{
			for (int button = 0; button < ButtonCount; button++)
			{
				queued[button].Clear();
				held[button] = button == 0 && holding;
				reported[button] = held[button];
				down[button] = up[button] = false;
			}
			pendingX = pendingY = 0;
			X = Y = 0;
		}

		/// <summary>Queues a transition. Past <see cref="MaxQueued"/>, whole clicks in the middle go: the order and final state stay.</summary>
		private void Queue(int button, bool press)
		{
			var queue = queued[button];
			queue.Enqueue(press);
			if (queue.Count <= MaxQueued) return;
			// Transitions alternate (Add puts back lost releases), so dropping two neighbours removes one whole click or one whole
			// release-and-press, and what remains still alternates. The first (due next frame) and the last (the end state) stay.
			var items = new List<bool>(queue);
			while (items.Count > MaxQueued) items.RemoveRange(1, 2);
			queue.Clear();
			foreach (bool item in items) queue.Enqueue(item);
		}
	}
}
