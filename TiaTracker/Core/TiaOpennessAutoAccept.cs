using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace TiaTracker.Core
{
    /// <summary>
    /// Auto-aceita o diálogo de segurança do TIA Portal Openness.
    /// Usa Win32 EnumWindows para encontrar a janela e UI Automation para clicar o botão.
    /// </summary>
    public static class TiaOpennessAutoAccept
    {
        // ── Win32 ─────────────────────────────────────────────────────────────
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lp);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc fn, IntPtr lp);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr h, StringBuilder sb, int cap);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, int e);
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;

        static string GetWinText(IntPtr h)
        {
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // ── State ─────────────────────────────────────────────────────────────
        private static volatile bool _accepted;
        private static int _tiaPid;

        // ── Public API ────────────────────────────────────────────────────────
        public static CancellationTokenSource StartWatcher(int tiaProcessId)
        {
            _tiaPid   = tiaProcessId;
            _accepted = false;

            var cts = new CancellationTokenSource();
            var thread = new Thread(() =>
            {
                while (!cts.Token.IsCancellationRequested && !_accepted)
                {
                    try   { ScanAndClick(); }
                    catch { }
                    Thread.Sleep(150);
                }
            }) { IsBackground = true, Name = "TIA-Openness-AutoAccept" };

            thread.Start();
            return cts;
        }

        // ── Scan ──────────────────────────────────────────────────────────────
        private static void ScanAndClick()
        {
            EnumWindows((hwnd, _) =>
            {
                if (_accepted) return false;
                if (!IsWindowVisible(hwnd)) return true;

                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != (uint)_tiaPid) return true; // só janelas do TIA Portal

                var title = GetWinText(hwnd);
                var lower = title.ToLowerInvariant();

                if (!lower.Contains("openness") && !lower.Contains("acesso"))
                    return true;

                // Encontrou o diálogo do Openness
                TryClickViaUIA(hwnd);
                return !_accepted;

            }, IntPtr.Zero);
        }

        private static void TryClickViaUIA(IntPtr hwnd)
        {
            AutomationElement win;
            try { win = AutomationElement.FromHandle(hwnd); }
            catch { return; }

            var buttons = win.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            // "Yes to all" em qualquer formato (com ou sem espaços, com ou sem sufixo "Button")
            // Prioridade: YestoallButton > YesButton
            AutomationElement yesAll = null, yes = null;

            foreach (AutomationElement btn in buttons)
            {
                string n = ""; try { n = btn.Current.Name; } catch { continue; }
                var key = n.ToLowerInvariant().Replace(" ", "").Replace("button", "").Trim();

                if (key == "yestoall" || key == "simparatodos" || key == "yesall")
                    yesAll = btn;
                else if (key == "yes" || key == "sim" || key == "ja")
                    yes = btn;
            }

            var target = yesAll ?? yes;
            if (target == null) return;

            string targetName = ""; try { targetName = target.Current.Name; } catch { }

            if (!TryInvoke(target))
                TryMouseClick(target);

            _accepted = true;
            Console.WriteLine($"  [AutoAccept] Aceite: '{targetName}'");
        }

        private static bool TryInvoke(AutomationElement btn)
        {
            try
            {
                if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out object p))
                { ((InvokePattern)p).Invoke(); return true; }
            }
            catch { }
            return false;
        }

        private static bool TryMouseClick(AutomationElement btn)
        {
            try
            {
                var r = btn.Current.BoundingRectangle;
                if (r.IsEmpty) return false;
                int x = (int)(r.Left + r.Width / 2), y = (int)(r.Top + r.Height / 2);
                SetCursorPos(x, y); Thread.Sleep(50);
                mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0); Thread.Sleep(50);
                mouse_event(MOUSEEVENTF_LEFTUP,   x, y, 0, 0);
                return true;
            }
            catch { }
            return false;
        }
    }
}
