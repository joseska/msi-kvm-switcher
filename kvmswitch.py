#!/usr/bin/env python3
"""Cambia el KVM del monitor MSI MD342CQP entre sobremesa (2) y portatil (3). Linux, sin dependencias.

Protocolo (el mismo que usa la app de MSI): informe HID id=1, 64 bytes, ASCII "5b0050000N\\r".
Lectura del estado: "5800500\\r" -> respuesta "5b0050000N".

Uso:
  kvmswitch.py --toggle | --status | --to hdmi1|hdmi2|dp|usbc   (desktop = dp, laptop = usbc)
  kvmswitch.py --print-udev      regla udev (instalar una vez con sudo, ver abajo)
  kvmswitch.py --install-shortcut [ctrl+shift+1]   atajo en GNOME/Zorin
  kvmswitch.py --auto-awake      residente: mantiene la pantalla de ESTE equipo encendida solo mientras el monitor esta
                                 en el otro equipo (evita que el KVM falle al volver tras un reposo). Recomendado.
                                 Sin sondeo: espera los avisos de udev cuando el USB del monitor aparece o desaparece.
  kvmswitch.py --install-autostart | --remove-autostart   arranca --auto-awake al iniciar sesion (GNOME/Zorin)
"""
import ctypes, glob, os, select, shlex, shutil, signal, subprocess, sys, tempfile, time

VID, PID = "00001462", "00003FA4"
DESKTOP, LAPTOP = 2, 3

UDEV = 'KERNEL=="hidraw*", ATTRS{idVendor}=="1462", ATTRS{idProduct}=="3fa4", TAG+="uaccess"\n'


def find_hidraw():
    for uevent in glob.glob("/sys/class/hidraw/hidraw*/device/uevent"):
        txt = open(uevent).read().upper()
        if f"HID_ID=0003:{VID}:{PID}" in txt:
            return "/dev/" + uevent.split("/")[4]
    raise SystemExit("Monitor MSI (1462:3FA4) no encontrado. Esta conectado el USB?")


def talk(fd, cmd):
    os.write(fd, b"\x01" + (cmd + "\r").encode().ljust(64, b"\0"))
    if not select.select([fd], [], [], 1.5)[0]:
        return None
    data = os.read(fd, 65)
    return data[1:].split(b"\r")[0].decode(errors="replace") if data else None


def open_dev():
    path = find_hidraw()
    try:
        return os.open(path, os.O_RDWR)
    except PermissionError:
        raise SystemExit(f"Sin permiso para {path}. Ejecuta una vez: kvmswitch.py --print-udev | sudo tee /etc/udev/rules.d/99-msi-kvm.rules && sudo udevadm control --reload && sudo udevadm trigger  (y reconecta el USB)")


def status():
    fd = open_dev()
    try:
        r = talk(fd, "5800500")
    finally:
        os.close(fd)
    return int(r[-1], 16) if r and r.startswith("5b0050") else -1


def set_input(value):
    fd = open_dev()
    try:
        talk(fd, f"5b0050000{value}")
    finally:
        os.close(fd)


def toggle():
    target = DESKTOP if status() == LAPTOP else LAPTOP
    set_input(target)
    return "portatil" if target == LAPTOP else "sobremesa"


def install_shortcut(combo):
    key = "".join({"ctrl": "<Primary>", "shift": "<Shift>", "alt": "<Alt>", "super": "<Super>"}.get(p, p) for p in combo.lower().split("+"))
    base = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/kvmswitch/"
    schema = "org.gnome.settings-daemon.plugins.media-keys"
    cur = subprocess.check_output(["gsettings", "get", schema, "custom-keybindings"], text=True).strip()
    if base not in cur:
        items = [] if cur in ("@as []", "[]") else eval(cur)
        items.append(base)
        subprocess.check_call(["gsettings", "set", schema, "custom-keybindings", str(items)])
    s = f"{schema}.custom-keybinding:{base}"
    subprocess.check_call(["gsettings", "set", s, "name", "KVM MSI"])
    subprocess.check_call(["gsettings", "set", s, "command", f"python3 {shlex.quote(os.path.abspath(__file__))} --toggle"])
    subprocess.check_call(["gsettings", "set", s, "binding", key])
    print("Atajo instalado:", key)


def present():
    """True si el USB del monitor esta conectado a este equipo (el KVM esta aqui).
    KVM_FAKE_AWAY=1 solo sirve para probar la logica sin mover el monitor."""
    if os.environ.get("KVM_FAKE_AWAY") == "1":
        return False
    for uevent in glob.glob("/sys/class/hidraw/hidraw*/device/uevent"):
        try:
            if f"HID_ID=0003:{VID}:{PID}" in open(uevent).read().upper():
                return True
        except OSError:
            pass
    return False


def log(msg):
    line = time.strftime("%Y-%m-%d %H:%M:%S ") + msg
    print(line, flush=True)
    try:
        with open(os.path.join(tempfile.gettempdir(), "kvmswitch.log"), "a") as f:
            f.write(line + "\n")
    except OSError:
        pass


