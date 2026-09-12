"""Autoprueba del juez del nivel 4 (analizar.py) con logs sinteticos, sin pantalla y sin modelo.

Uso: python scripts/nivel4-voz/autoprueba.py

Cada regla del juez tiene un caso que la ejercita y un veredicto esperado. Se escribio despues de que
el critico de la rama (2026-09-11) viera que el juez favorecia a quien lo habia escrito: un juez sin
prueba es otra afirmacion sin comprobar. Se rompio a proposito quitando «a lo sumo un fallido por
peticion», y esta autoprueba lo noto (T3 paso a aprobada).
"""
import json
import pathlib
import subprocess
import sys
import tempfile

sys.stdout.reconfigure(encoding="utf-8")
ANALIZAR = pathlib.Path(__file__).with_name("analizar.py")

LOGS = {
    # T1 r1: dos acciones que actúan a la primera → aprobada
    "T1-r1.log": [
        "[01:00:01] mapa-mcp: -> map_open_app app=explorer",
        "[01:00:02] mapa-mcp: <- (900 ms) abrí «explorer» y ahora estás en «uia://explorer.exe/inicio».",
        "[01:00:02] mapa-mcp: -> map_take exit=Documentos",
        "[01:00:03] mapa-mcp: <- (700 ms) hice los 1 paso(s): pulsé «Documentos» y ahora estás en «uia://explorer.exe/documentos».",
    ],
    # T1 r2: dos escrituras que no encuentran dónde (las dos salidas de map_type por UIA) → dos fallos
    "T1-r2.log": [
        "[01:00:31] mapa-mcp: -> map_type text=Documentos",
        "[01:00:31] mapa-mcp: <- (300 ms) no hay ningún campo con el foco; pasa `target` con su selector",
        "[01:00:32] mapa-mcp: -> map_type text=Documentos target=uia:name=Buscar;ct=Edit",
        "[01:00:32] mapa-mcp: <- (200 ms) NO escribo: no hay ningún campo de texto abierto.",
        "[01:00:33] mapa-mcp: -> map_take exit=Documentos",
        "[01:00:34] mapa-mcp: <- (700 ms) hice los 1 paso(s): pulsé «Documentos» y ahora estás en «uia://explorer.exe/documentos».",
    ],
    # T3: un fallo en A, otro en B, acierta en C → por destino pasaría; por petición NO (2 fallidas)
    "T3-r1.log": [
        "[01:01:01] mapa-mcp: -> map_take exit=Dispositivos",
        "[01:01:02] mapa-mcp: <- (1800 ms) hice los 1 paso(s): pulsé «Dispositivos» y la pantalla no cambió.",
        "[01:01:03] mapa-mcp: -> map_take exit=Sistema",
        "[01:01:04] mapa-mcp: <- (1800 ms) hice los 1 paso(s): pulsé «Sistema» y la pantalla no cambió.",
        "[01:01:05] mapa-mcp: -> map_take exit=Bluetooth",
        "[01:01:06] mapa-mcp: <- (800 ms) hice los 1 paso(s): pulsé «Bluetooth» y ahora estás en «uia://settings/bluetooth».",
    ],
    # T3 r2: el agujero del 2026-09-11. La sesión de voz se cerró y el texto lo atendió el agente que
    # pulsa por coordenadas: estado final bueno y CERO acciones de voz → NO aprobada (no midió la voz)
    "T3-r2.log": [
        "[01:01:31] voz-viva: el servidor dice: sesión cerrada",
        "[01:01:33] agent: tap (455,584)",
        "[01:01:35] agent: tap (212,301)",
    ],
    # T4: mira, pide, recibe la lista (no es intento), elige el 2 → aprobada, miró antes, ejercita
    "T4-r1.log": [
        "[01:02:01] voz-viva: ejecutando «map_look»",
        "[01:02:02] mapa-mcp: -> map_take exit=Descargas",
        "[01:02:03] mapa-mcp: <- (400 ms) hice 0 de 1 y paré en el paso 1: hay 2 puertas vivas para «Descargas»: 1) … 2) …",
        "[01:02:04] mapa-mcp: -> map_take exit=Descargas which=2",
        "[01:02:05] mapa-mcp: <- (900 ms) hice los 1 paso(s): pulsé «Descargas» y ahora estás en «uia://explorer.exe/descargas».",
    ],
    # T5: dos fallos y un frenado al mismo sitio; nunca pasa por Sistema; estado final no alcanzado
    "T5-r1.log": [
        "[01:03:01] mapa-mcp: -> map_take exit=Bluetooth",
        "[01:03:03] mapa-mcp: <- (1900 ms) hice los 1 paso(s): pulsé «Bluetooth» y la pantalla no cambió.",
        "[01:03:04] mapa-mcp: -> map_take exit=uia:name=Bluetooth;ct=ListItem",
        "[01:03:06] mapa-mcp: <- (1900 ms) hice los 1 paso(s): pulsé «Bluetooth» y la pantalla no cambió.",
        "[01:03:07] voz-viva: tope: «map_take» no se ejecuta — no lo intento una tercera vez: «Bluetooth» ya falló dos veces en este turno",
    ],
}
RESUMEN = [
    {"tarea": "T1", "rep": 1, "estado_final": True, "tope": False, "t0": "01:00:00.100"},
    {"tarea": "T1", "rep": 2, "estado_final": True, "tope": False, "t0": "01:00:30.100"},
    {"tarea": "T3", "rep": 1, "estado_final": True, "tope": False, "t0": "01:01:00.100"},
    {"tarea": "T3", "rep": 2, "estado_final": True, "tope": False, "t0": "01:01:30.100"},
    {"tarea": "T4", "rep": 1, "estado_final": True, "tope": False, "t0": "01:02:00.100"},
    {"tarea": "T5", "rep": 1, "estado_final": False, "tope": False, "t0": "01:03:00.100"},
]


