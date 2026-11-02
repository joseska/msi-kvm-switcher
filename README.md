# MSI KVM Switcher

Cambia la entrada del monitor y el KVM (teclado y ratón) entre dos equipos con un atajo de teclado, **sin permisos de administrador** y **sin la app oficial de MSI**.

> **English summary:** a tiny, dependency-free tool (a C# tray app for Windows, a Python script for Linux) that switches the input/KVM of an **MSI MD342CQP** monitor between two computers with a hotkey, no admin rights needed. It talks to the monitor over USB HID using a protocol observed from MSI's own app. It also includes an `--auto-awake` mode that fixes a failure when switching back to a computer whose screen went to sleep. Tested only on one monitor and one setup. **Built with vibe coding** (written by an AI, see [below](#hecho-con-vibe-coding)), use at your own risk. Everything below is in Spanish; the section ["Adaptarlo a tu monitor"](#adaptarlo-a-tu-monitor-o-a-tu-montaje) explains how to port it.

- Windows: [`kvmswitch.cs`](kvmswitch.cs) → `kvmswitch.exe`, un programa de bandeja de ~47 KB sin dependencias.
- Linux: [`kvmswitch.py`](kvmswitch.py), un script de Python sin dependencias.

Nace de este montaje: sobremesa por DisplayPort y portátil de trabajo por USB-C, con teclado y ratón conectados al monitor. La app oficial de MSI (Productivity Intelligence) hace esto con un atajo, pero solo funciona en Windows y se instala solo como administrador; esto cubre Windows y Linux sin permisos especiales.

## Hecho con vibe coding

Este proyecto se ha hecho con **vibe coding**: el código lo ha escrito una IA (Claude, de Anthropic, usando Claude Code) a partir de una conversación en lenguaje natural. La persona que lo encargó dirigió el trabajo y probó el resultado en sus dos equipos, pero:

- no ha pasado una revisión de código formal ni tiene pruebas automáticas;
- está probado en **un solo monitor y un solo montaje** (ver [Estado](#estado));
- en algunas partes las explicaciones de este README son hipótesis razonadas a partir de mediciones, no documentación oficial.

Úsalo bajo tu responsabilidad. Se agradecen correcciones y pruebas con otros modelos.

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

- Si la app oficial de MSI está abierta, tiene el mismo atajo registrado: ciérrala.
- Algunos antivirus o políticas corporativas pueden bloquear un `.exe` sin firmar. Puedes compilarlo tú mismo (ver abajo) o usar `--toggle` desde otro lanzador.
- Con `--auto-awake` se anotan los cambios en `%TEMP%\kvmswitch.log`.

### Compilar

Windows ya trae el compilador de C# (no hace falta Visual Studio):

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe -target:winexe -win32icon:kvmswitch.ico -resource:kvmswitch.ico,kvmswitch.ico -r:System.Windows.Forms.dll -r:System.Drawing.dll -out:kvmswitch.exe kvmswitch.cs
```

El icono se genera con [`mkicon.cs`](mkicon.cs) (compílalo con `-r:System.Drawing.dll` y ejecútalo; escribe `kvmswitch.ico`).

## Uso en Linux (probado solo con simulación)

Necesita acceso a `/dev/hidraw*`. Una sola vez, con `sudo`, instala la regla udev y reconecta el USB del monitor:

```bash
python3 kvmswitch.py --print-udev | sudo tee /etc/udev/rules.d/99-msi-kvm.rules && sudo udevadm control --reload && sudo udevadm trigger
```

Después:

```bash
python3 kvmswitch.py --install-shortcut          # atajo Ctrl+Shift+1 en GNOME
python3 kvmswitch.py --install-autostart         # arranca --auto-awake al iniciar sesión
python3 kvmswitch.py --toggle | --status | --to hdmi1|hdmi2|dp|usbc
```

El atajo usa `gsettings` de GNOME; en otros escritorios, asigna a mano un atajo que ejecute `python3 /ruta/kvmswitch.py --toggle`.

## Por qué existe `--auto-awake`

Si el sobremesa lleva unos minutos con el monitor en el portátil, Windows apaga su pantalla y deja de dar señal por DisplayPort. Al volver con la app, el monitor selecciona DP pero **no mueve el USB hasta ver señal**, y sin teclado ni ratón el sobremesa no despierta: se queda en negro con "sin señal". Pulsar el botón físico del monitor sí funciona porque mueve el USB sin esperar señal.

`--auto-awake` evita el bucle: mientras el monitor está en el otro equipo, mantiene despierta la pantalla de este equipo (así sigue emitiendo señal); con el monitor aquí, el reposo es el normal. Medido en este montaje: con la pantalla despierta, el USB llega unos 2 s después del vídeo.

No hace sondeo. En Windows se registra para los avisos del sistema de aparición/desaparición de USB (`WM_DEVICECHANGE`); en Linux espera eventos de `udevadm monitor`. En reposo gasta 0 ms de CPU.

## Protocolo

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

Escribir y leer el dispositivo HID no requiere administrador. El programa solo envía esos dos tipos de orden (cambiar entrada y leer estado); no escribe ningún otro registro del monitor.

## Adaptarlo a tu monitor o a tu montaje

Si tu monitor es otro modelo, o tienes otras entradas, esto es lo que hay que cambiar y cómo averiguarlo.

### 1. Valores que están en el código

| Qué | Dónde |
|---|---|
| VID y PID del monitor | `const ushort VID, PID` en [`kvmswitch.cs`](kvmswitch.cs); `VID, PID` y la regla `UDEV` en [`kvmswitch.py`](kvmswitch.py) |
| Valor de cada entrada (`DESKTOP`, `LAPTOP`) y nombres (`hdmi1`, `usbc`…) | `ToValue()` y `Name()` en `kvmswitch.cs`; diccionario `names` en `kvmswitch.py` |
| Código de la orden (`0050`) y formato (`5b…`, `58…`) | funciones `Set()`, `Status()` y `Toggle()` en `kvmswitch.cs`; `status()` y `set_input()` en `kvmswitch.py` |
| Atajo por defecto | variable `hk` en `Main()` de `kvmswitch.cs`; argumento de `--install-shortcut` en el `.py` |

Las dos implementaciones usan el mismo protocolo: si cambias algo en una, cámbialo en la otra.

### 2. Encontrar el VID/PID

- Windows: `Get-PnpDevice -PresentOnly | Where-Object InstanceId -match '^HID\\VID_' | Select FriendlyName, InstanceId` (PowerShell) y busca el que aparezca al conectar el USB del monitor.
- Linux: `lsusb` (el monitor sale como un hub y un dispositivo HID).

### 3. Descubrir el comando de tu monitor

Es lo que se hizo aquí, por observación y sin tocar el firmware:

1. Ejecuta la app oficial del fabricante (en este caso, como administrador) con el monitor conectado.
2. Engánchate a su proceso con [Frida](https://frida.re/) y registra las escrituras de 64 bytes a dispositivos HID. Ejemplo mínimo:

   ```js
   // frida -p <pid de la app del fabricante>
   const nt = Process.getModuleByName('ntdll.dll');
   Interceptor.attach(nt.getExportByName('NtWriteFile'), { onEnter(a) {
     const n = a[6].toInt32();                       // longitud
     if (n === 64) console.log(hexdump(a[5], { length: 16, header: false }));  // informe HID
   }});
   ```
3. Pulsa el atajo de la app oficial y mira qué escrituras aparecen justo después. Al arrancar, la app lanza decenas de consultas (aquí empezaban por `58…`); filtra esas y busca la que cambia con la acción.
4. Alternativa sin Frida: capturar el tráfico USB con Wireshark + USBPcap.
5. Confirma el valor de cada entrada leyendo el estado con tu propia herramienta mientras cambias la entrada con los botones del monitor (en este proyecto, `kvmswitch.exe --status`).

Notas del método:

- En este monitor, la app oficial **no** usa DDC/CI para cambiar de entrada.
- En el perfil de la app oficial (`profile\Standard\*.txt`) hay una tabla de nombres (`InputSource`, `AutoScan`, `…_KVM_Setting_Selection`…) con números, pero esos números **no** sirven como tabla de órdenes: aquí, la orden que cambia de entrada es la `0x50`, que en ese perfil figura como `GrayLevel`, y `InputSource` (`0x14`) no es la que usa el atajo.
- No envíes órdenes desconocidas al monitor "a ver qué pasa": podrías cambiar ajustes persistentes. Primero observa lo que envía la app oficial.

### 4. Otros modelos de MSI

Los monitores de MSI compatibles con la app oficial parecen compartir familia de protocolo, pero **no está comprobado**. Si pruebas otro modelo y funciona (o no), cuéntalo en un issue con el VID/PID y la lista de valores que has medido.

### 5. Otras personalizaciones

- Varias entradas (por ejemplo un tercer equipo por HDMI): usa `--to hdmi1|hdmi2` una vez comprobados sus valores, o añade otro atajo con otro `--to`.
- Comportamiento de `--auto-awake`: en Windows es `SetThreadExecutionState`; en Linux es un inhibidor de reposo (`gnome-session-inhibit`, o `systemd-inhibit` si no existe).
- Icono: edita el dibujo en [`mkicon.cs`](mkicon.cs) y recompila.

## Estado

Probado en Windows (sobremesa y portátil): cambio en los dos sentidos con el atajo, `--auto-awake` con eventos reales de USB y vuelta correcta tras ~7 minutos de reposo.

**Sin probar:**

- El script de Linux: solo se comprobó la sintaxis y la lógica con eventos simulados. Falta verificar la regla udev, el atajo, `gnome-session-inhibit` y el autoarranque en un sistema real.
- `hdmi1` y `hdmi2`.
- `--auto-awake` en el portátil con el monitor en el sobremesa.

Solo se ha probado con el MSI MD342CQP.

## Herramientas de diagnóstico

No hacen falta para el uso diario.

- [`kvmtiming.cs`](kvmtiming.cs): envía un cambio de entrada (o solo observa con `-1`) y anota con milisegundos cuándo desaparecen el vídeo (DDC) y el USB. Uso: `kvmtiming.exe <0-3|-1> <segundos>`.
- [`kvmwatch.cs`](kvmwatch.cs): registrador (`kvmwatch.log`) del USB del monitor, la entrada de teclado/ratón, el estado de la pantalla de Windows y DDC.

## Licencia

[MIT](LICENSE).

## Aviso legal

Este proyecto no está afiliado ni respaldado por MSI. "MSI" y "Productivity Intelligence" son marcas de sus propietarios. El protocolo se obtuvo observando la comunicación de la app oficial con un monitor propio; este repositorio no contiene código ni binarios de MSI. Se ofrece tal cual, sin garantía de ningún tipo.
