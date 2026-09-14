# Plan de implementación: la prueba limpia, de la enseñanza al ✓ sin tropezar

Estado: **implementada** (226–229 verdes; contrato intacto, 182 promesas; sabotaje comprobado: las cuatro en rojo, 9 aserciones, huella restaurada) · Nace del diagnóstico del 2026-09-11 · Rama: `jose/el-check-corre-una-skill`

> El dueño, tras borrar todos los aprendizajes, enseñar uno desde cero, grabar una consulta y pulsar
> ✓: «Lo que yo esperaría como desarrollador y como usuario es que, al enseñarle algo, y luego
> escribir una nota clínica donde se mencionen esos datos, y al hacer check, elija la enseñanza de
> forma correcta, y vaya y lo haga, y lo rellene.» Y dos dudas: «intenté comprobar la tarea dando
> clic en mostrar, pero creo que no funciona así» y «solo le he hecho una enseñanza y veo dos».

## Diagnóstico: qué se midió (log de `C:\U-dev2`, 2026-09-11, 11:26–11:54)

| Qué | Medida | Fuente |
|---|---|---|
| La enseñanza | lección `leccion_20260911_112651`: 48 eventos, 1161 cuadros, 291 s; skill de la demo guardada con 23 pasos, 19 huecos y su lección enlazada | `leccion:`, `workflow-teach:` |
| «Mostrar» | sí lanzó la comprobación de SU lección (promesa 225); el panel dijo «la estoy repasando» y devolvió en 0 s; el veredicto salió 12 min después en la carita | `aprendizajes:` 11:36:42, `comprobar:` 11:49:09 |
| El veredicto | 17 de 19: SIGUE PENDIENTE. Costó 4,25 USD | `comprobar: piloto terminó` |
| Paso que falló nº1 | evento 1, «Favoritos/IS-H: Pto.tbjo.clínico»: la demo llegaba a `sapgui://QAS/SESSION_MANAGER/SAPLN_WP_FRAMEWORK/0100` y el piloto a `sapgui://QAS/NWP1/SAPLN_WP_FRAMEWORK/0100`. Mismo programa, mismo dynpro; el código de transacción todavía no había cambiado cuando el terreno lo grabó | `NO ATERRIZÓ el evento 1` |
| Paso que falló nº2 | evento 48, el último clic: sin selector ni etiqueta, con llegada = donde acabó la demo. Cuenta como navegante y nadie puede darlo | `EventosQueCuentan`, lección |
| Dos aprendizajes | uno lo guarda `WorkflowTeachSession` al cerrar la demo («Ingreso de datos clínicos…», 23 pasos) y otro lo guarda el piloto al comprobar («Registrar triage…», 17 pasos). Misma lección, dos archivos | `skills/`, ambos con `DeLaLeccion = leccion_20260911_112651` |
| El ✓ | «0 skill(s) comprobada(s) de 2»: el piloto se negó a inventar, que es lo correcto | `envio:` 11:53:35 |
| Fallo latente | la skill verificada guarda como `Llegada` de cada campo tecleado el VALOR leído («75», «normal»); `RecorrerSegunElNucleo.LlegoDondeTocaba` exige esa llegada: el ✓ habría parado en el primer campo aunque la skill estuviera comprobada | `SkillDeLoVerificado` (`v.Real`), `RecorrerSegunElNucleo:213` |

## Por qué esto va dirigido por especificación

Los cuatro fallos son silenciosos y ya han costado una prueba real de 12 minutos y 5 dólares. Dos de
ellos —la pantalla a medio cambiar y el evento sin identidad— son la MISMA clase de error que la
spec 014 creyó cerrada («19 de 20»): se tapó por un lado y volvió por otro. Sin promesa, vuelve
una tercera vez.

**La regla del flujo, y no tiene excepciones:** ninguna línea de producción entra antes que la
promesa que la juzga.

## La especificación

