"""
EL PUENTE: el collar Omi como micrófono normal de Windows, sin la app de Ü de por medio.

Usa el SDK OFICIAL de Omi (omi-sdk en PyPI) para hablar por Bluetooth con el collar y
decodificar Opus a PCM. Lo único que este script añade es el último tramo: escribir ese
PCM en un cable de audio virtual (VB-Cable) para que Windows lo enseñe como un micrófono
más — usable por Zoom, Discord, Teams, lo que sea. Cero relación con U.exe.

Requisitos, ya resueltos en este entorno:
  - Python 3.12 (el SDK fija bleak==0.22.3, que no tiene rueda para 3.12+... salvo que
    aquí SÍ la tiene; 3.14 no la tenía, por eso el venv usa 3.12).
  - opus.dll: opuslib busca la librería nativa con ctypes.util.find_library('opus'), que
    en Windows siempre devuelve None si la DLL no está en el PATH. PyOgg la trae empaquetada
    (pyogg/opus.dll); por eso su carpeta se añade al PATH ANTES de importar el SDK.
  - VB-Cable instalado: crea "CABLE Input" (salida, donde este script escribe) y
    "CABLE Output" (entrada, lo que Windows enseña como micrófono).
"""

import sys

# LO PRIMERO DE TODO, antes de cualquier otro import: sounddevice (PortAudio/WASAPI) deja
# el hilo en modo STA como efecto secundario de solo CONSULTAR los dispositivos de audio.
# bleak necesita MTA para que sus llamadas WinRT asíncronas se completen en un programa de
# consola sin bucle de mensajes — si el hilo queda en STA, revienta con «Thread is
# configured for Windows GUI but callbacks are not working» (medido, 2026-08-14).
#
# `sys.coinit_flags = 0` NO basta: solo cambia lo que haría `pythoncom` la próxima vez que
# inicialice, pero aquí no es pythoncom quien inicializa — es WinRT, y lo hace en cuanto
# sounddevice toca audio. `uninitialize_sta()` de bleak tampoco basta: deja el hilo SIN
# apartamento, y WinRT lo vuelve a poner en STA por su cuenta en la siguiente llamada.
#
# Lo único que se sostiene es fijar el apartamento ANTES de que nadie más lo toque, con la
# API nativa de WinRT — y tiene que ser la primerísima línea ejecutable, antes incluso de
# importar sounddevice o numpy.
from winrt import _winrt
_winrt.init_apartment(0)  # 0 = MTA (COINIT_MULTITHREADED)

import asyncio
import os
from pathlib import Path

# opus.dll ANTES que nada: el SDK oficial hace `from opuslib import Decoder` en cuanto
# se importa, y opuslib falla en el import si no encuentra la DLL — no hay forma de
# retrasarlo hasta después de importar omi.
import pyogg
_dll_dir = str(Path(pyogg.__file__).parent)
os.add_dll_directory(_dll_dir)
os.environ["PATH"] = _dll_dir + os.pathsep + os.environ["PATH"]

import numpy as np
import sounddevice as sd
from bleak import BleakScanner, BleakClient
from omi import OmiOpusDecoder

CHAR_AUDIO = "19b10001-e8f2-537e-4f6c-d104768a1214"
SVC_OMI = "19b10000-e8f2-537e-4f6c-d104768a1214"
HZ = 16000


def buscar_cable_input() -> int:
    """El índice de "CABLE Input" cambia según cuántas veces se reinstale VB-Cable, así
    que se busca por nombre en vez de fijarlo a mano — un índice fijo es el primer sitio
    donde esto se rompería silenciosamente si alguien reinstala el driver."""
    for i, d in enumerate(sd.query_devices()):
        if d["max_output_channels"] > 0 and d["name"].startswith("CABLE Input"):
            return i
    raise RuntimeError('No se encontró "CABLE Input". ¿Está VB-Cable instalado?')


async def buscar_collar(segundos: float = 10.0) -> str:
    """Rastrea por el UUID del servicio Omi, no por nombre: un nombre puede coincidir
    por casualidad, el UUID del servicio no."""
    print(f"Buscando el collar ({segundos:.0f} s)...")
    dispositivos = await BleakScanner.discover(timeout=segundos, return_adv=True)
    for direccion, (dispositivo, anuncio) in dispositivos.items():
        uuids = [u.lower() for u in (anuncio.service_uuids or [])]
        if SVC_OMI in uuids:
            print(f"  encontrado: {dispositivo.name or '(sin nombre)'} [{direccion}]")
            return direccion
    raise RuntimeError("No apareció ningún collar Omi. Enciéndelo y acércalo.")


async def main() -> None:
    idx_salida = buscar_cable_input()
    info = sd.query_devices(idx_salida)
    print(f'Escribiendo en: "{info["name"]}" (dispositivo {idx_salida})')

    direccion = await buscar_collar()

    decoder = OmiOpusDecoder()
    stream = sd.OutputStream(device=idx_salida, samplerate=HZ, channels=1, dtype="int16")
    stream.start()

    tramas = 0

    def al_llegar_audio(_sender, data: bytearray) -> None:
        nonlocal tramas
        # El SDK oficial ya quita los 3 bytes de cabecera y decodifica Opus → PCM16.
        pcm = decoder.decode_packet(data)
        if not pcm:
            return
        tramas += 1
        if tramas % 100 == 0:
            print(f"  {tramas} tramas entregadas al micrófono virtual")
        muestras = np.frombuffer(pcm, dtype=np.int16).reshape(-1, 1)
        stream.write(muestras)

    print(f"Conectando a {direccion}...")
    async with BleakClient(direccion) as client:
        await client.start_notify(CHAR_AUDIO, al_llegar_audio)
        print("Conectado. Habla al collar — debería oírse por \"CABLE Output\" en cualquier app.")
        print("Ctrl+C para salir.")
        try:
            while True:
                await asyncio.sleep(3600)
        except asyncio.CancelledError:
            pass

    stream.stop()
    stream.close()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nDetenido.")
    except Exception as e:
        print(f"\n{type(e).__name__}: {e}", file=sys.stderr)
        sys.exit(1)
