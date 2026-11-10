
# RadioMaker — Guía de ejecución en macOS

Versión: 2.01 · Plataformas: macOS 11 (Big Sur) o superior
Arquitecturas: Apple Silicon (arm64) · Intel (x64)

## Requisitos

- macOS 11 (Big Sur) o superior
- No requiere .NET instalado (viene incluido)
- ~500 MB de espacio libre en disco
- Dispositivo de audio configurado en macOS

## ⚠️ IMPORTANTE — Firma obligatoria

Los binarios NO están firmados con un certificado de Apple. En macOS
**Apple Silicon (M1/M2/M3/M4)** el sistema operativo **mata cualquier
binario sin firma** con el mensaje "damaged and can't be opened".

Por eso, antes de ejecutar la app por primera vez, hay que correr el
script `first-run.sh` que hace:

1. `chmod +x` (permisos de ejecución)
2. `codesign --force --sign -` (firma ad-hoc)
3. `xattr -d com.apple.quarantine` (limpia atributo de cuarentena)

Esto es normal y no requiere certificado de Apple. La firma ad-hoc es
suficiente para uso personal.

## Instalación

1. Copiar `RadioMaker-mac-arm64.tar.gz` (o `RadioMaker-mac-x64.tar.gz`)
   al disco local (**no ejecutar directamente desde el pendrive**).
2. Extraer:
   ```bash
   tar -xzf RadioMaker-mac-arm64.tar.gz
   cd RadioMaker-mac-arm64
   ```

Ejecutar la configuración inicial UNA SOLA VEZ:

```bash
first-run.sh
```

Debería terminar con:
```bash
Configuracion completada.
Ahora podes ejecutar:  ./run.sh
```

## Lanzar la app:

```bash
./run.sh
```

## Estructura esperada del paquete

RadioMaker-mac-arm64/
├── RadioMaker                    ← ejecutable principal
├── first-run.sh                  ← configurar UNA VEZ
├── run.sh                        ← lanzador
├── runtimes/
│   └── osx-arm64/
│       └── native/
│           ├── libbass.dylib
│           ├── libbassmix.dylib
│           └── libbass_fx.dylib
├── Addons/
│   ├── RMVoice/
│   │   └── RMVoice
│   └── RMWave/
│       └── RMWave
└── README-MAC.md                 ← este archivo

## Si algo no funciona

"damaged and can't be opened" o "code signature is invalid"
Significa que no corriste `first-run.sh`, o falló.
Solución manual:

```bash
codesign --force --sign - RadioMaker
codesign --force --sign - Addons/RMVoice/RMVoice
codesign --force --sign - Addons/RMWave/RMWave
```

"cannot be opened because the developer cannot be verified"
Gatekeeper.

Dos opciones:

Click derecho sobre RadioMaker → Open → Open otra vez.

O directamente:

```bash
xattr -d com.apple.quarantine RadioMaker
xattr -d com.apple.quarantine Addons/RMVoice/RMVoice
xattr -d com.apple.quarantine Addons/RMWave/RMWave
```

## La app arranca pero falla con DllNotFoundException
Faltan las `.dylib` de `BASS`.

Verificar:

```bash
ls runtimes/osx-arm64/native/libbass*.dylib
```
Debería listar `libbass.dylib, libbassmix.dylib, libbass_fx.dylib`.

## La app no arranca o se cierra al inicio
Revisá el log. Según la versión de .NET la ruta puede variar:

```bash
tail -50 ~/.config/RadioMaker/log/*.log 2>/dev/null
# o bien
tail -50 ~/Library/Application\ Support/RadioMaker/log/*.log 2>/dev/null
```
Buscá líneas con [ERROR] o [FATAL].

## No se pudo inicializar BASS
Falta dispositivo de audio, o el sistema no lo tiene habilitado.
Verificar en System Settings → Sound.

## Arquitectura — Apple Silicon vs Intel
El paquete `RadioMaker-mac-arm64.tar.gz` está compilado para Apple
Silicon (M1/M2/M3/M4).

Si tu Mac es Intel (fabricado antes de 2020), necesitás el paquete
`RadioMaker-mac-x64.tar.gz`. Para saber cuál tenés:

```bash
uname -m
```
arm64 → Apple Silicon → usar `RadioMaker-mac-arm64`

x86_64 → Intel → usar `RadioMaker-mac-x64`

## Complementos (RMVoice / RMWave)
Se abren desde el menú Complementos de RadioMaker, o directamente:

```bash
./Addons/RMVoice/RMVoice
./Addons/RMWave/RMWave
```

Si macOS bloquea alguno, aplicar `codesign --force --sign` - sobre el
binario correspondiente (igual que con el ejecutable principal).

## Sobre BASS y licencia
BASS es gratis para uso no comercial. Para uso comercial se requiere una licencia de `un4seen.com`.
Ver `README.md` del proyecto para más detalles.

## Soporte
- Documentación técnica completa: ver `README.md` en el repositorio
- Reportar problemas: en el repositorio del proyecto