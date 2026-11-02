// kvmswitch: cambia el KVM del monitor MSI MD342CQP entre sobremesa (2) y portatil (3).
// Sin admin. Protocolo: informe HID id=1, 64 bytes, ASCII "5b0050000N\r" (N = 2 sobremesa, 3 portatil).
// Lectura del estado: "5800500\r" -> respuesta "5b0050000N".
// Uso:
//   kvmswitch.exe                  residente en la bandeja, atajo Ctrl+Shift+1
//   kvmswitch.exe --hotkey ctrl+alt+k
//   kvmswitch.exe --toggle         alterna una vez y sale
//   kvmswitch.exe --to hdmi1|hdmi2|dp|usbc   (tambien desktop = dp, laptop = usbc)
//   kvmswitch.exe --status
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

static class Kvm
{
    const ushort VID = 0x1462, PID = 0x3FA4;
    const int DESKTOP = 2, LAPTOP = 3;

    [StructLayout(LayoutKind.Sequential)] struct SpDeviceInterfaceData { public int cb; public Guid g; public int f; public IntPtr r; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SpDeviceInterfaceData o);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SpDeviceInterfaceData d, IntPtr det, int sz, out int req, IntPtr dd);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);

    static List<string> FindPaths()
    {
        var res = new List<string>();
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        var d = new SpDeviceInterfaceData(); d.cb = Marshal.SizeOf(typeof(SpDeviceInterfaceData));
        string needle = string.Format("vid_{0:x4}&pid_{1:x4}", VID, PID);
        for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            int req; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
            IntPtr p = Marshal.AllocHGlobal(req);
            Marshal.WriteInt32(p, IntPtr.Size == 8 ? 8 : 6);
            if (SetupDiGetDeviceInterfaceDetail(set, ref d, p, req, out req, IntPtr.Zero))
            {
                string path = Marshal.PtrToStringAuto(IntPtr.Add(p, 4));
                if (path.ToLower().Contains(needle)) res.Add(path);
            }
            Marshal.FreeHGlobal(p);
        }
        SetupDiDestroyDeviceInfoList(set);
        return res;
    }

    // Envia un comando ASCII y devuelve la respuesta (texto) o null.
    static string Talk(FileStream fs, string cmd)
    {
        var b = new byte[65]; b[0] = 1;
        var s = Encoding.ASCII.GetBytes(cmd + "\r");
        Array.Copy(s, 0, b, 1, s.Length);
        fs.Write(b, 0, 65); fs.Flush();
        var r = new byte[65];
        var t = fs.BeginRead(r, 0, 65, null, null);
        if (!t.AsyncWaitHandle.WaitOne(1500)) return null;
        int n = fs.EndRead(t);
        var sb = new StringBuilder();
        for (int i = 1; i < n && r[i] >= 32 && r[i] < 127; i++) sb.Append((char)r[i]);
        return sb.ToString();
    }

    static void WithDevice(Action<FileStream> act)
    {
        foreach (var path in FindPaths())
        {
            var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (h.IsInvalid) continue;
            using (var fs = new FileStream(h, FileAccess.ReadWrite, 65, true)) { act(fs); return; }
        }
        throw new Exception("Monitor MSI (VID 1462 / PID 3FA4) no encontrado por USB.");
    }

    // true si el USB del monitor esta conectado a este equipo (KVM en este equipo).
    // KVM_FAKE_AWAY=1 solo sirve para probar la logica sin mover el monitor.
    public static bool Present()
    {
        if (Environment.GetEnvironmentVariable("KVM_FAKE_AWAY") == "1") return false;
        if (File.Exists(Path.Combine(Path.GetTempPath(), "kvmswitch_fake_away"))) return false;
        return FindPaths().Count > 0;
    }

    public static int Status()
    {
        int v = -1;
        WithDevice(fs => { var r = Talk(fs, "5800500"); if (r != null && r.StartsWith("5b0050") && r.Length >= 10) v = Convert.ToInt32(r.Substring(r.Length - 1), 16); });
        return v;
    }

    public static void Set(int value) { WithDevice(fs => Talk(fs, "5b0050000" + value)); }

    // Si estamos en el portatil (3) vuelve al sobremesa (2); en cualquier otro caso va al portatil.
    public static string Toggle()
    {
        int cur = Status();
        int target = cur == LAPTOP ? DESKTOP : LAPTOP;
        Set(target);
        return target == LAPTOP ? "portatil" : "sobremesa";
    }

    // Entradas del monitor: 0 HDMI1, 1 HDMI2, 2 DP, 3 USB-C. Solo 2 y 3 estan verificados; 0 y 1 son hipotesis sin probar.
    public static int ToValue(string name)
    {
        switch (name.ToLower())
        {
            case "hdmi1": return 0;
            case "hdmi2": return 1;
            case "dp": case "desktop": case "sobremesa": return DESKTOP;
            case "usbc": case "laptop": case "portatil": return LAPTOP;
        }
        throw new Exception("Entrada desconocida: " + name + " (hdmi1, hdmi2, dp, usbc, desktop, laptop)");
    }
    public static string Name(int v)
    {
        switch (v) { case 0: return "HDMI 1 (0, sin verificar)"; case 1: return "HDMI 2 (1, sin verificar)"; case 2: return "DisplayPort / sobremesa (2)"; case 3: return "USB-C / portatil (3)"; }
        return "desconocido (" + v + ")";
    }
}

