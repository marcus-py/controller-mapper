using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace ControllerMapper
{
    public static class Program
    {
        public static event Action<bool>? MappingStateChanged;

        private const double DEADZONE_OFFSET = 0.15;
        private const double GAMMA = 0.65;
        private const double SENSITIVITY = 1.0;
        private const double MAX_DELTA = 35.0;

        private static IXbox360Controller? _controller;
        private static bool _isMappingEnabled = true;

        private static int _rawMouseX = 0;
        private static int _rawMouseY = 0;
        private static readonly object _stateLock = new object();

        private static bool _btnA, _btnB, _btnX, _btnY;
        private static bool _btnLB, _btnRB, _btnL3, _btnR3, _btnStart;
        private static bool _dpadUp, _dpadDown, _dpadLeft, _dpadRight;
        private static byte _triggerL, _triggerR;
        private static bool _isW, _isA, _isS, _isD;

        // --- CUSTOMIZABLE KEYBINDINGS (Scan Codes) ---
        public static ushort Key_Toggle = 0x3A; // Caps Lock
        public static ushort Key_W = 0x11;      // W
        public static ushort Key_A = 0x1E;      // A
        public static ushort Key_S = 0x1F;      // S
        public static ushort Key_D = 0x20;      // D

        public static ushort Key_BtnA = 0x39;   // Space
        public static ushort Key_BtnB = 0x1D;   // Ctrl (Primary)
        public static ushort Key_BtnB_Alt = 0x2E; // C (Alt)
        public static ushort Key_BtnX = 0x13;   // R
        public static ushort Key_BtnY = 0x22;   // G

        public static ushort Key_LB = 0x10;     // Q
        public static ushort Key_LB_Alt = 0x02; // 1
        public static ushort Key_RB = 0x12;     // E
        public static ushort Key_RB_Alt = 0x03; // 2

        public static ushort Key_L3 = 0x2A;     // Left Shift
        public static ushort Key_R3 = 0x21;     // F
        public static ushort Key_Start = 0x01;  // Esc

        public static ushort Key_DpadUp = 0x30;    // B
        public static ushort Key_DpadDown = 0x2F;  // V
        public static ushort Key_DpadLeft = 0x0F;  // Tab
        public static ushort Key_DpadRight = 0x14; // T

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

        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Connecting to ViGEmBus driver...");
            var client = new ViGEmClient();
            _controller = client.CreateXbox360Controller();
            _controller.Connect();

            StartControllerOutputThread();

            Task.Run(() => StartKernelInterception());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public static bool ToggleMapping()
        {
            _isMappingEnabled = !_isMappingEnabled;
            ResetState();
            MappingStateChanged?.Invoke(_isMappingEnabled);
            return _isMappingEnabled;
        }

        private static void StartKernelInterception()
        {
            IntPtr context = interception_create_context();

            interception_set_filter(context, interception_is_keyboard, FILTER_KEY_ALL);
            interception_set_filter(context, interception_is_mouse, FILTER_MOUSE_ALL);

            Console.WriteLine("\n=== KERNEL INTERCEPTION MAPPER ACTIVE ===");

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

            // Toggle Mapping via Configured Toggle Key
            if (code == Key_Toggle)
            {
                if (isDown)
                {
                    ToggleMapping();
                }
                return true;
            }

            if (!_isMappingEnabled) return false;

            lock (_stateLock)
            {
                // Dynamic Movement Check
                if (code == Key_W) _isW = isDown;
                if (code == Key_A) _isA = isDown;
                if (code == Key_S) _isS = isDown;
                if (code == Key_D) _isD = isDown;

                // Dynamic Button Checks
                if (code == Key_BtnA) _btnA = isDown;
                if (code == Key_BtnB || code == Key_BtnB_Alt) _btnB = isDown;
                if (code == Key_BtnX) _btnX = isDown;
                if (code == Key_BtnY) _btnY = isDown;

                // Bumpers & Sticks
                if (code == Key_LB || code == Key_LB_Alt) _btnLB = isDown;
                if (code == Key_RB || code == Key_RB_Alt) _btnRB = isDown;
                if (code == Key_L3) _btnL3 = isDown;
                if (code == Key_R3) _btnR3 = isDown;

                // D-Pad
                if (code == Key_DpadDown) _dpadDown = isDown;
                if (code == Key_DpadRight) _dpadRight = isDown;
                if (code == Key_DpadUp) _dpadUp = isDown;
                if (code == Key_DpadLeft) _dpadLeft = isDown;

                // Menu
                if (code == Key_Start) _btnStart = isDown;
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
                    if (_isMappingEnabled && _controller != null)
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

        public static void ResetState()
        {
            lock (_stateLock)
            {
                _isW = _isA = _isS = _isD = false;
                _btnA = _btnB = _btnX = _btnY = false;
                _btnLB = _btnRB = _btnL3 = _btnR3 = _btnStart = false;
                _dpadUp = _dpadDown = _dpadLeft = _dpadRight = false;
                _triggerL = _triggerR = 0;

                if (_controller != null)
                {
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
}