def start_inhibitor():
    """Impide que se apague la pantalla / se suspenda mientras viva el proceso devuelto."""
    why = "KVM MSI: el monitor esta en el otro equipo"
    candidates = []
    if shutil.which("gnome-session-inhibit"):  # lo respeta GNOME/Zorin para el apagado de pantalla
        candidates.append(["gnome-session-inhibit", "--inhibit", "idle:suspend", "--reason", why, "sleep", "infinity"])
    if shutil.which("systemd-inhibit"):
        candidates.append(["systemd-inhibit", "--what=idle:sleep", "--who=kvmswitch", f"--why={why}", "--mode=block", "sleep", "infinity"])
    for cmd in candidates:
        try:
            # PDEATHSIG: si este script muere, el inhibidor muere tambien y no se queda la pantalla encendida para siempre.
            return subprocess.Popen(cmd, preexec_fn=lambda: ctypes.CDLL("libc.so.6").prctl(1, signal.SIGTERM))
        except OSError:
            continue
    return None


def wait_for_usb_change(monitor):
    """Bloquea hasta que udev avise de un cambio en dispositivos hidraw (sin sondeo). Sin udevadm: comprueba cada 30 s."""
    if monitor is None:
        time.sleep(30)
        return
    if not monitor.stdout.readline():     # EOF: udevadm murio
        time.sleep(30)
        return
    time.sleep(0.5)                        # deja terminar el cambio de USB; un aviso de mas solo causa una comprobacion inocua


def auto_awake():
    proc, away = None, None
    signal.signal(signal.SIGTERM, lambda *a: sys.exit(0))
    monitor = None
    if shutil.which("udevadm"):
        monitor = subprocess.Popen(["udevadm", "monitor", "--udev", "--subsystem-match=hidraw"],
                                   stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, bufsize=1)
        log("vigilando aparicion/desaparicion de USB con udev (sin sondeo)")
    else:
        log("AVISO: no hay udevadm; comprobando cada 30 s")
    try:
        while True:
            now_away = not present()
            if now_away != away:
                away = now_away
                if away:
                    proc = start_inhibitor()
                    log("monitor en el otro equipo: pantalla mantenida encendida" if proc else
                        "monitor en el otro equipo, pero no hay gnome-session-inhibit ni systemd-inhibit: no puedo mantener la pantalla encendida")
                else:
                    if proc:
                        proc.terminate()
                        proc.wait()
                        proc = None
                    log("monitor en este equipo: reposo normal")
            wait_for_usb_change(monitor)
    finally:
        if proc:
            proc.terminate()
        if monitor:
            monitor.terminate()


def autostart_path():
    return os.path.join(os.environ.get("XDG_CONFIG_HOME", os.path.expanduser("~/.config")), "autostart", "kvmswitch.desktop")


def install_autostart():
    p = autostart_path()
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w") as f:
        f.write("[Desktop Entry]\nType=Application\nName=KVM MSI\n"
                f"Exec=python3 {shlex.quote(os.path.abspath(__file__))} --auto-awake\n"
                "X-GNOME-Autostart-enabled=true\n")
    print("Instalado en el inicio de sesion:", p, "\nSi mueves el script, vuelve a ejecutar --install-autostart.")


def remove_autostart():
    p = autostart_path()
    if os.path.exists(p):
        os.remove(p)
    print("Quitado del inicio de sesion.")


def main(a):
    if a[:1] == ["--status"]:
        print({0: "HDMI 1 (0, sin verificar)", 1: "HDMI 2 (1, sin verificar)", 2: "DisplayPort / sobremesa (2)", 3: "USB-C / portatil (3)"}.get(status(), "desconocido"))
    elif a[:1] == ["--toggle"]:
        print("->", toggle())
    elif a[:1] == ["--to"] and len(a) > 1:
        # 0 HDMI1, 1 HDMI2, 2 DP, 3 USB-C. Solo 2 y 3 estan verificados; 0 y 1 son hipotesis sin probar.
        names = {"hdmi1": 0, "hdmi2": 1, "dp": 2, "desktop": 2, "sobremesa": 2, "usbc": 3, "laptop": 3, "portatil": 3}
        if a[1].lower() not in names:
            raise SystemExit("Entrada desconocida. Usa: " + ", ".join(names))
        set_input(names[a[1].lower()])
    elif a[:1] == ["--print-udev"]:
        sys.stdout.write(UDEV)
    elif a[:1] == ["--install-shortcut"]:
        install_shortcut(a[1] if len(a) > 1 else "ctrl+shift+1")
    elif a[:1] == ["--auto-awake"]:
        auto_awake()
    elif a[:1] == ["--install-autostart"]:
        install_autostart()
    elif a[:1] == ["--remove-autostart"]:
        remove_autostart()
    else:
        print(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
