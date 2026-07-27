# ModernBox: Legacy Warfare

Fork personalizado de ModernBox para WorldBox `0.51.2` (build 719), basado en
el trabajo original de Tuxxego.

El objetivo de esta variante es conservar la apariencia, las culturas y el
ritmo de construcción de WorldBox, pero mantener disponible el combate
realista de ModernBox: artillería, vehículos terrestres, aviación, buques,
submarinos, misiles convencionales y armas nucleares.

## Principios de esta variante

- Apariencia medieval por defecto, sin bloquear la tecnología militar.
- Sin era hiperfuturista, monstruos mecánicos ni vehículos fantásticos.
- Progresión nativa de las viviendas y culturas de WorldBox.
- Producción militar deliberadamente escasa y costosa.
- Misiles independientes de la era visual.
- Misiles visibles desde la vista máxima y persistentes hasta el impacto.
- Las explosiones nucleares militares destruyen unidades y edificios e incendian,
  pero no craterizan ni eliminan terreno, ni lo transforman en bioma radiactivo.
- Conocimiento prohibido desbloqueado automáticamente.
- Trenes de la versión moderna de ModernBox.

## Sistemas fantasticos desactivados

`EnableFantasySystems` esta fijado en `false`. ModernBox conserva sus clases y
assets fantasticos para que las partidas guardadas puedan cargarse, pero no
inicializa las bombas de God Powers, sus efectos, zombis ni kaijus. El filtro
de unidades tambien mantiene monstruos y vehiculos fantasticos fuera de los
menus y de la produccion. La pestana heredada de bombas se conserva como
controles nucleares estrategicos para no ocultar `nukes_toggle`.

Las ideologias Dynastic, Martial, Peoplewoven, Mercantile y Chaosvolt siguen
registradas solo para compatibilidad: no se heredan, no se pueden conceder, no
tienen efecto especial y su propagacion Harmony queda cortada. Las culturas y
traits nativos, junto con Unitpotential y NavalUnit, siguen operativos.

Las unidades militares realistas conservan sus sprites de faccion y sus
estadisticas, pero sus ataques usan los proyectiles convencionales compartidos:
sin municion elemental ni fantastica en canones, vehiculos, aviacion, destructores,
lanzamisiles y submarinos.

## Espacio, planetas y galaxias desactivados

`EnableSpaceSystems` esta fijado en `false`. Los assets, clases y datos legacy
de espacio se conservan para compatibilidad, pero no se crean gestores de
planetas o espacio, no se muestran ventanas ni controles de galaxias y no se
generan planetas. Tampoco se ejecuta la limpieza de datos ModernBox ni la
persistencia/autoguardado espacial, por lo que la compuerta no borra ni escribe
datos espaciales existentes.

## Defensa aérea y antimisiles

Los lanzamisiles terrestres y los destructores pueden derribar aeronaves enemigas con los cohetes ya existentes. También intentan interceptar una vez los misiles enemigos que pasan cerca: los convencionales son vulnerables, las bombas nucleares son difíciles de detener y la Bomba del Zar casi nunca puede ser interceptada.

Cada 8–12 segundos se revisa la producción defensiva y se construye como máximo
un lanzamisiles terrestre global por ciclo. Cada ciudad puede mantener uno
cuando alcanza 100 habitantes, tiene líder y reino válidos, la producción de
vehículos está activa y puede pagar `9` madera, `7` piedra, `6` metal y `3` oro.
No depende de la era visual.

Los misiles usan su sprite completo y una marca visible desde la vista máxima.
Los impactos convencionales se reproducen como audio no posicional para poder
oírlos desde lejos, con el sonido pequeño original y sin duplicarlo localmente.

## Recursos OreBox integrados

ModernBox incorpora los seis generadores de recursos de OreBox: metal, oro,
piedra, plata, mitril y adamantita. Están en la pestaña **Recursos** del centro
de ModernBox y conservan sus identificadores originales para que las partidas
guardadas de OreBox sigan siendo compatibles. Esta integración no requiere que
el mod OreBox independiente esté instalado.

OreBox fue creado por Erex_147 y se distribuye bajo licencia MIT. Los sprites
son de core.skull. Créditos y texto de licencia completos en
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md); proyecto original:
https://github.com/Erex147/OreBox.

## Flotas

Cada puerto puede mantener cuatro embarcaciones automáticas, con un máximo de
dos embarcaciones militares por puerto. Dos puertos pertenecientes a la misma
ciudad pueden mantener hasta ocho embarcaciones en total.

Los barcos creados manualmente desde el menú de aparición no están limitados
por este sistema.

### SSBN de salva nuclear

Cada facción puede fabricar un submarino estratégico de salva, limitado a uno
por puerto. Sólo entra en producción después de que ese puerto posea al menos
una embarcación militar normal y tiene un coste de `14` madera, `12` piedra,
`10` metal y `6` oro.

Durante una guerra, con la opción de guerra nuclear activada, el SSBN consume
`160` de oro para lanzar hasta cuatro Bombas del Zar sin convertir el terreno
en wasteland. Prioriza una ciudad por misil y sólo usa posiciones de reserva
separadas cuando no existen suficientes blancos reales. La recarga de la salva
es de `600` segundos. También conserva su ataque convencional.

El disparo queda reservado para el último recurso y exige rey vivo:

- Con una ciudad viva: la ciudad debe estar siendo capturada por un enemigo, o
  estar en peligro y cumplir al menos dos señales: enemigos con `>=3` ciudades,
  población enemiga de al menos `max(80, 3x población propia)` o guerreros
  enemigos de al menos `max(8, 3x guerreros propios)`.
- Con dos ciudades vivas: al menos una debe estar siendo capturada y los
  enemigos deben reunir `>=4` ciudades, `max(150, 3x población propia)` y
  `max(12, 2x guerreros propios)`.
- Con más de dos ciudades, la salva nunca se autoriza.

## Instalación

Requiere WorldBox build 719 y NeoModLoader `1.2.0.1`.

1. Copia la carpeta completa del mod dentro de `WorldBox\Mods`.
2. Conserva la estructura `Code`, `GameResources`,
   `GameResourcesReplace` y `Artwork`.
3. Inicia WorldBox con el modo experimental habilitado.

Los DLL de WorldBox no se distribuyen en este repositorio. NeoModLoader compila
el código contra las bibliotecas de la instalación local del juego.

## Trenes

Los raíles pueden generarse automáticamente, aunque la colocación manual es
más fiable. Utiliza el pincel mínimo para construir líneas horizontales o
verticales continuas antes de invocar el tren.

## Menú de unidades

La invocación manual está separada en cuatro categorías: artillería, unidades
terrestres, aviación y armada. Cada categoría conserva los poderes de aparición
originales; Trainbox permanece en su propia pestaña.

El panel heredado de planetas no se muestra mientras los sistemas espaciales
están desactivados.

La interfaz activa de ModernBox, los controles de Trainbox y las descripciones
de las unidades están localizados al español. Los identificadores internos de
poderes y entidades se conservan para mantener la compatibilidad con partidas y
configuraciones existentes.
