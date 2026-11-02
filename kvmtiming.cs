// Herramienta de diagnostico: envia "--to usbc" y anota cuando desaparece el USB (teclado/raton/HID)
// y cuando cambia el estado del vídeo en este equipo (monitores activos, DDC, estado de pantalla).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class T
{
    [StructLayout(LayoutKind.Sequential)] struct SPD { public int cb; public Guid g; public int f; public IntPtr r; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SPD o);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SPD d, IntPtr det, int sz, out int req, IntPtr dd);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
    delegate bool MonEnum(IntPtr h, IntPtr dc, ref RECT r, IntPtr d);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonEnum cb, IntPtr d);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PM { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string d; }
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr h, out uint n);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr h, uint n, [Out] PM[] a);
    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr t, out uint cur, out uint max);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitor(IntPtr h);

    static string HidPath()
    {
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        var d = new SPD(); d.cb = Marshal.SizeOf(typeof(SPD));
        string found = null;
        for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            int req; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
            IntPtr p = Marshal.AllocHGlobal(req); Marshal.WriteInt32(p, IntPtr.Size == 8 ? 8 : 6);
            if (SetupDiGetDeviceInterfaceDetail(set, ref d, p, req, out req, IntPtr.Zero))
            { string s = Marshal.PtrToStringAuto(IntPtr.Add(p, 4)); if (s.ToLower().Contains("vid_1462&pid_3fa4")) found = s; }
            Marshal.FreeHGlobal(p);
        }
        SetupDiDestroyDeviceInfoList(set);
        return found;
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

    static void Main(string[] a)
    {
        string path = HidPath();
        if (path == null) { Console.WriteLine("Monitor no encontrado por USB."); return; }
        int target = int.Parse(a[0]); int seconds = int.Parse(a[1]);
        var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        var fs = new FileStream(h, FileAccess.ReadWrite, 65, true);
        var sw = Stopwatch.StartNew();
        bool lastHid = true, lastDdc = Ddc(); int lastMon = GetSystemMetrics(80);
        Console.WriteLine("t=0 ms: estado inicial hid=" + lastHid + " ddc=" + lastDdc + " monitores=" + lastMon);
        var b = new byte[65]; b[0] = 1; var s = Encoding.ASCII.GetBytes("5b0050000" + target + "\r"); Array.Copy(s, 0, b, 1, s.Length);
        long t0 = sw.ElapsedMilliseconds;
        if (target >= 0)
        {
            fs.Write(b, 0, 65); fs.Flush();
            Console.WriteLine("t=" + (sw.ElapsedMilliseconds - t0) + " ms: comando enviado (5b0050000" + target + ")");
        }
        else Console.WriteLine("modo observacion: cambia la entrada con los botones del monitor ahora");
        while (sw.ElapsedMilliseconds - t0 < seconds * 1000)
        {
            bool hid = HidPath() != null; bool ddc = Ddc(); int mon = GetSystemMetrics(80);
            long t = sw.ElapsedMilliseconds - t0;
            if (hid != lastHid) { Console.WriteLine("t=" + t + " ms: USB/HID del monitor " + (hid ? "APARECE" : "DESAPARECE")); lastHid = hid; }
            if (ddc != lastDdc) { Console.WriteLine("t=" + t + " ms: DDC/CI " + (ddc ? "responde" : "DEJA de responder")); lastDdc = ddc; }
            if (mon != lastMon) { Console.WriteLine("t=" + t + " ms: monitores activos " + lastMon + " -> " + mon); lastMon = mon; }
            Thread.Sleep(15);
        }
        Console.WriteLine("fin.");
    }
}
