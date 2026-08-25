using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ControllerMapper
{
    internal class Program
    {
        private const double DEADZONE_OFFSET = 0.15;
        private const double GAMMA = 0.65;
        private const double SENSITIVITY = 1.0;
        private const double MAX_DELTA = 35.0;

        private static IXbox360Controller _controller;
        private static bool _isMappingEnabled = true;

        private static int _rawMouseX = 0;
        private static int _rawMouseY = 0;
        private static readonly object _stateLock = new object();

        private static bool _btnA, _btnB, _btnX, _btnY;
        private static bool _btnLB, _btnRB, _btnL3, _btnR3, _btnStart;
        private static bool _dpadUp, _dpadDown, _dpadLeft, _dpadRight;
        private static byte _triggerL, _triggerR;
        private static bool _isW, _isA, _isS, _isD;

        // --- Native Interception API Direct Imports ---
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int Predicate(int device);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr interception_create_context();

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_destroy_context(IntPtr context);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_set_filter(IntPtr context, Predicate predicate, ushort filter);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_wait(IntPtr context);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_receive(IntPtr context, int device, ref Stroke stroke, uint nstroke);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_send(IntPtr context, int device, ref Stroke stroke, uint nstroke);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_is_keyboard(int device);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_is_mouse(int device);

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyStroke
        {
            public ushort Code;
            public ushort State;
            public uint Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseStroke
        {
            public ushort State;
            public ushort Flags;
            public short Rolling;
            public int X;
            public int Y;
            public uint Information;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct Stroke
        {
            [FieldOffset(0)] public KeyStroke Key;
            [FieldOffset(0)] public MouseStroke Mouse;
        }

        private const ushort FILTER_KEY_ALL = 0xFFFF;
        private const ushort FILTER_MOUSE_ALL = 0xFFFF;

        static void Main(string[] args)
        {
            Console.WriteLine("Connecting to ViGEmBus driver...");
            var client = new ViGEmClient();
            _controller = client.CreateXbox360Controller();
            _controller.Connect();
            Console.WriteLine("Virtual Xbox 360 Controller connected!");

            StartControllerOutputThread();
            StartKernelInterception();
        }

        private static void StartKernelInterception()
        {
            IntPtr context = interception_create_context();

            interception_set_filter(context, interception_is_keyboard, FILTER_KEY_ALL);
            interception_set_filter(context, interception_is_mouse, FILTER_MOUSE_ALL);

            Console.WriteLine("\n=== KERNEL INTERCEPTION MAPPER ACTIVE ===");
            Console.WriteLine("Press 'CAPS LOCK' to TOGGLE mapping ON/OFF.\n");

            Stroke stroke = new Stroke();
            int device;

            while (interception_receive(context, device = interception_wait(context), ref stroke, 1) > 0)
            {
                bool suppress = false;

                if (interception_is_keyboard(device) != 0)
                {
                    suppress = HandleKeyboardInput(stroke.Key);
                }
                else if (interception_is_mouse(device) != 0)
                {
                    suppress = HandleMouseInput(stroke.Mouse);
                }

                if (!suppress)
                {
                    interception_send(context, device, ref stroke, 1);
                }
            }

            interception_destroy_context(context);
        }

        private static bool HandleKeyboardInput(KeyStroke key)
        {
            bool isDown = (key.State & 1) == 0; // State 0 = Down, 1 = Up
            ushort code = key.Code;

            // Toggle Mapping via Caps Lock (Scan Code 0x3A)
            if (code == 0x3A)
            {
                if (isDown)
                {
                    _isMappingEnabled = !_isMappingEnabled;
                    Console.WriteLine(_isMappingEnabled ? "[ENABLED] Interception Active" : "[DISABLED] Native KBM Active");
                    ResetState();
                }
                return true;
            }

            if (!_isMappingEnabled) return false;

            lock (_stateLock)
            {
                // WASD
                if (code == 0x11) _isW = isDown;
                if (code == 0x1E) _isA = isDown;
                if (code == 0x1F) _isS = isDown;
                if (code == 0x20) _isD = isDown;

                // Buttons
                if (code == 0x39) _btnA = isDown;                            // Space -> A
                if (code == 0x2A || code == 0x36) _btnL3 = isDown;           // Shift -> L3
                if (code == 0x1D || code == 0x2E) _btnB = isDown;           // Ctrl / C -> B
                if (code == 0x13) _btnX = isDown;                            // R -> X
                if (code == 0x21) _btnR3 = isDown;                           // F -> R3
                if (code == 0x22) _btnY = isDown;                            // G -> Y

                // D-Pad
                if (code == 0x2F || code == 0x32) _dpadDown = isDown;        // V / M -> Down
                if (code == 0x14) _dpadRight = isDown;                       // T -> Right
                if (code == 0x30) _dpadUp = isDown;                          // B -> Up
                if (code == 0x0F) _dpadLeft = isDown;                        // Tab -> Left

                // Bumpers
                if (code == 0x02 || code == 0x10) _btnLB = isDown;           // 1 / Q -> LB
                if (code == 0x03 || code == 0x12) _btnRB = isDown;           // 2 / E -> RB

                // Menus
                if (code == 0x01) _btnStart = isDown;                        // Esc -> Start
            }

            return true;
        }

        private static bool HandleMouseInput(MouseStroke mouse)
        {
            if (!_isMappingEnabled) return false;

            lock (_stateLock)
            {
                _rawMouseX += mouse.X;
                _rawMouseY += mouse.Y;

                if ((mouse.State & 0x0001) != 0) _triggerR = 255; // Left Down
                if ((mouse.State & 0x0002) != 0) _triggerR = 0;   // Left Up
                if ((mouse.State & 0x0004) != 0) _triggerL = 255; // Right Down
                if ((mouse.State & 0x0008) != 0) _triggerL = 0;   // Right Up
            }

            return true;
        }

        private static void StartControllerOutputThread()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    if (_isMappingEnabled)
                    {
                        lock (_stateLock)
                        {
                            float x = 0.0f, y = 0.0f;
                            if (_isW) y += 1.0f;
                            if (_isS) y -= 1.0f;
                            if (_isD) x += 1.0f;
                            if (_isA) x -= 1.0f;
                            if (x != 0.0f && y != 0.0f) { x *= 0.7071f; y *= 0.7071f; }

                            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, (short)(x * 32767));
                            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, (short)(y * 32767));

                            int dx = _rawMouseX;
                            int dy = _rawMouseY;
                            _rawMouseX = 0;
                            _rawMouseY = 0;

                            if (dx == 0 && dy == 0)
                            {
                                _controller.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                                _controller.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                            }
                            else
                            {
                                double mag = Math.Sqrt(dx * dx + dy * dy);
                                double norm = Math.Clamp((mag * SENSITIVITY) / MAX_DELTA, 0.0, 1.0);
                                double output = DEADZONE_OFFSET + (1.0 - DEADZONE_OFFSET) * Math.Pow(norm, GAMMA);
                                double unitX = dx / mag;
                                double unitY = -dy / mag;

                                _controller.SetAxisValue(Xbox360Axis.RightThumbX, (short)Math.Clamp(unitX * output * 32767, -32768, 32767));
                                _controller.SetAxisValue(Xbox360Axis.RightThumbY, (short)Math.Clamp(unitY * output * 32767, -32768, 32767));
                            }

                            _controller.SetButtonState(Xbox360Button.A, _btnA);
                            _controller.SetButtonState(Xbox360Button.B, _btnB);
                            _controller.SetButtonState(Xbox360Button.X, _btnX);
                            _controller.SetButtonState(Xbox360Button.Y, _btnY);
                            _controller.SetButtonState(Xbox360Button.LeftShoulder, _btnLB);
                            _controller.SetButtonState(Xbox360Button.RightShoulder, _btnRB);
                            _controller.SetButtonState(Xbox360Button.LeftThumb, _btnL3);
                            _controller.SetButtonState(Xbox360Button.RightThumb, _btnR3);
                            _controller.SetButtonState(Xbox360Button.Start, _btnStart);
                            _controller.SetButtonState(Xbox360Button.Up, _dpadUp);
                            _controller.SetButtonState(Xbox360Button.Down, _dpadDown);
                            _controller.SetButtonState(Xbox360Button.Left, _dpadLeft);
                            _controller.SetButtonState(Xbox360Button.Right, _dpadRight);

                            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, _triggerL);
                            _controller.SetSliderValue(Xbox360Slider.RightTrigger, _triggerR);

                            _controller.SubmitReport();
                        }
                    }
                    Thread.Sleep(2);
                }
            });
        }

        private static void ResetState()
        {
            lock (_stateLock)
            {
                _isW = _isA = _isS = _isD = false;
                _btnA = _btnB = _btnX = _btnY = false;
                _btnLB = _btnRB = _btnL3 = _btnR3 = _btnStart = false;
                _dpadUp = _dpadDown = _dpadLeft = _dpadRight = false;
                _triggerL = _triggerR = 0;

                _controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
                _controller.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
                _controller.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                _controller.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                _controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
                _controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
                _controller.SubmitReport();
            }
        }
    }
}
