using UnityEngine;

namespace WholeGrip
{
    internal sealed class HandState
    {
        public readonly string Name;
        public readonly Color Color;
        public Grabber Grabber;
        public string Device;
        public readonly HandDriver Driver = new HandDriver();
        public VanillaHandInteraction Interaction;
        public DeviceFrame Frame;
        public bool Rearmed, SeenInput;
        public HandState(string name, Color color) { Name = name; Color = color; }
    }
}
