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
- Conocimiento prohibido desbloqueado automáticamente.
- Trenes de la versión moderna de ModernBox.

## Flotas

Cada puerto puede mantener cuatro embarcaciones automáticas, con un máximo de
dos embarcaciones militares por puerto. Dos puertos pertenecientes a la misma
ciudad pueden mantener hasta ocho embarcaciones en total.

Los barcos creados manualmente desde el menú de aparición no están limitados
por este sistema.

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