class HotkeyForm : Form
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    const int WM_HOTKEY = 0x312;
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint f);
    // Impide que Windows apague la pantalla/suspenda: la salida DisplayPort sigue con senal aunque el monitor este en el otro equipo.
    static void SetKeepAwake(bool on) { SetThreadExecutionState(0x80000000u | (on ? 0x2u | 0x1u : 0u)); }
    NotifyIcon icon;

    public HotkeyForm(uint mod, uint vk, string label)
    {
        ShowInTaskbar = false; WindowState = FormWindowState.Minimized; Opacity = 0;
        Icon ico = SystemIcons.Application;
        try { using (var st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("kvmswitch.ico")) ico = new Icon(st, SystemInformation.SmallIconSize); } catch { }
        Icon = ico;
        icon = new NotifyIcon { Icon = ico, Visible = true, Text = "KVM MSI (" + label + ")" };
        var menu = new ContextMenu();
        menu.MenuItems.Add("Cambiar ahora", (s, e) => Run());
        var keep = new MenuItem("Mantener esta pantalla encendida");
        keep.Click += (s, e) => { keep.Checked = !keep.Checked; SetKeepAwake(keep.Checked); };
        menu.MenuItems.Add(keep);
        if (Program.KeepAwake) { keep.Checked = true; SetKeepAwake(true); }
        if (Program.AutoAwake)
        {
            // Pantalla encendida solo mientras el monitor esta en el otro equipo: asi este equipo sigue dando senal
            // y el monitor puede traer el USB al volver. Con el monitor aqui, Windows gestiona el reposo con normalidad.
            keep.Text = "Pantalla despierta si el monitor esta en el otro equipo (auto)";
            keep.Enabled = false;
            keepItem = keep;
            // Sin sondeo periodico: Windows avisa (WM_DEVICECHANGE) cuando el USB del monitor aparece o desaparece.
            recheckTimer = new Timer { Interval = 500 };
            recheckTimer.Tick += (s, e) => { recheckTimer.Stop(); Recheck(); };
        }
        menu.MenuItems.Add("Salir", (s, e) => { icon.Visible = false; Application.Exit(); });
        icon.ContextMenu = menu;
        icon.DoubleClick += (s, e) => Run();
        this.mod = mod; this.vk = vk; this.label = label;
        CreateHandle();
    }
    uint mod, vk; string label;
    bool? awayState; MenuItem keepItem; Timer recheckTimer;

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr RegisterDeviceNotification(IntPtr h, IntPtr filter, uint flags);

    // Aviso de aparicion/desaparicion de cualquier dispositivo HID (DBT_DEVTYP_DEVICEINTERFACE + GUID_DEVINTERFACE_HID).
    static void Log(string s)
    {
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "kvmswitch.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + s + "\r\n"); } catch { }
    }

    bool WatchHidDevices()
    {
        IntPtr f = Marshal.AllocHGlobal(32);
        for (int i = 0; i < 32; i++) Marshal.WriteByte(f, i, 0);
        Marshal.WriteInt32(f, 0, 32); Marshal.WriteInt32(f, 4, 5);
        Marshal.Copy(new Guid("4d1e55b2-f16f-11cf-88cb-001111000030").ToByteArray(), 0, IntPtr.Add(f, 12), 16);
        return RegisterDeviceNotification(Handle, f, 0) != IntPtr.Zero;
    }

    // Se llama solo cuando algo cambia (o al arrancar / reanudar), nunca de forma periodica.
    void Recheck()
    {
        if (keepItem == null) return;
        bool away;
        try { away = !Kvm.Present(); } catch { return; }
        if (awayState == away) return;
        awayState = away;
        SetKeepAwake(away); keepItem.Checked = away;
        icon.Text = "KVM MSI (" + label + ")" + (away ? " - pantalla despierta" : "");
        Log(away ? "monitor en el otro equipo: pantalla mantenida encendida" : "monitor en este equipo: reposo normal");
    }
    // Pequeno retardo para que el cambio de USB termine y para agrupar avisos seguidos.
    void ScheduleRecheck() { if (recheckTimer != null) { recheckTimer.Stop(); recheckTimer.Start(); } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!RegisterHotKey(Handle, 1, mod | 0x4000, vk))
            MessageBox.Show("No se pudo registrar el atajo " + label + " (ya esta en uso por otra aplicacion, por ejemplo la app de MSI).", "KVM MSI");
        if (Program.AutoAwake)
        {
            if (WatchHidDevices()) Log("vigilando aparicion/desaparicion de USB (sin sondeo)");
            else
            {
                // Red de seguridad: si Windows rechazara el aviso, comprobar cada 30 s en vez de quedarnos ciegos.
                Log("AVISO: no se pudo registrar el aviso de USB; comprobando cada 30 s");
                var slow = new Timer { Interval = 30000 };
                slow.Tick += (sender, args) => Recheck();
                slow.Start();
            }
            Recheck();
        }
    }
    protected override void OnHandleDestroyed(EventArgs e) { UnregisterHotKey(Handle, 1); base.OnHandleDestroyed(e); }
    void Run()
    {
        try { icon.ShowBalloonTip(1500, "KVM MSI", "Cambiando a " + Kvm.Toggle(), ToolTipIcon.None); }
        catch (Exception ex) { icon.ShowBalloonTip(3000, "KVM MSI", ex.Message, ToolTipIcon.Error); }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) Run();
        else if (m.Msg == 0x219) { int w = m.WParam.ToInt32(); if (w == 0x8000 || w == 0x8004) { if (recheckTimer != null && !recheckTimer.Enabled) Log("aviso de USB: " + (w == 0x8000 ? "dispositivo conectado" : "dispositivo retirado")); ScheduleRecheck(); } }   // WM_DEVICECHANGE (un solo apunte por rafaga)
        else if (m.Msg == 0x218) { int w = m.WParam.ToInt32(); if (w == 7 || w == 18) ScheduleRecheck(); }              // WM_POWERBROADCAST: reanudado
        base.WndProc(ref m);
    }
    protected override void SetVisibleCore(bool v) { base.SetVisibleCore(false); }
}

