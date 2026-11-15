# Whole Grip

**Version 0.3.0** for Half Grip 0.419, a [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools) mod (1.0.6 or newer).

Two mice, two hands. Plug a second mouse into your PC and the cockpit gets a left hand of its own: keep one hand on the wheel
while the other works the shifter, and let go of either without touching the other. Windows only.

## Using it

1. Start a race with both mice plugged in.
2. In the cockpit, click with the mouse you want as your **right hand**. That is the game's usual hand; until you click, any
   mouse moves it.
3. Click with the other mouse: it becomes the **left hand** (a mirrored copy of the right one) and appears left of centre.

Both choices are remembered, by the mouse itself, so next time both hands are there from the start. A line at the top of the
screen says when a hand is still waiting for its mouse. Each mouse grabs with its left button and uses its right button for the
secondary action (steering lock), like the game's own hand.

- **Right hand:** everything the game's hand does: wheel, shifter, guns, ammo, buttons, other mods' parts.
- **Left hand:** the steering wheel, the shifter, and buttons. Guns and parts added by other mods (SlapPack's stalks, cranks and
  mirror) answer to the right mouse only. Each part has one owner at a time: what one hand holds, the other can't take.
- The mod only steps in while you're in the cockpit, racing. Menus, the pause menu, popups, other cameras and a window in the
  background are exactly as the game makes them, with one mouse.
- With a single mouse, or Whole Grip switched off in the mod manager, the game is unchanged.

## Install

### Easy way: the HalfGripMods installer

1. Download `HalfGrip-Mods-Installer.exe` from the newest release of
   [HalfGripMods](https://github.com/4l3x4nd3r0/HalfGripMods/releases) and double-click it. If Windows says "Windows protected
   your PC", press **More info**, then **Run anyway**. It finds the game through Steam (or asks for its folder) and installs
   BepInEx and HalfGripTools for you.
2. Close the game. Press **+ ADD** and paste this link to the mod's folder:
   ```
   https://github.com/htubdawg/HalfGripWholeGrip/tree/main/WholeGrip
   ```
   Then press **INSTALL** on Whole Grip's card.
3. Start the game from Steam. The first start takes a little longer while the mods compile.

**CHECK NOW** in the installer looks for updates.

### By hand

Install BepInEx 5 and [HalfGripTools](https://github.com/4l3x4nd3r0/HalfGripTools) 1.0.6+ as its README says. Then copy only the
`WholeGrip` folder into `<game>\BepInEx\HalfGripMods\`, so that `mod.json` sits directly in `HalfGripMods\WholeGrip\`, not the whole
repository download. Coming from 0.2.0 or older: delete the old `HalfGripWholeGrip` folder from `HalfGripMods\` first.

## Settings

`BepInEx\config\halfgrip.wholegrip.cfg`:

| Setting | Default | What it does |
|---|---|---|
| `Devices.Right`, `Devices.Left` | empty | Which mouse is which hand, filled in by your clicks. Empty one (or both) to choose again; swap them to swap hands. |
| `Hands.RightSensitivity`, `Hands.LeftSensitivity` | 1 | Multiply the game's hand sensitivity for each hand, for two mice of different DPI. |
| `Display.Hints` | true | The line at the top of the screen while a hand waits for its mouse. |
| `Debug.VerboseLog` | false | Routine information in `BepInEx\LogOutput.log`. |

## How it works

Windows sends a program's raw mouse input to one window, and the game's engine reads it in bulk, so no mod can watch it from the
side without missing packets. 0.2.0 tried, and missed most of them: that was the jitter, the lost clicks and the releases that
never came. Whole Grip now borrows the game's mouse input while you drive and gives it back the moment you stop: a background
thread reads every packet from every mouse, tagged with the mouse it came from. Each hand then runs the game's own hand code once
a frame, fed with its own mouse's movement and buttons. A click that falls between two frames is played as a press and then a
release. If Windows says no mouse is holding a button, a hand that still thinks it is lets go.

No drivers, helper programs or other software. The mod reads only the mice, and writes only its own config file.
