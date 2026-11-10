
# RadioMaker — Guía de ejecución en Windows

Versión: 2.01 · Plataformas: Windows 10 (build 1809+) / Windows 11
Arquitecturas: x64 · ARM64

## Requisitos

- Windows 10 build 1809 o superior, o Windows 11
- No requiere .NET instalado (viene incluido)
- ~500 MB de espacio libre en disco
- Dispositivo de audio configurado en Windows

## Instalación

1. Extraer el contenido del `.zip` en una carpeta local
   (por ejemplo `C:\RadioMaker\`). **No ejecutar desde el .zip directamente.**
2. Ejecutar `RadioMaker.exe`.

No hay instalador. No hay registro de sistema. Para desinstalar, borrá la
carpeta.

## Sobre el primer arranque

Windows puede mostrar un aviso de **Windows SmartScreen**:

> "Windows protegió su PC — Microsoft Defender SmartScreen impidió que se
> iniciara una aplicación no reconocida."

Esto es esperado porque el ejecutable no está firmado con un certificado
comercial. Solución:

1. Click en **"Más información"**
2. Click en **"Ejecutar de todas formas"**

En futuras versiones firmadas con un certificado EV, este aviso desaparece.

## Estructura esperada del paquete

RadioMaker-windows-x64/
├── RadioMaker.exe ← ejecutable principal
├── runtimes/
│ └── win-x64/
│ └── native/
│ ├── bass.dll
│ ├── bassmix.dll
│ └── bass_fx.dll
├── Addons/
│ ├── RMVoice/
│ │ └── RMVoice.exe
│ └── RMWave/
│ └── RMWave.exe
└── README-WINDOWS.md ← este archivo

## Si algo no funciona

### La app no arranca o se cierra al inicio

Revisá el log en:
`%APPDATA%\RadioMaker\log\`

Ahí hay archivos `rms-log-YYYY-MM-DD.log`. Buscá líneas con `[ERROR]`.

### No se pudo inicializar BASS

Falta un dispositivo de audio, o Windows no tiene tarjeta de sonido
habilitada. Verificá en **Configuración → Sistema → Sonido**.

### DllNotFoundException: bass

Falta `runtimes\win-x64\native\bass.dll`. Verificá que la carpeta
`runtimes/` esté completa al extraer el paquete.

### La app abre pero no hay sonido

1. Verificá el volumen del sistema (ícono de parlante en la bandeja)
2. Dentro de la app: **Configuración → Buses de salida**
3. Click en **Probar tono** en el bus activo
4. Si no suena, revisá el dispositivo seleccionado

### Antivirus bloquea el .exe

Algunos antivirus (Avast, Kaspersky, Windows Defender con heurística
agresiva) bloquean ejecutables no firmados. Agregá una exclusión para la
carpeta `RadioMaker\` en tu antivirus.

## Complementos (RMVoice / RMWave)

Se abren desde el menú **Complementos** de RadioMaker, o directamente ejecutando los `.exe` de la carpeta `Addons/`.

Si SmartScreen también bloquea los addons, aplicar el mismo paso de
"Más información → Ejecutar de todas formas".

## Firma de código

Los binarios **no están firmados**. Para distribución a terceros se
requiere un certificado de firma de código (no incluido).

## Soporte

- Documentación técnica completa: ver `README.md` en el repositorio
- Reportar problemas: en el repositorio del proyecto