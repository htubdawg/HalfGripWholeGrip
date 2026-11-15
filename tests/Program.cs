using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using WholeGrip;

/// <summary>
/// Offline checks: the hand input and mouse assignment logic as it ships, and the game members the patches name by string.
/// Usage: dotnet run --project tests -- [Assembly-CSharp.dll]
/// </summary>
internal static class Program
{
	private static int failures;
	private static readonly IntPtr A = new IntPtr(1), B = new IntPtr(2);
	private static readonly bool[] Up = { false, false }, LeftDown = { true, false }, BothDown = { true, true };

	private static int Main(string[] args)
	{
		HandInputChecks();
		AssignmentChecks();
		if (args.Length > 0) GameChecks(args[0]);
		Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
		return failures == 0 ? 0 : 1;
	}

	private static void Check(bool ok, string what)
	{
		if (!ok) { failures++; Console.WriteLine("FAIL " + what); }
	}

	private static MousePacket Move(int x, int y) => new MousePacket(A, x, y, 0);
	private static MousePacket Buttons(ushort flags) => new MousePacket(A, 0, 0, flags);
	private const ushort LDown = 0x1, LUp = 0x2, RDown = 0x4, RUp = 0x8;

	private static void HandInputChecks()
	{
		// A whole click between two frames: a press this frame, its release the next. 0.2.0 lost these.
		var hand = new HandInput();
		hand.Add(Buttons(LDown)); hand.Add(Buttons(LUp));
		hand.BeginFrame(Up);
		Check(hand.Down(0) && hand.Held(0) && !hand.Up(0), "click between frames: press first");
		hand.BeginFrame(Up);
		Check(hand.Up(0) && !hand.Held(0) && !hand.Down(0), "click between frames: release next frame");
		hand.BeginFrame(Up);
		Check(!hand.Up(0) && !hand.Down(0) && !hand.Held(0), "click between frames: then quiet");

		// Press and release reported in one packet.
		hand = new HandInput();
		hand.Add(Buttons(LDown | LUp));
		hand.BeginFrame(Up);
		Check(hand.Down(0), "one-packet click: press");
		hand.BeginFrame(Up);
		Check(hand.Up(0), "one-packet click: release");

		// Hold across many frames while moving; the physical state says held, so nothing lets go.
		hand = new HandInput();
		hand.Add(Buttons(LDown));
		hand.BeginFrame(LeftDown);
		for (int frame = 0; frame < 100; frame++)
		{
			for (int i = 0; i < 8; i++) hand.Add(Move(3, -2));
			hand.BeginFrame(LeftDown);
			Check(hand.Held(0) && !hand.Up(0) && !hand.Down(0), "hold while moving, frame " + frame);
			Check(hand.X == 24 && hand.Y == -16, "movement adds up per frame, frame " + frame);
		}
		hand.Add(Buttons(LUp));
		hand.BeginFrame(Up);
		Check(hand.Up(0) && !hand.Held(0), "release after a long hold");

		// The owner's 0.3.0 report: both mice hold, one lets go. Windows' button state is shared and not a count, so the one
		// release makes it say up for both; the hand that still holds must keep holding until its own release.
		var holdA = new HandInput();
		var holdB = new HandInput();
		holdA.Add(Buttons(LDown)); holdB.Add(Buttons(LDown));
		holdA.BeginFrame(LeftDown); holdB.BeginFrame(LeftDown);
		holdA.Add(Buttons(LUp));
		holdA.BeginFrame(Up); holdB.BeginFrame(Up);
		Check(holdA.Up(0) && !holdA.Held(0), "both hold, A releases: A lets go");
		Check(holdB.Held(0) && !holdB.Up(0), "both hold, A releases: B keeps holding although Windows says up");
		for (int frame = 0; frame < 30; frame++) holdB.BeginFrame(Up);
		Check(holdB.Held(0), "both hold, A releases: B still holds frames later");
		holdB.Add(Buttons(LUp));
		holdB.BeginFrame(Up);
		Check(holdB.Up(0) && !holdB.Held(0), "both hold: B lets go on its own release");

		// A hold guessed at Resync stops being a guess once this hand's packets show the button: Windows' state no longer counts.
		hand = new HandInput();
		hand.Resync(true);
		hand.Add(Buttons(LDown)); // the "lost release" path puts back a release, then the press
		hand.BeginFrame(LeftDown);
		hand.BeginFrame(LeftDown);
		Check(hand.Held(0), "guess replaced by a seen press: held");
		hand.BeginFrame(Up);
		Check(hand.Held(0) && !hand.Up(0), "seen press: Windows saying up does not let go");

		// The other mouse holding the button keeps the physical state down: no early release.
		hand = new HandInput();
		hand.Add(Buttons(LDown));
		hand.BeginFrame(LeftDown);
		hand.BeginFrame(LeftDown);
		Check(hand.Held(0) && !hand.Up(0), "physical state held by either mouse: hold stays");

		// The safety net waits for queued transitions: a press then release both queued are not cut short.
		hand = new HandInput();
		hand.Add(Buttons(LDown)); hand.Add(Buttons(LUp)); hand.Add(Buttons(LDown));
		hand.BeginFrame(Up);
		Check(hand.Down(0), "queued: press");
		hand.BeginFrame(Up);
		Check(hand.Up(0), "queued: release");
		hand.BeginFrame(LeftDown);
		Check(hand.Down(0) && hand.Held(0), "queued: second press");

		// A press without a release before it (the release got lost on the way): the release is put back.
		hand = new HandInput();
		hand.Add(Buttons(LDown));
		hand.BeginFrame(LeftDown);
		hand.Add(Buttons(LDown));
		hand.BeginFrame(LeftDown);
		Check(hand.Up(0) && !hand.Held(0), "double press: missing release put back");
		hand.BeginFrame(LeftDown);
		Check(hand.Down(0) && hand.Held(0), "double press: then the new press");

		// A release with no press seen (button held when driving began): ignored.
		hand = new HandInput();
		hand.Add(Buttons(LUp));
		hand.BeginFrame(Up);
		Check(!hand.Up(0) && !hand.Down(0) && !hand.Held(0), "stray release ignored");

		// Click spam inside one frame: bounded queue, alternating, ends in the final state.
		hand = new HandInput();
		for (int i = 0; i < 40; i++) { hand.Add(Buttons(LDown)); hand.Add(Buttons(LUp)); }
		int frames = 0, downs = 0, ups = 0;
		bool alternating = true, last = false;
		for (; frames < 50; frames++)
		{
			hand.BeginFrame(Up);
			if (hand.Down(0)) { downs++; if (last) alternating = false; last = true; }
			if (hand.Up(0)) { ups++; if (!last) alternating = false; last = false; }
			if (!hand.Down(0) && !hand.Up(0)) break;
		}
		Check(frames <= HandInput.MaxQueued, "click spam: drained within " + HandInput.MaxQueued + " frames (took " + frames + ")");
		Check(alternating && downs == ups && downs > 0 && !hand.Held(0), "click spam: alternates and ends released");

		// Right button runs on its own queue.
		hand = new HandInput();
		hand.Add(Buttons(LDown)); hand.Add(Buttons(RDown)); hand.Add(Buttons(RUp));
		hand.BeginFrame(BothDown);
		Check(hand.Down(0) && hand.Down(1), "both buttons press in the same frame");
		hand.BeginFrame(LeftDown);
		Check(hand.Held(0) && hand.Up(1) && !hand.Up(0), "right releases without the left");

		// Absolute packets (tablets, touch) move nothing; their buttons still count.
		hand = new HandInput();
		hand.Add(new MousePacket(A, 30000, 30000, LDown, true));
		hand.BeginFrame(LeftDown);
		Check(hand.X == 0 && hand.Y == 0 && hand.Down(0), "absolute packet: buttons only");

		// Resync from a hand that holds something with the button already up: it lets go on the next frame.
		hand = new HandInput();
		hand.Resync(true);
		hand.BeginFrame(Up);
		Check(hand.Up(0) && !hand.Held(0), "resync while holding, button up: lets go");
		hand.Resync(true);
		hand.BeginFrame(LeftDown);
		Check(hand.Held(0) && !hand.Up(0), "resync while holding, button down: keeps holding");
		hand.Add(Buttons(LUp));
		hand.BeginFrame(Up);
		Check(hand.Up(0), "resync while holding: the later release lets go");

		// Two hands at once at a high polling rate: each sees only its own packets, every frame, with nothing crossing over.
		var right = new HandInput();
		var left = new HandInput();
		for (int frame = 0; frame < 500; frame++)
		{
			for (int i = 0; i < 16; i++) { right.Add(Move(1, 0)); left.Add(Move(0, 1)); }
			if (frame % 7 == 0) { left.Add(Buttons(LDown)); left.Add(Buttons(LUp)); }
			if (frame == 10) right.Add(Buttons(LDown));
			right.BeginFrame(LeftDown); left.BeginFrame(LeftDown);
			Check(right.X == 16 && right.Y == 0 && left.X == 0 && left.Y == 16, "two hands: movement kept apart, frame " + frame);
			if (frame > 10) Check(right.Held(0) && !right.Up(0), "two hands: right keeps holding while left clicks, frame " + frame);
		}
	}

