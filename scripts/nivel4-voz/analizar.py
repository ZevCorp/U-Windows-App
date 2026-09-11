"""Analiza una corrida de nivel4.ps1: una fila por tarea y repeticion, y el agregado.

Uso: python analizar_nivel4.py <carpeta_de_salida> [<etiqueta>]

Lee <carpeta>/T#-r#.log (el trozo de log de U desde que se pulso Enter) y resumen.jsonl.
Umbrales de la spec 017, fijados antes de medir:
  - intentos: acciones al mismo destino dentro de la peticion, maximo 2 (lectura estricta del audio)
  - tiempo por accion: del "->" a su "<-", maximo 2000 ms
  - aprobado: estado final alcanzado, <=2 intentos y todas las acciones <=2000 ms
El denominador es el plan (tareas x repeticiones), no lo que se llego a ejecutar (patron n.10).
"""
import json
import re
import sys
import unicodedata
from pathlib import Path
from statistics import median

sys.stdout.reconfigure(encoding="utf-8")

ACCIONES = {"map_take", "map_type", "map_go_to", "map_open_app", "map_unblock", "file_open"}
ARG_DESTINO = {"map_take": "exit", "map_type": "target", "map_go_to": "surface",
               "map_open_app": "app", "map_unblock": "choose", "file_open": "path"}
RE_LINEA = re.compile(r"^\[(\d\d):(\d\d):(\d\d)\] ([\w-]+): (.*)$")
RE_IDA = re.compile(r"^(?:→|->) (\S+)\s*(.*)$")
RE_VUELTA = re.compile(r"^(?:←|<-)\s*\(?\s*(\d+)\s*ms\)?\s*(.*)$")
RE_ARG = re.compile(r"(\w+)=(.*?)(?=\s+\w+=|$)")


def aplanar(s: str) -> str:
    """Lo mismo que Navigation.Nombres.Aplanar, y el nombre de un selector uia si lo es."""
    s = (s or "").strip()
    m = re.match(r"^uia:.*?\bname=([^;]*)", s)
    if m:
        s = m.group(1)
    s = unicodedata.normalize("NFD", s.lower())
    s = "".join(c for c in s if unicodedata.category(c) != "Mn")
    return " ".join(s.split())


def analizar_trozo(texto: str):
    llamadas, acciones, dijo, rechazos, turnos = [], [], [], [], []
    pendiente = None
    for linea in texto.splitlines():
        m = RE_LINEA.match(linea.strip())
        if not m:
            continue
        seg = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + int(m.group(3))
        tag, msg = m.group(4), m.group(5)
        if tag == "mapa-mcp":
            ida, vuelta = RE_IDA.match(msg), RE_VUELTA.match(msg)
            if ida:
                tool = ida.group(1)
                args = dict(RE_ARG.findall(ida.group(2)))
                llamadas.append(tool)
                pendiente = {"tool": tool, "destino": aplanar(args.get(ARG_DESTINO.get(tool, ""), "")),
                             "seg": seg, "ms": None, "resultado": ""}
                if tool in ACCIONES:
                    acciones.append(pendiente)
            elif vuelta and pendiente is not None:
                pendiente["ms"] = int(vuelta.group(1))
                pendiente["resultado"] = vuelta.group(2)[:160]
                pendiente = None
        elif tag == "voz-viva":
            if msg.startswith("ejecutando «map_look»"):
                llamadas.append("map_look")
            elif re.match(r"^\S{1,2} dijo:", msg):
                dijo.append(msg.split("dijo:", 1)[1].strip())
            elif msg.startswith("tope:"):
                rechazos.append(msg)
        elif tag == "voz-turno":
            turnos.append(msg)
    por_destino = {}
    for a in acciones:
        por_destino[(a["tool"], a["destino"])] = por_destino.get((a["tool"], a["destino"]), 0) + 1
    intentos_max = max(por_destino.values(), default=0)
    lentas = [a for a in acciones if a["ms"] is not None and a["ms"] > 2000]
    sin_vuelta = [a for a in acciones if a["ms"] is None]
    pos_look = [i + 1 for i, t in enumerate(llamadas) if t == "map_look"]
    return {
        "llamadas": len(llamadas), "distintas": len(set(llamadas)), "acciones": len(acciones),
        "intentos_max": intentos_max, "lentas": len(lentas), "sin_vuelta": len(sin_vuelta),
        "ms_acciones": [a["ms"] for a in acciones if a["ms"] is not None],
        "pos_look": pos_look, "dijo": dijo, "rechazos": rechazos, "turnos": turnos,
        "secuencia": [f'{a["tool"]}({a["destino"] or "·"}) {a["ms"] if a["ms"] is not None else "?"}ms' for a in acciones],
    }