| # | Promesa | Fase |
|---|---|---|
| 226 | una pantalla de SAP cogida a medio cambiar es la misma pantalla: una llegada bajo SESSION_MANAGER cuyo programa no es el de Easy Access casa con esa pantalla bajo su transacción real, y el batch y el juez lo deciden por la MISMA función | 1 |
| 227 | un evento que nadie puede dar —sin selector ni etiqueta— no cuenta para comprobar; donde acabó la demo lo exige el último paso de la skill, que es donde termina | 2 |
| 228 | una lección deja UN aprendizaje: al guardar el verificado se retira el que la demo dejó de la misma lección, y uno sin lección conocida no retira nada | 3 |
| 229 | la llegada viaja solo con los pasos que navegan: un paso verificado que escribe no exige llegada, porque el valor leído del campo no es una pantalla | 4 |

Y sin promesa, porque la pantalla no la juzga el contrato: **«Mostrar» sobre uno sin repasar
ESPERA a la comprobación y devuelve su veredicto al panel**, con el progreso mientras dura.

### Retirado ninguno, extendido 175

La 175 decía «un evento que navega aterriza si…». Sigue diciéndolo; lo que cambia es qué eventos
cuentan: los que nadie puede dar, no. Anotado aquí con el dueño («mi objetivo principal es que
funcione»).

## Las fases

### Fase 1 — la pantalla a medio cambiar (226)
`Navigation.Superficies.MismaPantalla(a, b)`, puro. `ElRescate.Aterrizo` y los dos sitios de
`RecorrerSegunElNucleo` que comparaban con `Equals` pasan por ahí. Tres sitios, tres corregidos.

### Fase 2 — el evento que nadie puede dar (227)
`EventosQueCuentan` excluye los eventos sin selector ni etiqueta. `SkillDeLoVerificado` toma
`DondeTermina` de `leccion.Termino`, no de la llegada del último paso.

### Fase 3 — una lección, un aprendizaje (228)
`SkillEnsenada.GuardarComoElUnicoDeSuLeccion(carpeta)`: quita los archivos de la misma
`DeLaLeccion` y guarda. Lo usa el `GuardarSkill` del piloto.

### Fase 4 — la llegada solo con los que navegan (229)
`SkillDeLoVerificado`: `Llegada = v.Real` solo si el evento está en `EventosQueNavegan`.

### Fase 5 — «Mostrar» espera (sin promesa)
`OnComprobarAprendizaje` se parte en `ComprobarAsync(progreso)` que devuelve el veredicto;
`MostrarAprendizajeAsync` lo espera y el panel lo pinta.

## Primera tanda: lo que se midió al implementar (2026-09-11)

**Rojo primero, verde después.** Las cuatro promesas salieron rojas por su motivo escrito, y una
de ellas enseñó el fallo latente con el dato real: «DondeTermina es «75»». Tras el código, 182 de
182. Con los cuatro sabotajes puestos —Equals a secas, el clic anónimo contando, la de la demo
quedándose, el valor leído viajando como llegada— cayeron exactamente 203, 204, 205 y 206, con 9
aserciones; sanado, la huella de los cuatro archivos volvió a la de antes.

**La comparación de llegadas vivía en tres sitios** (el rescate y dos veces el batch) y los tres
pasan ahora por `Superficies.MismaPantalla`. Contados antes de arreglar (patrón nº5).

**Lo que queda para la máquina con SAP delante:**

1. En el panel, «Mostrar» sobre «Ingreso de datos clínicos…»: ahora ESPERA. El panel va contando
   el progreso y al final pinta el veredicto. Con la lección de las 11:26 debería salir 18 de 18 y
   COMPROBADA, y la lista quedar con UN aprendizaje, el del piloto.
2. Grabar una consulta con signos vitales y pulsar ✓: el piloto elige ese aprendizaje y lo corre.
   En el log: `envio: … 1 skill(s) comprobada(s)`, `skill: corriendo «…» · con coreografía`, y la
   cuenta con lo que quedó en blanco.
3. Límite conocido que verás: la skill elige al paciente por el nombre de la fila que se tocó en
   la demo («GIRALDO»). Con otro paciente en la lista parará ahí. Es un hueco que no existe
   todavía, y va en su propia spec.

## Lo que NO entra
- Corregir en el terreno las aristas ya grabadas con `SESSION_MANAGER`: se casan al comparar.
- Que la demo no guarde skill al cerrar (dejar solo la del piloto): la de la demo es la que se
  ofrece a «Mostrar»; sin ella no habría nada que repasar.
