using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using WholeGrip;
using Mono.Cecil;

internal static class BindingChecks
{
    public static void Main()
    {
        // Inspect real game IL through the real production transpiler without creating Unity objects.
        using var game = AssemblyDefinition.ReadAssembly(typeof(Grabber).Assembly.Location);
        var update = game.MainModule.Types.Single(t => t.FullName == "Grabber").Methods.Single(m => m.Name == "Update");
        // Only call-site metadata is needed; no Unity runtime or Mono detours execute on Linux.
        var original = new List<CodeInstruction>();
        foreach (var instruction in update.Body.Instructions)
        {
            if (instruction.Operand is MethodReference method && method.DeclaringType.FullName == "GameInput" &&
                new[] { "Hand", "HandDown", "HandUp" }.Contains(method.Name))
                original.Add(new CodeInstruction(OpCodes.Call, typeof(GameInput).GetMethod(method.Name)));
            else original.Add(new CodeInstruction(OpCodes.Nop));
        }
        var patchType = typeof(Mod).Assembly.GetType("WholeGrip.Patches", true);
        var route = typeof(Mod).Assembly.GetType("WholeGrip.InputBindings", true).GetMethod("RouteGrabberInput", BindingFlags.Static | BindingFlags.NonPublic);
        var output = ((IEnumerable<CodeInstruction>)route.Invoke(null, new object[] { original })).ToList();
        var names = new HashSet<string>();
        int calls = 0;
        for (int i = 0; i < output.Count; i++)
        {
            var instruction = output[i];
            if (!(instruction.operand is MethodInfo method)) continue;
            if (method.DeclaringType == typeof(GameInput) && new[] { "Hand", "HandDown", "HandUp" }.Contains(method.Name))
                throw new Exception("Aggregate input remains in native Grabber.Update: " + method.Name);
            if (method.DeclaringType == patchType && method.Name.StartsWith("OwnedHand"))
            {
                calls++; names.Add(method.Name);
                if (i == 0 || output[i - 1].opcode != OpCodes.Ldarg_0 || method.GetParameters()[1].ParameterType != typeof(Grabber))
                    throw new Exception("Hand read is not bound to its own Grabber argument");
            }
        }
        if (calls < 3 || names.Count != 3) throw new Exception("Missing held/down/up bindings");
        Console.WriteLine("PASS: real Grabber.Update call sites route all " + calls + " hand reads directly to its Grabber instance.");
    }
}
