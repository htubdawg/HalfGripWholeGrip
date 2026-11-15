using System;
using HalfGripTools;
using UnityEngine;

namespace WholeGrip
{
	/// <summary>
	/// Whole Grip: two mice, two cockpit hands. Load checks the platform and the game members the mod needs, installs the patches
	/// (all or none) and makes the persistent <see cref="Hands"/> object that does the work.
	/// </summary>
	public sealed class Mod : HalfGripMod
	{
		private Hands hands;

		public override void Load(ModContext ctx)
		{
			if (Application.platform != RuntimePlatform.WindowsPlayer || IntPtr.Size != 8)
			{
				ctx.Log.LogWarning("Whole Grip needs the 64-bit Windows game; it stays off.");
				return;
			}
			var missing = Patches.Missing();
			if (missing.Count > 0)
			{
				ctx.Log.LogError("Whole Grip stays off: this game version lacks " + string.Join(", ", missing.ToArray()));
				return;
			}
			try
			{
				var host = new GameObject("Whole Grip");
				UnityEngine.Object.DontDestroyOnLoad(host);
				hands = host.AddComponent<Hands>();
				hands.Initialize(ctx);
				Patches.Install(ctx.Harmony);
				ctx.OnSceneLoaded(_ => { if (hands) hands.SceneChanged(); });
			}
			catch (Exception e)
			{
				ctx.Log.LogError("Whole Grip could not start: " + e);
				Unload();
			}
		}

		/// <summary>Gives the mice back and removes the left hand before the loader removes the patches.</summary>
		public override void Unload()
		{
			if (!hands) return;
			hands.Shutdown();
			UnityEngine.Object.Destroy(hands.gameObject);
			hands = null;
		}
	}
}
