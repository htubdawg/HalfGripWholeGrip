using UnityEngine;

namespace WholeGrip
{
    internal sealed class HandState
    {
        public readonly string Name;
        public readonly Color Color;
        public Grabber Grabber;
        public string Device;
        public MouseStep Input;
        public DeviceFrame Frame;
        public bool Rearmed;
        public HandState(string name, Color color) { Name = name; Color = color; }
    }
}
