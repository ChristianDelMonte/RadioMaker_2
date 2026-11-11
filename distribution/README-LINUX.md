# RadioMaker — Guía de ejecución en Linux

Versión: 2.01 · Plataformas: Linux x64 (x86_64)
Distribuciones probadas: Debian 12+, Ubuntu 22.04+, Fedora 38+, Arch

## Requisitos

- Linux x64 (x86_64). ARM64 soportado a futuro.
- No requiere .NET instalado (viene incluido)
- ~500 MB de espacio libre en disco
- Servidor de audio: PulseAudio, PipeWire o ALSA
- Librerías nativas de Avalonia (probablemente ya instaladas):

  ```bash
  # Debian / Ubuntu
  sudo apt install libice6 libsm6 libfontconfig1 libgdiplus

  # Fedora / RHEL
  sudo dnf install libICE libSM fontconfig libgdiplus

  # Arch / Manjaro
  sudo pacman -S libice libsm fontconfig libgdiplus
  ```

## Instalación

Copiar `RadioMaker-linux.tar.gz` al disco local
(no ejecutar directamente desde el pendrive).

## Extraer:

```bash
tar -xzf RadioMaker-linux.tar.gz
cd RadioMaker-linux`
```

## Ejecutar el lanzador:

```bash
./run.sh
```

## Sobre run.sh

El script `run.sh` hace:

`chmod +x` sobre los binarios (necesario porque los permisos Unix se
pierden al pasar por un pendrive FAT32/exFAT)

Puedes ejecutar RadioMaker utilizando `run.sh` para setear / restaurar los permisos y luego ejecutar automaticamente la app o si prefieres hacerlo de manera normal manualmente:

```bash
chmod +x RadioMaker
chmod +x Addons/RMVoice/RMVoice
chmod +x Addons/RMWave/RMWave
./RadioMaker
```

## Estructura esperada del paquete

RadioMaker-linux/
├── RadioMaker                    ← ejecutable principal
├── run.sh                        ← lanzador (recomendado)
├── runtimes/
│   └── linux-x64/
│       └── native/
│           ├── libbass.so
│           ├── libbassmix.so
│           └── libbass_fx.so
├── Addons/
│   ├── RMVoice/
│   │   └── RMVoice
│   └── RMWave/
│       └── RMWave
└── README-LINUX.md               ← este archivo

## Si algo no funciona

bash: `./run.sh:` Permission denied
El script no tiene bit de ejecución (pasó por FAT32/exFAT). Solución:

```bash:
run.sh
```

O bien:

```bash
chmod +x run.sh RadioMaker
./run.sh
```

## La app no arranca o se cierra al inicio: Revisá el log:

```bash
tail -50 ~/.config/RadioMaker/log/*.log
```
**Buscá líneas con [ERROR] o [FATAL].**

- DllNotFoundException: bass / libbass.so: cannot open shared object file.
Falta `libbass.so`.

Verificá:

```bash
ls runtimes/linux-x64/native/libbass*.so
```

Debería listar `libbass.so, libbassmix.so, libbass_fx.so`.

En algunos casos es necesario copiar las librerias de
`runtimes/linux-x64/native/libbass*.so` hacia la carpeta base de radiomaker.

- The type initializer for 'Avalonia...Skia' threw an exception
Faltan librerías de Avalonia en el sistema.

Instalar:

```bash
# Debian / Ubuntu
sudo apt install libice6 libsm6 libfontconfig1 libgdiplus
```

- BASS_ERROR_DEVICE o no hay sonido
El servidor de audio no tiene dispositivos activos.

Verificar:

```bash
# PulseAudio
pactl info

# PipeWire
pw-cli info 0

# ALSA
aplay -l
```

Si no hay salida, habilitá el servidor de audio y volvé a intentar.

## Otros errores que pueden surgir con respecto al sistema de audio:

- ERROR: ALSA lib pcm_dmix.c:1000:(snd_pcm_dmix_open) unable to open slave.
Ocurre en Linux cuando la capa de audio ALSA intenta usar un dispositivo mezclador (dmix) y no puede acceder a la tarjeta de sonido o al servidor de audio predeterminado (como PipeWire o PulseAudio)

Instalar:

```bash
## Arch Linux:
sudo pacman -S pipewire-alsa

## Ubuntu / Debian:
sudo apt install pipewire-audio-client-libraries
```

## Wayland vs X11
Avalonia 12 funciona en ambos. Si Wayland da problemas gráficos, forzá X11:

```bash
GDK_BACKEND=x11 ./run.sh
```

Permisos de audio (grupo audio)
Si tu usuario no está en el grupo audio, la app puede no detectar el
dispositivo de salida:

```bash
sudo usermod -a -G audio $USER
# cerrar sesión y volver a entrar
```

## Complementos (RMVoice / RMWave)
Se abren desde el menú Complementos de RadioMaker (recomendado), o directamente:

```bash
./Addons/RMVoice/RMVoice
./Addons/RMWave/RMWave
```

Sobre BASS y licencia
BASS es gratis para uso no comercial. Para uso comercial se requiere una licencia de `un4seen.com`.
Ver `README.md` del proyecto para más detalles.

## Soporte

- Documentación técnica completa: ver `README.md` en el repositorio
- [github.com/ChristianDelMonte/RadioMaker_2/](https://github.com/ChristianDelMonte/RadioMaker_2/)
- Reportar problemas: [github.com/ChristianDelMonte/RadioMaker_2/issues](https://github.com/ChristianDelMonte/RadioMaker_2/issues)

## Para Donaciones monetarias y apoyar el proyecto comunicarse via correo electronico:
<strong>creadig@gmail.com / creadig@hotmail.com</strong>