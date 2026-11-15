using System;
using HalfGripTools;
using UnityEngine;

namespace WholeGrip
{
    public sealed class Mod : HalfGripMod
    {
        private DualHandController controller;
        public override void Load(ModContext ctx)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer || IntPtr.Size != 8)
            { ctx.Log.LogWarning("Whole Grip requires the 64-bit Windows player; no patches applied."); return; }
            ctx.Log.LogInfo("Installed game: " + Application.version + "; Unity: " + Application.unityVersion +
                "; loader: " + typeof(HalfGripMod).Assembly.GetName().Version);
            if (Application.version != "0.419")
            { ctx.Log.LogError("Whole Grip only inspected game 0.419; no patches applied on this build."); return; }
            try
            {
                // Patch as one transaction. ModContext.PatchAll intentionally tolerates partial failure,
                // which would be unsafe for an input coordinator.
                Patches.Install(ctx.Harmony);
                var host = new GameObject("WholeGrip controller");
                UnityEngine.Object.DontDestroyOnLoad(host);
                controller = host.AddComponent<DualHandController>();
                controller.Initialize(ctx);
                ctx.OnSceneLoaded(_ => controller.SceneChanged());
            }
            catch (Exception e)
            {
                ctx.Log.LogError("Whole Grip initialization failed: " + e);
                if (controller) { controller.Shutdown(); UnityEngine.Object.Destroy(controller.gameObject); }
                ctx.Harmony.UnpatchSelf();
            }
        }
        public override void Unload()
        {
            if (!controller) return;
            controller.Shutdown(); // synchronous, before HalfGripTools removes patches
            UnityEngine.Object.Destroy(controller.gameObject);
            controller = null;
        }
    }
}