def main():
    carpeta = Path(sys.argv[1])
    etiqueta = sys.argv[2] if len(sys.argv) > 2 else carpeta.name
    resumen = {}
    rj = carpeta / "resumen.jsonl"
    if rj.exists():
        for l in rj.read_text(encoding="utf-8").splitlines():
            if l.strip():
                o = json.loads(l)
                resumen[(o["tarea"], int(o["rep"]))] = o
    filas = []
    for f in sorted(carpeta.glob("T*-r*.log")):
        m = re.match(r"(T\d)-r(\d+)\.log", f.name)
        if not m:
            continue
        tarea, rep = m.group(1), int(m.group(2))
        a = analizar_trozo(f.read_text(encoding="utf-8", errors="replace"))
        r = resumen.get((tarea, rep), {})
        a.update(tarea=tarea, rep=rep, estado_final=bool(r.get("estado_final")), tope=bool(r.get("tope")))
        a["aprobado"] = a["estado_final"] and a["intentos_max"] <= 2 and a["lentas"] == 0 and a["sin_vuelta"] == 0
        filas.append(a)

    planeadas = len({(t, r) for (t, r) in resumen}) or len(filas)
    print(f"## Nivel 4 · {etiqueta}\n")
    print("| Tarea | Rep | Estado final | Llamadas | Distintas | Acciones | Intentos máx. | Acciones > 2 s | map_look en | Aprobada |")
    print("|---|---|---|---|---|---|---|---|---|---|")
    for a in filas:
        print(f'| {a["tarea"]} | {a["rep"]} | {"sí" if a["estado_final"] else "no"} | {a["llamadas"]} | {a["distintas"]} | '
              f'{a["acciones"]} | {a["intentos_max"]} | {a["lentas"]} | {",".join(map(str, a["pos_look"])) or "—"} | '
              f'{"**sí**" if a["aprobado"] else "no"} |')
    ms = [x for a in filas for x in a["ms_acciones"]]
    aprob = sum(a["aprobado"] for a in filas)
    print(f"\n**Aprobadas: {aprob} de {planeadas}** (el plan como denominador).")
    if ms:
        print(f"Acciones medidas: {len(ms)} · mediana {median(ms):.0f} ms · ≤ 2 s: {sum(x <= 2000 for x in ms)}/{len(ms)} · máx. {max(ms)} ms")
    if filas:
        print(f"Llamadas por petición: mediana {median(a['llamadas'] for a in filas)} · máx. {max(a['llamadas'] for a in filas)}")
        print(f"Estado final alcanzado: {sum(a['estado_final'] for a in filas)}/{planeadas}")
        print(f"Tareas que agotaron el tope de observación sin que U hablara: {sum(a['tope'] for a in filas)}")
    print("\n### Secuencias\n")
    for a in filas:
        print(f'- **{a["tarea"]} r{a["rep"]}**: {" → ".join(a["secuencia"]) or "(ninguna acción)"}')
        for d in a["dijo"][-2:]:
            print(f"  - Ü dijo: «{d[:180]}»")
        for x in a["rechazos"]:
            print(f"  - {x[:180]}")
        for x in a["turnos"]:
            print(f"  - voz-turno: {x[:180]}")
    (carpeta / "analisis.json").write_text(json.dumps(filas, ensure_ascii=False, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
