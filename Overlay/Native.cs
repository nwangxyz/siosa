using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Siosa
{
    static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_COMPOSITED = 0x02000000;   // 창 전체를 한 장으로 모아 그림 (자식 컨트롤 깜빡임 방지)
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int WM_SETREDRAW = 0x0B;
        public const int HTCAPTION = 2;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const int EM_SETCUEBANNER = 0x1501;

        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
        [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint affinity);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        // 화면 캡처에서 이 창을 제외 (Windows 10 2004 이상). 실패해도 무시.
        //   보상 판별 캡처에 Siosa 의 강조 상자·오버레이가 섞이지 않게 하려는 것인데, PrintScreen·Win+Shift+S·OBS 에서도 안 보이게 된다.
        //   Siosa.exe --showcapture 로 실행하면 제외하지 않는다 (README 용 화면 찍기. 이때는 판별 캡처에 Siosa 창이 섞일 수 있음)
        public static bool ShowInCapture;

        public static void ExcludeFromCapture(IntPtr hWnd)
        {
            if (ShowInCapture) return;
            try { SetWindowDisplayAffinity(hWnd, 0x11 /* WDA_EXCLUDEFROMCAPTURE */); } catch { }
        }

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    }
}
