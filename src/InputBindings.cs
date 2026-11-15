using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using HarmonyLib;

namespace WholeGrip
{
    internal static class InputBindings
    {
        // Bind native input calls to the actual Grabber argument. This does not rely on small
        // GameInput accessors remaining uninlined or on Unity's currently active mouse device.
        internal static IEnumerable<CodeInstruction> RouteGrabberInput(IEnumerable<CodeInstruction> instructions)
        {
            var routes = new Dictionary<MethodInfo, MethodInfo>
            {
                [typeof(GameInput).GetMethod("Hand")] = typeof(Patches).GetMethod(nameof(Patches.OwnedHand), BindingFlags.Static | BindingFlags.NonPublic),
                [typeof(GameInput).GetMethod("HandDown")] = typeof(Patches).GetMethod(nameof(Patches.OwnedHandDown), BindingFlags.Static | BindingFlags.NonPublic),
                [typeof(GameInput).GetMethod("HandUp")] = typeof(Patches).GetMethod(nameof(Patches.OwnedHandUp), BindingFlags.Static | BindingFlags.NonPublic)
            };
            var seen = new HashSet<MethodInfo>();
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && routes.TryGetValue(method, out var route))
                {
                    seen.Add(method);
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    instruction.opcode = OpCodes.Call; instruction.operand = route;
                }
                yield return instruction;
            }
            if (seen.Count != routes.Count) throw new MissingMethodException("Grabber input call sites differ from the inspected build.");
        }
    }
}