	private static void AssignmentChecks()
	{
		var plugged = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mouseA", "mouseB", "mouseC" };
		Func<string, bool> connected = plugged.Contains;
		var assigned = new List<string>();
		var a = new Assignment();
		a.Assigned += (side, path) => assigned.Add(side + ":" + path);

		Check(a.Route("mouseA", false, connected) == Side.Right, "no right hand: any mouse moves it");
		Check(a.Route("mouseB", false, connected) == Side.Right, "no right hand: the other mouse too");
		Check(a.Route("mouseA", true, connected) == Side.Right && a.Right == "mouseA", "first click takes the right hand");
		Check(a.Route("mouseB", false, connected) == Side.None, "no left hand yet: other mice's movement does nothing");
		Check(a.Route("MOUSEA", true, connected) == Side.Right && a.Left == "", "the right mouse again (any case) stays right");
		Check(a.Route("mouseB", true, connected) == Side.Left && a.Left == "mouseB", "a click from another mouse takes the left hand");
		Check(a.Complete(connected), "both hands assigned");
		Check(a.Route("mouseC", true, connected) == Side.None, "a third mouse does nothing");
		Check(assigned.SequenceEqual(new[] { "Right:mouseA", "Left:mouseB" }), "assigned events in order");

		plugged.Remove("mouseB");
		Check(!a.Complete(connected), "left mouse unplugged");
		Check(a.Route("mouseC", false, connected) == Side.None, "replacement: movement alone does not take the hand");
		Check(a.Route("mouseC", true, connected) == Side.Left && a.Left == "mouseC", "replacement: its click takes the free hand");
		plugged.Add("mouseB");
		Check(a.Route("mouseB", true, connected) == Side.None, "the old mouse back after replacement does nothing");

		var saved = new Assignment { Right = "mouseA", Left = "MouseA" };
		saved.Tidy();
		Check(saved.Left == "", "a saved left hand equal to the right is dropped");
		var empty = new Assignment { Right = null, Left = null };
		empty.Tidy();
		Check(empty.Route("", true, connected) == Side.None && empty.Right == "", "empty path routes nowhere");
	}

