using System;

namespace WholeGrip
{
	/// <summary>Which hand a mouse packet drives.</summary>
	internal enum Side { None, Right, Left }

	/// <summary>
	/// Decides which mouse is which hand, by device path (stable across replugging and restarts, unlike the handle). No Unity
	/// types here, so tests/ run it as it ships.
	/// <list type="bullet">
	/// <item>No right hand yet: every mouse but the left one moves the right hand, as the game's single hand would, and the first
	/// one to press a button becomes the right hand.</item>
	/// <item>Right hand known, no left hand yet: the first other mouse to press a button becomes the left hand. Until then other
	/// mice do nothing.</item>
	/// <item>A saved hand whose mouse is unplugged is free again: the next click from another mouse takes it.</item>
	/// </list>
	/// </summary>
	internal sealed class Assignment
	{
		public string Right = "", Left = "";

		/// <summary>Raised with the side and the new path when a click assigns a mouse.</summary>
		public event Action<Side, string> Assigned;

		/// <summary>Routes a packet from <paramref name="path"/>; <paramref name="press"/> is whether a button goes down in it.</summary>
		public Side Route(string path, bool press, Func<string, bool> connected)
		{
			if (string.IsNullOrEmpty(path)) return Side.None;
			if (Same(path, Right)) return Side.Right;
			if (Same(path, Left)) return Side.Left;
			bool rightFree = !Usable(Right, connected), leftFree = !Usable(Left, connected);
			if (rightFree)
			{
				if (press) { Right = path; Assigned?.Invoke(Side.Right, path); }
				return Side.Right;
			}
			if (leftFree && press)
			{
				Left = path;
				Assigned?.Invoke(Side.Left, path);
				return Side.Left;
			}
			return Side.None;
		}

		/// <summary>True once both hands have a connected mouse.</summary>
		public bool Complete(Func<string, bool> connected) => Usable(Right, connected) && Usable(Left, connected);

		/// <summary>Drops a saved left hand that is the same mouse as the right (an old or hand-edited config).</summary>
		public void Tidy()
		{
			if (Right == null) Right = "";
			if (Left == null) Left = "";
			if (Same(Right, Left)) Left = "";
		}

		private static bool Usable(string path, Func<string, bool> connected) => !string.IsNullOrEmpty(path) && connected(path);

		public static bool Same(string a, string b) =>
			!string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}
}
