using System;
using System.Collections.Generic;

namespace WholeGrip
{
    // Menu use chooses R. Missing/disconnected hands recover from ordinary clicks, with no hotkeys.
    internal sealed class MouseAssignment
    {
        public string Right = "", Left = "", MenuMouse;
        public void ObserveMenu(Dictionary<string, DeviceFrame> snapshot)
        {
            int latest = 0;
            foreach (var pair in snapshot)
                if (pair.Value.PrimaryDownOrder > latest)
                { latest = pair.Value.PrimaryDownOrder; MenuMouse = pair.Key; }
        }
        public void EnterGameplay(Func<string, bool> connected)
        {
            if (!string.IsNullOrEmpty(MenuMouse) && connected(MenuMouse)) Right = MenuMouse;
            if (string.Equals(Right, Left, StringComparison.OrdinalIgnoreCase)) Left = "";
        }
        public void ObserveGameplay(Dictionary<string, DeviceFrame> snapshot, Func<string, bool> connected)
        {
            var clicks = new List<KeyValuePair<string, DeviceFrame>>();
            foreach (var pair in snapshot)
                if ((pair.Value.Down & 1) != 0 && connected(pair.Key)) clicks.Add(pair);
            clicks.Sort((a, b) => a.Value.PrimaryDownOrder.CompareTo(b.Value.PrimaryDownOrder));
            foreach (var pair in clicks)
            {
                if (!connected(Right) && !string.Equals(pair.Key, Left, StringComparison.OrdinalIgnoreCase)) Right = pair.Key;
                else if (!connected(Left) && !string.Equals(pair.Key, Right, StringComparison.OrdinalIgnoreCase)) Left = pair.Key;
            }
        }
    }
}