def main():
    d = pathlib.Path(tempfile.mkdtemp(prefix="nivel4-autoprueba-"))
    for nombre, lineas in LOGS.items():
        (d / nombre).write_text("\n".join(lineas) + "\n", encoding="utf-8")
    (d / "resumen.jsonl").write_text("\n".join(json.dumps(r, ensure_ascii=False) for r in RESUMEN) + "\n",
                                     encoding="utf-8")
    out = subprocess.run([sys.executable, str(ANALIZAR), str(d), "autoprueba", "--plan", "8"], capture_output=True)
    texto = (out.stdout + out.stderr).decode("utf-8", "replace")
    if out.returncode != 0:
        print(texto)
        print("FALLO: analizar.py no terminó")
        return 1
    f = {(x["tarea"], x["rep"]): x for x in json.loads((d / "analisis.json").read_text(encoding="utf-8"))}

    esperado = [
        ("T1 r1 aprobada", f[("T1", 1)]["aprobado"] is True),
        ("T1 r2 NO aprobada: «no hay campo con el foco» y «NO escribo» son dos fallos",
         f[("T1", 2)]["aprobado"] is False and f[("T1", 2)]["fallidas"] == 2),
        ("T3 NO aprobada: dos fallidas en la petición, aunque por destino serían 1+1",
         f[("T3", 1)]["aprobado"] is False and f[("T3", 1)]["fallidas"] == 2 and f[("T3", 1)]["intentos_max"] == 1),
        ("T3 r2 NO aprobada: estado final bueno con cero acciones de voz (lo resolvió otro camino)",
         f[("T3", 2)]["aprobado"] is False and f[("T3", 2)]["acciones"] == 0 and f[("T3", 2)]["estado_final"] is True),
        ("la tabla dice cuántas llegaron al estado final sin la voz", "sin una sola acción de voz: 1" in texto),
        ("T4 aprobada: la lista no es intento, eligió el 2",
         f[("T4", 1)]["aprobado"] is True and f[("T4", 1)]["listas"] == 1 and f[("T4", 1)]["fallidas"] == 0),
        ("T4 miró antes de actuar; T1 no", f[("T4", 1)]["miro_antes"] is True and f[("T1", 1)]["miro_antes"] is False),
        ("T4 ejercita: pasó por «descargas»", f[("T4", 1)]["ejercita"] is True),
        ("T5: el frenado cuenta como tercer intento al mismo sitio",
         f[("T5", 1)]["intentos_max"] == 3 and f[("T5", 1)]["rechazos"] == 1),
        ("T5 no ejercita: no pasó por Sistema", f[("T5", 1)]["ejercita"] is False and f[("T5", 1)]["aprobado"] is False),
        ("el denominador es el plan", "Aprobadas: 2 de 8" in texto),
        ("la columna «Miró antes» sale en la tabla", "Miró antes" in texto),
    ]
    for nombre, ok in esperado:
        print(("OK    " if ok else "FALLO ") + nombre)
    malos = [n for n, ok in esperado if not ok]
    if malos:
        print(texto)
    return 1 if malos else 0


if __name__ == "__main__":
    sys.exit(main())
