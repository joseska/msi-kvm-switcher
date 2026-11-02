# MSI KVM Switcher

Cambia la entrada (y con ella el KVM de teclado y ratón) de un monitor **MSI MD342CQP** entre dos equipos con un atajo de teclado, **sin permisos de administrador** y sin la app oficial de MSI (Productivity Intelligence), que solo funciona en Windows y exige instalarse como administrador.

- Windows: [`kvmswitch.exe`](kvmswitch.cs), un programa de bandeja de ~47 KB sin dependencias.
- Linux (probado solo con simulación, ver [Estado](#estado)): [`kvmswitch.py`](kvmswitch.py), un script de Python sin dependencias.

Pensado para este montaje: sobremesa por DisplayPort y portátil por USB-C, con teclado y ratón conectados al monitor.

## Qué hace

Con el programa en marcha en **los dos equipos**, el mismo atajo (por defecto `Ctrl+Shift+1`) alterna el monitor:

1. Lee qué entrada tiene activa el monitor.
2. Si está en el portátil (USB-C), va al sobremesa (DP). En cualquier otro caso, va al portátil.

Como lee el estado del monitor, el mismo programa sirve en ambos equipos y no hay que configurar "dónde estoy".

## Uso en Windows

No hace falta instalar nada. Copia `kvmswitch.exe` a una carpeta fija y ejecútalo (doble clic): queda un icono en la bandeja.

```
kvmswitch.exe                         residente en la bandeja; atajo Ctrl+Shift+1
kvmswitch.exe --hotkey ctrl+alt+k     residente con otro atajo (ctrl, shift, alt, win + letra/número/F1-F12)
kvmswitch.exe --auto-awake            residente + mantiene la pantalla despierta si el monitor está en el otro equipo (recomendado)
kvmswitch.exe --keep-awake            residente + pantalla siempre encendida
kvmswitch.exe --install-startup       arranca con Windows (con --auto-awake), sin admin; --remove-startup lo quita
kvmswitch.exe --toggle                alterna una vez y sale
kvmswitch.exe --to hdmi1|hdmi2|dp|usbc    (desktop = dp, laptop = usbc)
kvmswitch.exe --status                muestra la entrada activa
kvmswitch.exe --help
```

Recomendado en cada equipo: copiar el `.exe` a una carpeta fija y ejecutar `kvmswitch.exe --install-startup`. Si luego mueves el `.exe`, repite el comando.

Notas:

- Si la app de MSI está abierta, tiene el mismo atajo registrado: ciérrala.
- Algunos antivirus corporativos pueden bloquear un `.exe` sin firmar.
- Con `--auto-awake` se anotan los cambios en `%TEMP%\kvmswitch.log`.

### Compilar

Windows ya trae el compilador de C# (no hace falta Visual Studio):

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe -target:winexe -win32icon:kvmswitch.ico -resource:kvmswitch.ico,kvmswitch.ico -r:System.Windows.Forms.dll -r:System.Drawing.dll -out:kvmswitch.exe kvmswitch.cs
```

El icono se genera con [`mkicon.cs`](mkicon.cs) (compílalo con `-r:System.Drawing.dll` y ejecútalo).

## Uso en Linux (Zorin / GNOME)

Necesita acceso a `/dev/hidraw*`. Una sola vez, con `sudo`, instala la regla udev y reconecta el USB del monitor:

```bash
python3 kvmswitch.py --print-udev | sudo tee /etc/udev/rules.d/99-msi-kvm.rules && sudo udevadm control --reload && sudo udevadm trigger
```

Después:

```bash
python3 kvmswitch.py --install-shortcut          # atajo Ctrl+Shift+1 en GNOME/Zorin
python3 kvmswitch.py --install-autostart         # arranca --auto-awake al iniciar sesión
python3 kvmswitch.py --toggle | --status | --to hdmi1|hdmi2|dp|usbc
```

## Por qué existe `--auto-awake`

Si el sobremesa lleva unos minutos con el monitor en el portátil, Windows apaga su pantalla y deja de dar señal por DisplayPort. Al volver con la app, el monitor selecciona DP pero **no mueve el USB hasta ver señal**, y sin teclado ni ratón el sobremesa no despierta: se queda en negro con "sin señal". Pulsar el botón físico del monitor sí funciona porque mueve el USB sin esperar señal.

`--auto-awake` evita el bucle: mientras el monitor está en el otro equipo, mantiene despierta la pantalla de este equipo (así sigue emitiendo señal); con el monitor aquí, el reposo es el normal de Windows. Medido: con la pantalla despierta, el USB llega unos 2 s después del vídeo.

No hace sondeo. En Windows se registra para los avisos del sistema de aparición/desaparición de USB (`WM_DEVICECHANGE`); en Linux espera eventos de `udevadm monitor`. En reposo gasta 0 ms de CPU.

## Protocolo (obtenido por ingeniería inversa de la app de MSI)

El monitor se controla por **USB HID** (VID `1462`, PID `3FA4`), no por DDC/CI: probando DDC (VCP `0x60`) el monitor acepta las órdenes pero no cambia de entrada.

- Informe HID con ID 1, 64 bytes, texto ASCII terminado en `\r`.
- Cambiar entrada: `5b0050000N\r`.
- Leer estado: `5800500\r` → responde `5b0050000N`.

| N | Entrada | Estado |
|---|---|---|
| 0 | HDMI 1 | hipótesis, **sin verificar** |
| 1 | HDMI 2 | hipótesis, **sin verificar** |
| 2 | DisplayPort (sobremesa) | verificado |
| 3 | USB-C (portátil) | verificado |

Escribir y leer el dispositivo HID no requiere administrador.

## Estado

Probado en Windows (sobremesa y portátil): cambio en los dos sentidos con el atajo, `--auto-awake` con eventos reales de USB y vuelta correcta tras ~7 minutos de reposo.

**Sin probar:**

- El script de Linux: solo se comprobó la sintaxis y la lógica con eventos simulados. Falta verificar la regla udev, el atajo, `gnome-session-inhibit` y el autoarranque en Zorin real.
- `hdmi1` y `hdmi2`.
- `--auto-awake` en el portátil con el monitor en el sobremesa.

Solo se ha probado con el MSI MD342CQP; en otros modelos los valores pueden cambiar.

## Herramientas de diagnóstico

No hacen falta para el uso diario.

- [`kvmtiming.cs`](kvmtiming.cs): envía un cambio de entrada (o solo observa con `-1`) y anota con milisegundos cuándo desaparecen el vídeo (DDC) y el USB. Uso: `kvmtiming.exe <0-3|-1> <segundos>`.
- [`kvmwatch.cs`](kvmwatch.cs): registrador (`kvmwatch.log`) del USB del monitor, la entrada de teclado/ratón, el estado de la pantalla de Windows y DDC.
