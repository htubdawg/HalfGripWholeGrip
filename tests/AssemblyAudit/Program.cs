using Mono.Cecil;

using var game = AssemblyDefinition.ReadAssembly(args[0]);
using var mod = AssemblyDefinition.ReadAssembly(args[1]);
var grabber = game.MainModule.Types.Single(t => t.Name == "Grabber");
var helpers = new[] { "UpdateHover", "TryInteract", "DispatchRightClick", "UpdateHeld", "BeginHeldLook", "EndHeldLook", "LateUpdate", "UpdateIndicators" };
foreach (var name in helpers)
{
    var method = grabber.Methods.Single(m => m.Name == name);
    CheckInput(method);
}
foreach (var name in new[] { "HandDriver", "VanillaHandInteraction", "DualHandController", "Patches" })
    foreach (var method in mod.MainModule.Types.Single(t => t.Name == name).Methods.Where(m => m.HasBody)) CheckInput(method);

void CheckInput(MethodDefinition method)
{
    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.Operand is not MethodReference called) continue;
        if (called.DeclaringType.Name == "GameInput" && called.Name is "Hand" or "HandDown" or "HandUp")
            throw new Exception(method.FullName + " reads shared mouse buttons: " + called.Name);
        if (called.DeclaringType.Name == "Input" && (called.Name.Contains("Mouse", StringComparison.OrdinalIgnoreCase) || called.Name.Contains("mouse", StringComparison.Ordinal)))
            throw new Exception(method.FullName + " reads aggregate mouse input: " + called.Name);
    }
}
// Verify the adapter's native fields and helper signatures against the private DLL, without
// executing Unity or depending on a synthetic copy of the native Update method.
foreach (var name in new[] { "rawHandPlanePoint", "<Held>k__BackingField", "<Hovered>k__BackingField", "heldLastFrame", "suppressSweep" })
    if (!grabber.Fields.Any(f => f.Name == name)) throw new Exception("Missing native field: " + name);
Console.WriteLine("PASS: compiled controller and native geometry helpers contain no shared mouse/button reads; helper/field targets exist.");
