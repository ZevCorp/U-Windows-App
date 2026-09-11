"""Analiza una corrida de conducir.ps1: una fila por tarea y repeticion, y el agregado.

Uso: python analizar.py <carpeta_de_salida> [<etiqueta>] [--plan N]

Lee <carpeta>/T#-r#.log (el trozo de log de U desde que se pulso Enter) y resumen.jsonl.
Umbrales de la spec 017, fijados antes de medir:
  - intentos: acciones al mismo destino dentro de la peticion, maximo 2 (lectura estricta del audio);
    lo que el tope de la rama FRENA cuenta como intento, y la lista numerada de homonimos NO
  - tiempo por accion: del "->" a su "<-", maximo 2000 ms
  - aprobado: estado final alcanzado, <=2 intentos al mismo destino, <=1 intento fallido en toda la
    peticion (lo frenado cuenta: el segundo tiene que ser el bueno) y todas las acciones <=2000 ms
  - "miro antes": si hubo un map_look antes de la primera accion (R3: mirar para desempatar)
El denominador es el plan (--plan, que correr.ps1 calcula como tareas x repeticiones), no lo que se
llego a ejecutar (patron n.10). Sin --plan se avisa de que el denominador es lo ejecutado.

POR QUE CUENTA LOS RECHAZOS (critico de la noche del 2026-09-11): lo que el tope frena no deja linea
"mapa-mcp: ->". Contando solo esas, un tercer intento suspendia en main y "aprobaba" en la rama con
el mismo comportamiento del modelo: el juez favorecia a quien lo habia escrito.
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
RE_LISTA = re.compile(r"hay \d+ puertas vivas para|pediste la \d+, pero")
RE_RECHAZO = re.compile(r"tercera vez: «(.+?)» ya falló")
FALLOS = ("no cambió", "no pude", "paré en el paso", "no lo conozco", "no lo veo", "no está viva",
          "la herramienta falló", "no sé dónde", "no se ejecuta")


def aplanar(s: str) -> str:
    """Lo mismo que Navigation.Nombres.Aplanar, y el nombre de un selector uia si lo es."""
    s = (s or "").strip()
    m = re.match(r"^uia:.*?\bname=([^;]*)", s)
    if m:
        s = m.group(1)
    s = unicodedata.normalize("NFD", s.lower())
    s = "".join(c for c in s if unicodedata.category(c) != "Mn")
    return " ".join(s.split())


def clave(nombre: str, cual: str = "") -> str:
    """El destino como lo compara el tope: aplanado, y con el candidato si se eligio uno."""
    m = re.match(r"^(.*?)\s*(?:#which=|\(which=)(\d+)\)?$", nombre or "")
    if m:
        nombre, cual = m.group(1), m.group(2)
    return aplanar(nombre) + (f"#{cual}" if cual else "")


def seg(h, m, s):
    return int(h) * 3600 + int(m) * 60 + int(s)


def analizar_trozo(texto: str, t0_seg):
    llamadas, acciones, dijo, rechazos, turnos = [], [], [], [], []
    pendiente = None
    for linea in texto.splitlines():
        m = RE_LINEA.match(linea.strip())
        if not m:
            continue
        s = seg(m.group(1), m.group(2), m.group(3))
        tag, msg = m.group(4), m.group(5)
        if tag == "mapa-mcp":
            ida, vuelta = RE_IDA.match(msg), RE_VUELTA.match(msg)
            if ida:
                tool = ida.group(1)
                args = dict(RE_ARG.findall(ida.group(2)))
                llamadas.append(tool)
                pendiente = {"tool": tool, "destino": clave(args.get(ARG_DESTINO.get(tool, ""), ""), args.get("which", "")),
                             "seg": s, "fin": None, "ms": None, "resultado": "", "lista": False, "fallida": False}
                if tool in ACCIONES:
                    acciones.append(pendiente)
            elif vuelta and pendiente is not None:
                pendiente["ms"] = int(vuelta.group(1))
                pendiente["fin"] = s
                r = vuelta.group(2)
                pendiente["resultado"] = r[:200]
                pendiente["lista"] = bool(RE_LISTA.search(r))
                pendiente["fallida"] = (not pendiente["lista"]) and any(f in r for f in FALLOS)
                pendiente = None
        elif tag == "voz-viva":
            if msg.startswith("ejecutando «map_look»"):
                llamadas.append("map_look")
            elif re.match(r"^\S{1,2} dijo:", msg):
                dijo.append(msg.split("dijo:", 1)[1].strip())
            elif msg.startswith("tope:"):
                r = RE_RECHAZO.search(msg)
                rechazos.append({"destino": clave(r.group(1)) if r else "?", "texto": msg})
        elif tag == "voz-turno":
            turnos.append(msg)

    intentos = [a for a in acciones if not a["lista"]]
    por_destino = {}
    for a in intentos:
        por_destino[(a["tool"], a["destino"])] = por_destino.get((a["tool"], a["destino"]), 0) + 1
    for r in rechazos:
        k = next((k for k in por_destino if k[1] == r["destino"]), ("map_take", r["destino"]))
        por_destino[k] = por_destino.get(k, 0) + 1
    fines = [a["fin"] for a in intentos if a["fin"] is not None]
    peticion = (max(fines) - t0_seg) if (fines and t0_seg is not None) else None
    return {
        "llamadas": len(llamadas) + len(rechazos), "distintas": len(set(llamadas)),
        "primera": llamadas[0] if llamadas else "—",
        "acciones": len(intentos), "listas": sum(a["lista"] for a in acciones),
        "fallidas": sum(a["fallida"] for a in intentos), "rechazos": len(rechazos),
        "intentos_max": max(por_destino.values(), default=0),
        "lentas": sum(1 for a in intentos if a["ms"] is not None and a["ms"] > 2000),
        "sin_vuelta": sum(1 for a in intentos if a["ms"] is None),
        "ms_acciones": [a["ms"] for a in intentos if a["ms"] is not None],
        "peticion_s": peticion,
        "destinos": [a["destino"] for a in intentos if a["tool"] == "map_take"],
        "pos_look": [i + 1 for i, t in enumerate(llamadas) if t == "map_look"],
        "miro_antes": "map_look" in llamadas[:next((i for i, t in enumerate(llamadas) if t in ACCIONES), len(llamadas))],
        "dijo": dijo, "rechazos_txt": [r["texto"] for r in rechazos], "turnos": turnos,
        "secuencia": [f'{a["tool"]}({a["destino"] or "·"}){" [lista]" if a["lista"] else ""} '
                      f'{a["ms"] if a["ms"] is not None else "?"}ms{" ✗" if a["fallida"] else ""}' for a in acciones],
    }


def ejercita(tarea, a):
    """¿La corrida pasó por lo que la tarea existe para probar? Si no, no mide su requisito."""
    d = a["destinos"]
    if tarea == "T4":
        return any(x.startswith("descargas") for x in d)
    if tarea == "T5":
        i = next((k for k, x in enumerate(d) if x.startswith("sistema")), None)
        return i is not None and any(x.startswith("bluetooth") for x in d[i + 1:])
    return True


def main():
    args = [x for x in sys.argv[1:]]
    plan = None
    if "--plan" in args:
        k = args.index("--plan")
        plan = int(args[k + 1])
        del args[k:k + 2]
    carpeta = Path(args[0])
    etiqueta = args[1] if len(args) > 1 else carpeta.name
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
        r = resumen.get((tarea, rep), {})
        t0 = None
        if r.get("t0"):
            h, mi, s = r["t0"].split(".")[0].split(":")
            t0 = seg(h, mi, s)
        a = analizar_trozo(f.read_text(encoding="utf-8", errors="replace"), t0)
        a.update(tarea=tarea, rep=rep, estado_final=bool(r.get("estado_final")), tope=bool(r.get("tope")))
        a["ejercita"] = ejercita(tarea, a)
        # «A la primera, máximo dos intentos» es de LA PETICIÓN (spec 017): a lo sumo UN intento fallido
        # en toda ella, y lo frenado cuenta. Aprobar por destino dejaba pasar un fallo en A, otro en B y
        # otro en C antes de acertar en D (crítico final, 2026-09-11).
        a["aprobado"] = (a["estado_final"] and a["ejercita"] and a["intentos_max"] <= 2
                         and a["fallidas"] + a["rechazos"] <= 1
                         and a["lentas"] == 0 and a["sin_vuelta"] == 0)
        filas.append(a)

    denominador = plan if plan is not None else len(filas)
    print(f"## Nivel 4 · {etiqueta}\n")
    if plan is None:
        print("> **Aviso:** sin `--plan`, el denominador es lo ejecutado y no el plan.\n")
    print("| Tarea | Rep | Estado final | Ejercita | 1.ª herr. | Miró antes | Llamadas | Distintas | Acciones | Fallidas | Frenadas | Intentos máx. | > 2 s | Petición | Aprobada |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for a in filas:
        pet = f'≈{a["peticion_s"]} s' if a["peticion_s"] is not None else "—"
        print(f'| {a["tarea"]} | {a["rep"]} | {"sí" if a["estado_final"] else "no"} | {"sí" if a["ejercita"] else "**no**"} | '
              f'{a["primera"]} | {"sí" if a["miro_antes"] else "no"} | {a["llamadas"]} | {a["distintas"]} | {a["acciones"]} | {a["fallidas"]} | {a["rechazos"]} | '
              f'{a["intentos_max"]} | {a["lentas"]} | {pet} | {"**sí**" if a["aprobado"] else "no"} |')
    ms = [x for a in filas for x in a["ms_acciones"]]
    print(f"\n**Aprobadas: {sum(a['aprobado'] for a in filas)} de {denominador}** (el plan como denominador).")
    print("«Ejercita = no» es que la corrida no pasó por lo que la tarea prueba (T4: pulsar «Descargas»; "
          "T5: pasar por Sistema); aunque el estado final sea el bueno, no cuenta como aprobada.")
    print("«Petición» va desde el Enter hasta el último resultado de una acción, con resolución de ±1 s: "
          "incluye la latencia del modelo.")
    if ms:
        print(f"\nAcciones medidas: {len(ms)} · mediana {median(ms):.0f} ms · ≤ 2 s: {sum(x <= 2000 for x in ms)}/{len(ms)} · máx. {max(ms)} ms")
    if filas:
        print(f"Estado final alcanzado: {sum(a['estado_final'] for a in filas)}/{denominador} · "
              f"frenadas por el tope: {sum(a['rechazos'] for a in filas)} · listas de homónimos: {sum(a['listas'] for a in filas)}")
        print(f"Tareas que agotaron la observación sin que U hablara: {sum(a['tope'] for a in filas)}")
    print("\n### Secuencias\n")
    for a in filas:
        print(f'- **{a["tarea"]} r{a["rep"]}**: {" → ".join(a["secuencia"]) or "(ninguna acción)"}')
        for d in a["dijo"][-2:]:
            print(f"  - Ü dijo: «{d[:180]}»")
        for x in a["rechazos_txt"]:
            print(f"  - {x[:200]}")
        for x in a["turnos"]:
            print(f"  - voz-turno: {x[:200]}")
    (carpeta / "analisis.json").write_text(json.dumps(filas, ensure_ascii=False, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
