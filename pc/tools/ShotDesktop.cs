using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace ShotDesktop
{
    static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateDesktop(string name, IntPtr dev, IntPtr dm, int flags, uint access, IntPtr sa);

        [DllImport("user32.dll")] static extern bool SetThreadDesktop(IntPtr desk);
        [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desk);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h, IntPtr r, IntPtr rgn, uint flags);
        [DllImport("user32.dll")] static extern bool UpdateWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);

        delegate bool EnumProc(IntPtr h, IntPtr p);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit,
            int flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

        static int Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("usage: shot-desktop <exe> <out.png> <titlePart> [waitMs]");
                return 1;
            }
            string exe = args[0], outPath = args[1], title = args[2];
            int wait = args.Length > 3 ? int.Parse(args[3]) : 6000;

            IntPtr desk = CreateDesktop("NexusShot", IntPtr.Zero, IntPtr.Zero, 0, 0x01FF, IntPtr.Zero);
            if (desk == IntPtr.Zero)
            {
                Console.WriteLine("desktop failed: " + Marshal.GetLastWin32Error());
                return 2;
            }
            if (!SetThreadDesktop(desk))
            {
                Console.WriteLine("setdesktop failed: " + Marshal.GetLastWin32Error());
                return 3;
            }

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = "NexusShot";
            PROCESS_INFORMATION pi;
            if (!CreateProcess(exe, null, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero,
                    System.IO.Path.GetDirectoryName(exe), ref si, out pi))
            {
                Console.WriteLine("createprocess failed: " + Marshal.GetLastWin32Error());
                return 4;
            }

            IntPtr target = IntPtr.Zero;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < wait)
            {
                Thread.Sleep(400);
                IntPtr found = IntPtr.Zero;
                EnumWindows((h, p) =>
                {
                    if (!IsWindowVisible(h)) return true;
                    uint pid;
                    GetWindowThreadProcessId(h, out pid);
                    if (pid != (uint)pi.dwProcessId) return true;
                    int len = GetWindowTextLength(h);
                    if (len == 0) return true;
                    var sb = new System.Text.StringBuilder(len + 2);
                    GetWindowText(h, sb, sb.Capacity);
                    if (sb.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                if (found != IntPtr.Zero)
                {
                    target = found;
                    break;
                }
            }

            if (target == IntPtr.Zero)
            {
                Console.WriteLine("window not found");
                try { Process.GetProcessById(pi.dwProcessId).Kill(); } catch { }
                return 5;
            }

            Thread.Sleep(800);
            RedrawWindow(target, IntPtr.Zero, IntPtr.Zero, 0x0085);
            UpdateWindow(target);
            SendMessage(target, 0x000F, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(900);
            RECT r;
            GetWindowRect(target, out r);
            int w = r.R - r.L, hh = r.B - r.T;
            if (w <= 0 || hh <= 0)
            {
                Console.WriteLine("bad rect");
                return 6;
            }

            using (var bmp = new Bitmap(w, hh))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr dc = g.GetHdc();
                    PrintWindow(target, dc, 0);
                    g.ReleaseHdc(dc);
                }
                bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            try { Process.GetProcessById(pi.dwProcessId).Kill(); } catch { }
            CloseDesktop(desk);
            Console.WriteLine("ok " + outPath + " " + w + "x" + hh);
            return 0;
        }
    }
}
