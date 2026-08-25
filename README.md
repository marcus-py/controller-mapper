A C# application that intercepts raw keyboard and mouse (KBM) inputs at the Windows kernel level and translates them into a virtual Xbox 360 controller. 

This was built for hidden input mapping for games that read raw direct inputs. When enabled, physical keyboard and mouse inputs are hidden from the operating system, passing only the virtual controller inputs to the game.

## Prerequisites

To use this software, **you must install two system drivers**.

1. **[ViGEmBus Driver](https://github.com/nefarius/ViGEmBus/releases)**
   * Required to emulate the virtual Xbox 360 controller.

2. **[Interception Driver](https://github.com/oblitum/Interception/releases)**
   * This is the kernel driver that captures and blocks your physical keyboard and mouse.
   * Download the zip, extract it, and open a Command Prompt **as Administrator**.
   * Navigate to the extracted `command line installer` folder and run:
     ```cmd
     install-interception.exe /install
     ```
   * **YOU MUST REBOOT YOUR PC** after installing this driver.

## Installation & Usage

1. Extract the downloaded zip file completely. *(Make sure `ControllerMapper.exe` and `interception.dll` stay together in the same folder).*
2. Right-click `ControllerMapper.exe` and select **Run as Administrator**.
3. Press **Caps Lock** to toggle the controller mapping on and off.

---
## Default Controls

| Keyboard/Mouse | Virtual Xbox 360 Controller |
| :--- | :--- |
| **Caps Lock** | **Toggle Mapping ON/OFF** |
| W, A, S, D | Left Thumbstick |
| Mouse Movement | Right Thumbstick |
| Left Click | Right Trigger (RT) |
| Right Click | Left Trigger (LT) |
| Space | A Button |
| Ctrl / C | B Button |
| R | X Button |
| G | Y Button |
| Shift | Left Stick Click (L3) |
| F | Right Stick Click (R3) |
| 1 / Q | Left Bumper (LB) |
| 2 / E | Right Bumper (RB) |
| Esc | Start Button |
| B | D-Pad Up |
| V / M | D-Pad Down |
| Tab | D-Pad Left |
| T | D-Pad Right |


This program is potentially bannable. Intercepting kernel inputs can trigger certain anti-cheat software. Use at your own risk.