	/// <summary>The members Patches and Hands name by string, and what Grabber.Update reads, in the game assembly.</summary>
	private static void GameChecks(string path)
	{
		using var stream = File.OpenRead(path);
		using var pe = new PEReader(stream);
		var md = pe.GetMetadataReader();
		TypeDefinition Type(string name) => md.TypeDefinitions.Select(md.GetTypeDefinition).First(t => md.GetString(t.Name) == name && md.GetString(t.Namespace) == "");
		IEnumerable<MethodDefinition> Methods(TypeDefinition t, string name) => t.GetMethods().Select(md.GetMethodDefinition).Where(m => md.GetString(m.Name) == name);
		bool HasField(TypeDefinition t, string name) => t.GetFields().Any(f => md.GetString(md.GetFieldDefinition(f).Name) == name);

		var grabber = Type("Grabber");
		foreach (var field in new[] { "rawHandPlanePoint", "<Held>k__BackingField", "<Hovered>k__BackingField" })
			Check(HasField(grabber, field), "game: Grabber." + field);
		foreach (var method in new[] { "Update", "LateUpdate", "UpdateCursor", "TryInteract", "DispatchRightClick", "SetHandVisible" })
			Check(Methods(grabber, method).Any(), "game: Grabber." + method);
		var input = Type("GameInput");
		foreach (var method in new[] { "GetKey", "GetKeyDown", "GetKeyUp", "GetMouseButton", "GetMouseButtonDown", "GetMouseButtonUp", "Hand", "HandDown", "HandUp" })
			Check(Methods(input, method).Count() == 1, "game: GameInput." + method + " (one overload)");
		Check(HasField(Type("NSteeringWheel"), "wheel"), "game: NSteeringWheel.wheel");
		Check(HasField(Type("NShifter"), "gate"), "game: NShifter.gate");
		Check(HasField(Type("InteriorMath"), "Paused"), "game: InteriorMath.Paused");
		Check(HasField(Type("GameManager"), "raceOver"), "game: GameManager.raceOver");

		// GameInput.Hand/HandDown/HandUp are tiny wrappers over GetKey*: small enough for Mono to inline, which is why the
		// patches sit on GetKey*. Check they still only forward (so patching GetKey* covers them).
		foreach (var (wrapper, target) in new[] { ("Hand", "GetKey"), ("HandDown", "GetKeyDown"), ("HandUp", "GetKeyUp") })
		{
			var body = pe.GetMethodBody(Methods(input, wrapper).First().RelativeVirtualAddress);
			var il = body.GetILBytes();
			bool callsTarget = Calls(md, il).Contains("GameInput." + target);
			Console.WriteLine("  GameInput." + wrapper + ": " + il.Length + " IL bytes, forwards to " + target);
			Check(callsTarget && il.Length < 20, "game: GameInput." + wrapper + " forwards to " + target + " (" + il.Length + " IL bytes)");
		}
		var update = Calls(md, pe.GetMethodBody(Methods(grabber, "Update").First().RelativeVirtualAddress).GetILBytes());
		foreach (var read in new[] { "GameInput.Hand", "GameInput.HandDown", "GameInput.HandUp", "Grabber.UpdateCursor", "Grabber.TryInteract", "Grabber.DispatchRightClick" })
			Check(update.Contains(read), "game: Grabber.Update calls " + read);
		var cursor = Calls(md, pe.GetMethodBody(Methods(grabber, "UpdateCursor").First().RelativeVirtualAddress).GetILBytes());
		Check(cursor.Contains("Input.get_mousePositionDelta"), "game: Grabber.UpdateCursor reads Input.mousePositionDelta (what the patch replaces)");
	}

	/// <summary>"Type.Method" for every call/callvirt in an IL body (method definitions and references).</summary>
	private static HashSet<string> Calls(MetadataReader md, byte[] il)
	{
		var calls = new HashSet<string>();
		for (int i = 0; i + 4 < il.Length; i++)
		{
			if (il[i] != 0x28 && il[i] != 0x6F) continue; // call, callvirt
			int token = BitConverter.ToInt32(il, i + 1);
			var handle = MetadataTokens.EntityHandle(token);
			try
			{
				if (handle.Kind == HandleKind.MethodDefinition)
				{
					var m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
					calls.Add(md.GetString(md.GetTypeDefinition(m.GetDeclaringType()).Name) + "." + md.GetString(m.Name));
				}
				else if (handle.Kind == HandleKind.MemberReference)
				{
					var m = md.GetMemberReference((MemberReferenceHandle)handle);
					if (m.Parent.Kind == HandleKind.TypeReference)
						calls.Add(md.GetString(md.GetTypeReference((TypeReferenceHandle)m.Parent).Name) + "." + md.GetString(m.Name));
				}
			}
			catch (BadImageFormatException) { }
		}
		return calls;
	}
}
