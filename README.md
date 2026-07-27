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
- Diplomacia moderna persistente, escalonada y conectada a guerras y recursos reales.
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

Cada 10–16 segundos se revisa por tandas la producción defensiva y se construye
como máximo un recurso global por ciclo: un lanzamisiles terrestre, un caza o
un bombardero. Cada ciudad madura puede mantener un lanzamisiles y una aeronave
de ala fija; la doctrina del reino decide cuál priorizar cuando faltan ambos.
Se requieren 75 habitantes, líder y reino válidos, producción de vehículos
activa y nivel militar 3, sin depender de la era visual. El lanzamisiles y el
caza cuestan `7` madera, `6` piedra, `4` metal y `2` oro; el bombardero cuesta
`9` madera, `8` piedra, `6` metal y `3` oro.

La artillería y los demás vehículos se encargan desde un controlador civil
escalonado, independiente de las ideologías fantásticas. Sólo examina cuatro
ciudades por ciclo, aplica 90 segundos de espera por ciudad y usa los mismos
costes, niveles y cupos del sistema militar. El chasis temporal se elimina si
no existe una transformación válida, por lo que no deja infantería gratuita.

Los misiles conservan su tamaño físico original. Su marcador seguidor muestra
el sprite completo a escala compacta y permanece visible desde la vista máxima,
sin dejar copias del misil como estela.
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

## Diplomacia moderna

Cada reino conserva en la propia partida sus pactos defensivos, garantías de
independencia, bloques comerciales, embargos, sanciones, ultimátums, estados
títeres, ventas de armas, ayuda económica y apoyo a guerras por
intermediarios. Las organizaciones internacionales votan sanciones, fondos de
ayuda o la mediación de guerras prolongadas con una mayoría mínima del 60%.

Los acuerdos tienen efectos concretos y limitados: los defensores se incorporan
a una guerra mediante una cola segura; el comercio, la ayuda, los tributos y
las armas transfieren recursos reales; las sanciones no pueden retirar más de
tres unidades de oro combinadas por ciclo; y un patrocinador proxy no entra
automáticamente en la guerra. La evaluación rota entre pocos reinos cada ciclo
y nunca recorre todas las relaciones en cada frame.

El botón **Centro diplomático**, representado por una caravana con bandera en
la pestaña principal de ModernBox, abre el panel que muestra una civilización
por página, la dirección de sus acuerdos, sus estados sujetos, su organización
y el registro reciente. La IA negocia automáticamente: el panel informa, no
obliga a activar cada pacto a mano. El sistema no cambia culturas, edificios,
gobernantes ni fronteras para representar un estado títere.

## Flotas

Cada puerto conserva un límite determinista de tres a cinco cascos totales,
pero ModernBox fabrica como máximo dos plataformas militares: una escolta
convencional y, si los cupos lo permiten, un submarino especial. Los
portaviones que arrojaban bombas pequeñas, cargueros, pesqueros y transportes
de ModernBox ya no se producen ni aparecen en el menú. Si existen en una
partida antigua, ocupan una plaza hasta desaparecer y no rompen el guardado.
Los límites se calculan por puerto: una ciudad con dos puertos dispone de dos
presupuestos navales independientes.

Los barcos creados manualmente desde el menú de aparición no están limitados
por este sistema.

### Clases de submarino

Todos los cascos usan el sprite de submarino de su facción y están disponibles
desde el menú de invocación sin depender de la era visual. La producción
automática mantiene el límite de dos naves militares por puerto: antes de
encargar un submarino estratégico necesita una nave militar normal, y cada
puerto sólo puede tener uno de esos cascos estratégicos. Esto permite que las
clases existan a la vez sin inundar las flotas.

- **SSN Cazador:** un torpedo convencional contra naves enemigas y dos misiles
  de crucero.
- **SSGN Arsenal:** salva de 6 a 10 misiles convencionales distribuidos entre
  blancos enemigos.
- **SSBN Tridente:** 3 a 5 misiles nucleares MIRV, sólo ante derrota extrema.
- **SSBN Neutrón:** carga táctica de radio corto; prioriza unidades y no deja
  cambios permanentes en el terreno.
- **SSBN EMP:** detonación aérea que inmoviliza temporalmente vehículos y
  unidades modernas enemigas, sin terraformar.
- **SSBN Martillo:** una carga termonuclear grande, rara y limitada a derrota
  extrema; destruye objetivos, no el terreno.
- **SSBN Ruina:** carga radiológica de baja potencia que afecta temporalmente
  unidades cercanas sin crear bioma de radiación.
- **SSBN Apocalipsis:** conserva el ID legacy `SalvoSubmarine_*` para partidas
  guardadas y dispara de 4 a 6 cargas nucleares normales, de radio levemente mayor,
  distribuidas en el último recurso.

Las armas nucleares de Tridente, Neutrón, Martillo, Ruina y Apocalipsis
respetan la opción **Guerra nuclear**. Ninguna clase crea wasteland ni un bioma
radiactivo permanente.

### SSBN Apocalipsis (salva nuclear)

Cada facción puede fabricar este submarino estratégico de salva, limitado a uno
por puerto. Sólo entra en producción después de que ese puerto posea al menos
una embarcación militar normal y tiene un coste de `15` madera, `13` piedra,
`11` metal y `6` oro.

Durante una guerra, con la opción de guerra nuclear activada, el SSBN consume
`240` de oro para lanzar de cuatro a seis cargas nucleares de radio levemente
mayor sin convertir el terreno en wasteland. Prioriza una ciudad por misil y sólo usa posiciones de reserva
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