static class Program
{
    static bool ParseHotkey(string s, out uint mod, out uint vk)
    {
        mod = 0; vk = 0;
        foreach (var part in s.ToLower().Split('+'))
        {
            var p = part.Trim();
            if (p == "ctrl") mod |= 2; else if (p == "shift") mod |= 4; else if (p == "alt") mod |= 1; else if (p == "win") mod |= 8;
            else if (p.Length == 1) vk = (uint)char.ToUpper(p[0]);
            else if (p.Length >= 2 && p[0] == 'f') vk = 0x70 + (uint)(int.Parse(p.Substring(1)) - 1);
            else return false;
        }
        return vk != 0 && mod != 0;
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    public static bool KeepAwake, AutoAwake;

    static void Startup(bool install)
    {
        string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "KVM MSI.lnk");
        if (!install) { if (File.Exists(lnk)) File.Delete(lnk); Console.WriteLine("Quitado del inicio de Windows."); return; }
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object sh = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
        Type st = sc.GetType();
        string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe });
        st.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "--auto-awake" });
        st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(exe) });
        st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "KVM MSI" });
        st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
        Console.WriteLine("Instalado en el inicio de Windows: " + exe + " --auto-awake\nAtajo: " + lnk + "\nSi mueves el .exe, vuelve a ejecutar --install-startup.");
    }

    const string HELP =
