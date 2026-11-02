// Registrador de diagnostico (sobremesa): anota con hora a kvmwatch.log
//  - USB/HID del monitor aparece/desaparece
//  - entrada de teclado/raton (el tiempo de inactividad vuelve a cero)
//  - estado de la pantalla de Windows (encendida/apagada/atenuada) y suspension del equipo
//  - DDC/CI responde o no, y monitores activos
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class W : NativeWindow
{
    [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, int flags);
    public static Action<string> Log;
    public W()
    {
        CreateHandle(new CreateParams());
        var g = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47"); // GUID_CONSOLE_DISPLAY_STATE
        RegisterPowerSettingNotification(Handle, ref g, 0);
        var g2 = new Guid("2e94c21f-1d2c-4b8d-97b6-b9f6ff1a8b30"); // placeholder, ignorado si no valido
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x218) // WM_POWERBROADCAST
        {
            int ev = m.WParam.ToInt32();
            if (ev == 0x8013) // PBT_POWERSETTINGCHANGE
            {
                int state = Marshal.ReadInt32(m.LParam, 20); // POWERBROADCAST_SETTING.Data
                Log("PANTALLA de Windows: " + (state == 0 ? "APAGADA" : state == 1 ? "ENCENDIDA" : "ATENUADA"));
            }
            else if (ev == 4) Log("EQUIPO: entrando en suspension");
            else if (ev == 7 || ev == 18) Log("EQUIPO: reanudado de suspension");
        }
        base.WndProc(ref m);
    }
}

static class P
{
    [StructLayout(LayoutKind.Sequential)] struct LII { public uint cb; public uint t; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LII p);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [StructLayout(LayoutKind.Sequential)] struct SPD { public int cb; public Guid g; public int f; public IntPtr r; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SPD o);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SPD d, IntPtr det, int sz, out int req, IntPtr dd);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    delegate bool MonEnum(IntPtr h, IntPtr dc, ref RECT r, IntPtr d);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonEnum cb, IntPtr d);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PM { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string d; }
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr h, out uint n);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr h, uint n, [Out] PM[] a);
    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr t, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitor(IntPtr h);

    static StreamWriter sw; static readonly object L = new object();
    static void Log(string s) { lock (L) { sw.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s); sw.Flush(); } }

    static bool Hid()
    {
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        var d = new SPD(); d.cb = Marshal.SizeOf(typeof(SPD)); bool f = false;
        for (int i = 0; !f && SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            int req; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
            IntPtr p = Marshal.AllocHGlobal(req); Marshal.WriteInt32(p, IntPtr.Size == 8 ? 8 : 6);
            if (SetupDiGetDeviceInterfaceDetail(set, ref d, p, req, out req, IntPtr.Zero)) f = Marshal.PtrToStringAuto(IntPtr.Add(p, 4)).ToLower().Contains("vid_1462&pid_3fa4");
            Marshal.FreeHGlobal(p);
        }
        SetupDiDestroyDeviceInfoList(set);
        return f;
    }
    static bool Ddc()
    {
        bool ok = false;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hm, IntPtr dc, ref RECT r, IntPtr d) =>
        {
            uint n; GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out n); var a = new PM[n]; GetPhysicalMonitorsFromHMONITOR(hm, n, a);
            foreach (var m in a) { uint c, mx; if (GetVCPFeatureAndVCPFeatureReply(m.h, 0x10, IntPtr.Zero, out c, out mx)) ok = true; DestroyPhysicalMonitor(m.h); }
            return false;
        }, IntPtr.Zero);
        return ok;
    }
    static uint IdleMs() { var l = new LII(); l.cb = 8; GetLastInputInfo(ref l); return (uint)Environment.TickCount - l.t; }

    [STAThread]
    static void Main(string[] a)
    {
        int minutes = a.Length > 0 ? int.Parse(a[0]) : 20;
        sw = new StreamWriter(new FileStream("kvmwatch.log", FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
        Log("=== inicio registro (" + minutes + " min) ===");
        W.Log = Log;
        var th = new Thread(() =>
        {
            bool hid = Hid(), ddc = Ddc(); int mon = GetSystemMetrics(80); uint lastIdle = IdleMs(); DateTime end = DateTime.Now.AddMinutes(minutes);
            Log("estado inicial: USB monitor=" + hid + " DDC=" + ddc + " monitores=" + mon);
            while (DateTime.Now < end)
            {
                bool h2 = Hid(), d2 = Ddc(); int m2 = GetSystemMetrics(80); uint idle = IdleMs();
                if (h2 != hid) { Log("USB del monitor " + (h2 ? "APARECE" : "DESAPARECE")); hid = h2; }
                if (d2 != ddc) { Log("DDC/CI " + (d2 ? "responde" : "no responde")); ddc = d2; }
                if (m2 != mon) { Log("monitores activos " + mon + " -> " + m2); mon = m2; }
                if (idle + 150 < lastIdle) Log("ENTRADA de teclado/raton en este equipo (inactividad era " + (lastIdle / 1000) + " s)");
                lastIdle = idle; Thread.Sleep(100);
            }
            Log("=== fin registro ==="); Application.Exit();
        });
        th.IsBackground = true; th.Start();
        new W();
        Application.Run();
    }
}
