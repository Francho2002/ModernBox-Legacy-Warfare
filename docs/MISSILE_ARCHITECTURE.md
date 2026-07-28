# Arquitectura de misiles

Esta variante usa un único núcleo para todos los proyectiles militares que se
comportan como misiles. El objetivo es que añadir una cabeza o plataforma no
requiera copiar listas de identificadores entre defensa aérea, marcadores,
sonidos, radiación y limpieza de proyectiles.

## Invariantes

1. Todo misil registrado tiene exactamente un `MissileProfile`.
2. `Projectile.start` inicia un estado nuevo y captura un destino válido.
3. Un misil termina una sola vez: impacto nativo, intercepción aérea o airburst
   de seguridad.
4. Ningún watchdog elimina silenciosamente un misil ofensivo. Si el vuelo se
   atasca, se completa el impacto en el destino capturado.
5. Antes del impacto se alinean la posición lógica, el sprite y el tile de la
   explosión.
6. Una intercepción destruye el misil en su altura actual y nunca ejecuta su
   carga, terraformación o radiación.
7. Espadas, balas y colisiones ordinarias no pueden derribar misiles protegidos.
8. Los proyectiles son reutilizados por WorldBox. El estado terminal se conserva
   como tombstone hasta el siguiente `start`, evitando una segunda detonación.

## Responsabilidades

- `Code/Warfare/Missiles/MissileCatalog.cs`
  - IDs canónicos.
  - Clasificación convencional, nuclear o defensiva.
  - Interceptabilidad, protección, marcador aéreo, radio seguro, radiación,
    sonido y estela.
  - Normalización final del `ProjectileAsset`.
- `Code/Warfare/Missiles/MissileLifecycle.cs`
  - Estado por proyectil.
  - Supervisión de vuelo y recuperación.
  - Alineación exacta del impacto.
  - Exclusión mutua entre impacto y airburst.
- `Code/IntegratedAirDefense.cs`
  - Selección del interceptor y probabilidad defensiva.
  - Solicita el airburst al lifecycle; no elimina proyectiles directamente.
- `Code/NuclearFallout.cs`
  - Aplica únicamente el nivel de residuo declarado por el perfil.
- `Code/MissileMapMarker.cs` y `Code/NuclearAlertController.cs`
  - Consumen el catálogo; no mantienen listas propias.
- `Code/NavalRoles.cs` y `Code/Vehicles.cs`
  - Registran assets, plataformas, costes y decisiones de lanzamiento.
  - Los efectos especiales de EMP, Neutrón, Ruina y Martillo siguen aislados en
    `NavalRoles.HandleSpecialWarheadImpact`.

## Ciclo

```text
start
  -> vuelo supervisado
     -> llegada: alinear -> efectos previos -> targetReached nativo -> efectos posteriores
     -> intercepción: airburst en altura -> ToRemove
     -> destino inválido: airburst seguro -> ToRemove
     -> atasco/timeout ofensivo: alinear -> ToRemove -> targetReached nativo
```

`targetReached` sigue siendo la única ruta que ejecuta la carga real. El
lifecycle coordina el orden, pero no duplica el sistema de daño de WorldBox.

## Añadir un misil

1. Añadir su ID a `MissileIds`.
2. Añadir un perfil a `MissileCatalog.Profiles`.
3. Registrar su `ProjectileAsset` en `Vehicles` o `NavalRoles` usando ese ID.
4. Registrar la plataforma y su política de lanzamiento.
5. Si necesita un efecto no nativo, añadirlo a
   `NavalRoles.HandleSpecialWarheadImpact`; no crear otro parche de
   `Projectile.targetReached`.
6. Compilar contra WorldBox build 719 y NeoModLoader.
7. Probar impacto normal, intercepción, timeout, vista aérea y reutilización de
   proyectiles en una salva larga.

Nunca se debe:

- añadir otra lista paralela de IDs;
- llamar `ToRemove` sobre un misil ofensivo sin impacto o airburst visible;
- ejecutar la carga de un misil interceptado;
- borrar el estado terminal antes de que la instancia reciba un nuevo `start`;
- crear otro parche independiente de `targetReached`.

## Compatibilidad

Los IDs históricos de proyectiles, submarinos y partidas guardadas se conservan.
El sistema espacial, planetas, galaxias y TUUDS fue retirado del runtime y del
código compilado; sus recursos no se borraron para evitar cambios destructivos
innecesarios en instalaciones existentes.