@"KVM MSI - cambia la entrada del monitor MSI MD342CQP (sin admin).

Uso:
  kvmswitch.exe                         residente en la bandeja; atajo Ctrl+Shift+1
  kvmswitch.exe --hotkey ctrl+alt+k     residente con otro atajo (ctrl, shift, alt, win + letra/numero/F1-F12)
  kvmswitch.exe --auto-awake            residente; mantiene la pantalla de ESTE equipo encendida solo mientras el monitor
                                        esta en el otro equipo (evita que el KVM falle al volver tras un reposo). Recomendado.
                                        Sin sondeo: Windows le avisa cuando el USB del monitor aparece o desaparece.
  kvmswitch.exe --keep-awake            residente y mantiene la pantalla siempre encendida
  kvmswitch.exe --install-startup       arranca solo con Windows (con --auto-awake), sin admin; --remove-startup lo quita
  kvmswitch.exe --toggle                alterna entre portatil y sobremesa y sale
  kvmswitch.exe --to hdmi1 | hdmi2 | dp | usbc     (desktop = dp, laptop = usbc)
  kvmswitch.exe --status                muestra la entrada activa
  kvmswitch.exe --help                  esta ayuda

Entradas: 0 hdmi1, 1 hdmi2 (sin verificar), 2 dp = sobremesa, 3 usbc = portatil.
Equivalente en Linux: kvmswitch.py  --to hdmi1 | hdmi2 | dp | usbc
";

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "/?"))
            {
                AttachConsole(-1);
                Console.Write(HELP);
                return 0;
            }
            if (args.Length > 0 && args[0] != "--hotkey" && args[0] != "--auto-awake" && args[0] != "--keep-awake") AttachConsole(-1);
            if (args.Length > 0 && args[0] == "--install-startup") { Startup(true); return 0; }
            if (args.Length > 0 && args[0] == "--remove-startup") { Startup(false); return 0; }
            if (args.Length > 0 && args[0] == "--status") { int v = Kvm.Status(); Console.WriteLine(Kvm.Name(v)); return 0; }
            if (args.Length > 0 && args[0] == "--toggle") { Console.WriteLine("-> " + Kvm.Toggle()); return 0; }
            if (args.Length > 1 && args[0] == "--to") { Kvm.Set(Kvm.ToValue(args[1])); return 0; }
            string hk = "ctrl+shift+1";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--keep-awake") KeepAwake = true;
                else if (args[i] == "--auto-awake") AutoAwake = true;
                else if (args[i] == "--hotkey" && i + 1 < args.Length) hk = args[++i];
            }
            uint mod, vk;
            if (!ParseHotkey(hk, out mod, out vk)) { MessageBox.Show("Atajo no valido: " + hk, "KVM MSI"); return 1; }
            bool created;
            using (new System.Threading.Mutex(true, "kvmswitch_single_instance", out created))
            {
                if (!created) return 0;
                Application.EnableVisualStyles();
                Application.Run(new HotkeyForm(mod, vk, hk));
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); if (args.Length == 0 || args[0] == "--hotkey") MessageBox.Show(ex.Message, "KVM MSI"); return 1; }
    }
}
