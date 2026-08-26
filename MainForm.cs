using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ControllerMapper
{
    public class MainForm : Form
    {
        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private Label _statusLabel;
        private Button _toggleBtn;
        private Button _activeBindingButton = null;
        private FlowLayoutPanel _panel;

        public MainForm()
        {
            Text = "Kernel Controller Remapper";
            Size = new Size(480, 680);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            InitializeComponents();

            // Subscribe to status changes from the background driver thread
            Program.MappingStateChanged += UpdateStatusDisplay;
        }

        private void InitializeComponents()
        {
            _statusLabel = new Label
            {
                Text = "Status: ACTIVE",
                ForeColor = Color.Green,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(20, 15),
                AutoSize = true
            };

            _toggleBtn = new Button
            {
                Text = "Toggle Mapping",
                Location = new Point(20, 45),
                Size = new Size(420, 35)
            };
            _toggleBtn.Click += (s, e) => Program.ToggleMapping();

            _panel = new FlowLayoutPanel
            {
                Location = new Point(20, 90),
                Size = new Size(424, 530),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };

            // --- SYSTEM & ACTION BUTTONS ---
            AddSectionHeader("System & Actions");
            AddBindControl("Toggle Key", Program.Key_Toggle, (sc) => Program.Key_Toggle = sc);
            AddBindControl("A Button", Program.Key_BtnA, (sc) => Program.Key_BtnA = sc);
            AddBindControl("B Button", Program.Key_BtnB, (sc) => Program.Key_BtnB = sc);
            AddBindControl("B Button (Alt)", Program.Key_BtnB_Alt, (sc) => Program.Key_BtnB_Alt = sc);
            AddBindControl("X Button", Program.Key_BtnX, (sc) => Program.Key_BtnX = sc);
            AddBindControl("Y Button", Program.Key_BtnY, (sc) => Program.Key_BtnY = sc);

            // --- BUMPERS & STICK CLICKS ---
            AddSectionHeader("Bumpers & Stick Clicks");
            AddBindControl("Left Bumper (LB)", Program.Key_LB, (sc) => Program.Key_LB = sc);
            AddBindControl("LB (Alt)", Program.Key_LB_Alt, (sc) => Program.Key_LB_Alt = sc);
            AddBindControl("Right Bumper (RB)", Program.Key_RB, (sc) => Program.Key_RB = sc);
            AddBindControl("RB (Alt)", Program.Key_RB_Alt, (sc) => Program.Key_RB_Alt = sc);
            AddBindControl("Left Stick Click (L3)", Program.Key_L3, (sc) => Program.Key_L3 = sc);
            AddBindControl("Right Stick Click (R3)", Program.Key_R3, (sc) => Program.Key_R3 = sc);
            AddBindControl("Start Button", Program.Key_Start, (sc) => Program.Key_Start = sc);

            // --- D-PAD ---
            AddSectionHeader("D-Pad Controls");
            AddBindControl("D-Pad Up", Program.Key_DpadUp, (sc) => Program.Key_DpadUp = sc);
            AddBindControl("D-Pad Down", Program.Key_DpadDown, (sc) => Program.Key_DpadDown = sc);
            AddBindControl("D-Pad Left", Program.Key_DpadLeft, (sc) => Program.Key_DpadLeft = sc);
            AddBindControl("D-Pad Right", Program.Key_DpadRight, (sc) => Program.Key_DpadRight = sc);

            Controls.Add(_statusLabel);
            Controls.Add(_toggleBtn);
            Controls.Add(_panel);

            KeyDown += MainForm_KeyDown;
        }

        private void AddSectionHeader(string text)
        {
            Label header = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.DarkBlue,
                Margin = new Padding(0, 10, 0, 5),
                AutoSize = true
            };
            _panel.Controls.Add(header);
        }

        private void AddBindControl(string labelText, ushort initialScanCode, Action<ushort> updateScanCode)
        {
            Panel row = new Panel
            {
                Size = new Size(395, 35),
                Margin = new Padding(0, 2, 0, 2)
            };

            Label lbl = new Label
            {
                Text = labelText,
                Location = new Point(5, 8),
                Size = new Size(180, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };

            Button btn = new Button
            {
                Text = GetKeyNameFromScanCode(initialScanCode),
                Location = new Point(195, 3),
                Size = new Size(190, 28),
                Tag = updateScanCode
            };

            btn.Click += (s, e) =>
            {
                if (_activeBindingButton != null)
                {
                    _activeBindingButton.BackColor = SystemColors.Control;
                }

                _activeBindingButton = btn;
                btn.Text = "Press Any Key...";
                btn.BackColor = Color.LightYellow;
            };

            row.Controls.Add(lbl);
            row.Controls.Add(btn);
            _panel.Controls.Add(row);
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_activeBindingButton != null)
            {
                uint scanCode = MapVirtualKey((uint)e.KeyCode, 0);
                if (scanCode != 0)
                {
                    ushort sc = (ushort)scanCode;
                    var updateAction = (Action<ushort>)_activeBindingButton.Tag;
                    updateAction(sc);

                    _activeBindingButton.Text = GetKeyNameFromScanCode(sc);
                    _activeBindingButton.BackColor = SystemColors.Control;
                    _activeBindingButton = null;
                }
                e.Handled = true;
            }
        }

        private void UpdateStatusDisplay(bool enabled)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatusDisplay(enabled)));
                return;
            }

            _statusLabel.Text = enabled ? "Status: ACTIVE" : "Status: DISABLED";
            _statusLabel.ForeColor = enabled ? Color.Green : Color.Red;
        }

        private static string GetKeyNameFromScanCode(ushort scanCode)
        {
            uint vk = MapVirtualKey(scanCode, 1);
            if (vk == 0) return $"0x{scanCode:X2}";

            Keys key = (Keys)vk;
            return key switch
            {
                Keys.Capital => "Caps Lock",
                Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => "Ctrl",
                Keys.Menu or Keys.LMenu or Keys.RMenu => "Alt",
                Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => "Shift",
                Keys.Escape => "Esc",
                Keys.Space => "Space",
                Keys.Back => "Backspace",
                Keys.Return => "Enter",
                Keys.Tab => "Tab",
                _ => key.ToString()
            };
        }
    }
}
