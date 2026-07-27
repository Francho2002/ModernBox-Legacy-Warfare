using System.Reflection;
using NeoModLoader.api;
using NeoModLoader.api.features;
using ReflectionUtility;

namespace ModernBox
{
    // Ore generator assets adapted from OreBox by Erex_147 (MIT). See THIRD_PARTY_NOTICES.md.
    internal static class OreBoxIntegration
    {
        private static bool initialized;

        internal static void InitializeAssets()
        {
            if (initialized)
            {
                return;
            }

            AddSpawners();
            AddDrops();
            AddPowers();
            CacheDrops();
            initialized = true;
        }

        private static void AddSpawners()
        {
            BuildingAsset metalSpawner = AssetManager.buildings.get("metal_spawner");
            if (metalSpawner == null)
            {
                metalSpawner = AssetManager.buildings.clone("metal_spawner", "$building$");
                metalSpawner.building_type = BuildingType.Building_Nature;
                metalSpawner.draw_light_area = true;
                metalSpawner.ignored_by_cities = false;
                metalSpawner.draw_light_size = 1f;
                metalSpawner.base_stats["health"] = 50000f;
                metalSpawner.fundament = new BuildingFundament(1, 1, 1, 0);
                metalSpawner.group = "nature";
                metalSpawner.kingdom = "nature";
                metalSpawner.can_be_placed_on_liquid = false;
                metalSpawner.ignore_buildings = true;
                metalSpawner.check_for_close_building = false;
                metalSpawner.can_be_living_house = true;
                metalSpawner.burnable = false;
                metalSpawner.setShadow(0.28f, 0.86f, 0f);
                metalSpawner.sound_built = "event:/SFX/BUILDINGS/SpawnBuildingStone";
                metalSpawner.sound_destroyed = "event:/SFX/BUILDINGS/DestroyBuildingStone";
                metalSpawner.has_sprites_spawn = true;
                metalSpawner.has_sprites_main = true;
                metalSpawner.has_sprites_ruin = true;
                metalSpawner.has_sprites_special = false;
                metalSpawner.has_sprites_main_disabled = false;
                metalSpawner.sprite_path = "buildings/metal_spawner";
                metalSpawner.atlas_asset = AssetManager.dynamic_sprites_library.get("buildings");
                metalSpawner.spawn_drops = true;
                metalSpawner.spawn_drop_id = "metals";
                metalSpawner.spawn_drop_interval = 1f;
                metalSpawner.spawn_drop_min_height = 10f;
                metalSpawner.spawn_drop_min_radius = 1f;
                metalSpawner.spawn_drop_max_radius = 10f;
                metalSpawner.spawn_drop_max_height = 20f;
                metalSpawner.spawn_drop_start_height = 10f;
                metalSpawner.loadBuildingSprites();
            }

            EnsureSpawnerBuilding("gold_spawner", metalSpawner.id, "buildings/gold_spawner", "gold");
            EnsureSpawnerBuilding("stone_spawner", metalSpawner.id, "buildings/stone_spawner", "stone");
            EnsureSpawnerBuilding("silver_spawner", metalSpawner.id, "buildings/silver_spawner", "silver");
            EnsureSpawnerBuilding("mythril_spawner", metalSpawner.id, "buildings/mythril_spawner", "mythril");
            EnsureSpawnerBuilding("adamantine_spawner", metalSpawner.id, "buildings/adamantine_spawner", "adamantine");
        }

        private static void EnsureSpawnerBuilding(string id, string sourceId, string spritePath, string dropId)
        {
            if (AssetManager.buildings.get(id) != null)
            {
                return;
            }

            BuildingAsset spawner = AssetManager.buildings.clone(id, sourceId);
            spawner.sprite_path = spritePath;
            spawner.atlas_asset = AssetManager.dynamic_sprites_library.get("buildings");
            spawner.spawn_drop_id = dropId;
            spawner.loadBuildingSprites();
        }

        private static void AddDrops()
        {
            DropAsset metalDrop = AssetManager.drops.get("spawn_metal_spawner");
            if (metalDrop == null)
            {
                metalDrop = new DropAsset
                {
                    id = "spawn_metal_spawner",
                    path_texture = "drops/drop_metal",
                    default_scale = 0.2f,
                    random_frame = true,
                    random_flip = true,
                    type = DropType.DropBuilding,
                    building_asset = "metal_spawner",
                    action_landed = DropsLibrary.action_spawn_building
                };
                AssetManager.drops.add(metalDrop);
            }

            EnsureSpawnerDrop("spawn_gold_spawner", metalDrop.id, "gold_spawner", "drops/drop_gold");
            EnsureSpawnerDrop("spawn_stone_spawner", metalDrop.id, "stone_spawner", "drops/drop_stone");
            EnsureSpawnerDrop("spawn_silver_spawner", metalDrop.id, "silver_spawner", "drops/drop_stone");
            EnsureSpawnerDrop("spawn_mythril_spawner", metalDrop.id, "mythril_spawner", "drops/drop_stone");
            EnsureSpawnerDrop("spawn_adamantine_spawner", metalDrop.id, "adamantine_spawner", "drops/drop_stone");
        }

        private static void EnsureSpawnerDrop(string id, string sourceId, string buildingId, string texturePath)
        {
            if (AssetManager.drops.get(id) != null)
            {
                return;
            }

            DropAsset drop = AssetManager.drops.clone(id, sourceId);
            drop.building_asset = buildingId;
            drop.path_texture = texturePath;
        }

        private static void AddPowers()
        {
            GodPower metalSpawner = AssetManager.powers.get("metal_spawner");
            if (metalSpawner == null)
            {
                metalSpawner = AssetManager.powers.clone("metal_spawner", "$template_drop_building$");
                metalSpawner.name = "Metal Spawner";
                metalSpawner.rank = PowerRank.Rank0_free;
                metalSpawner.drop_id = "spawn_metal_spawner";
                metalSpawner.falling_chance = 0f;
                metalSpawner.force_brush = "circ_0";
                metalSpawner.click_power_action = StuffDrop;
                metalSpawner.click_power_brush_action = new PowerAction((pTile, pPower) =>
                {
                    return (bool)AssetManager.powers.CallMethod("loopWithCurrentBrushPowerForDropsFull", pTile, pPower);
                });
            }

            EnsureSpawnerPower("gold_spawner", metalSpawner.id, "Gold Spawner", "spawn_gold_spawner");
            EnsureSpawnerPower("stone_spawner", metalSpawner.id, "Stone Spawner", "spawn_stone_spawner");
            EnsureSpawnerPower("silver_spawner", metalSpawner.id, "Silver Spawner", "spawn_silver_spawner");
            EnsureSpawnerPower("mythril_spawner", metalSpawner.id, "Mythril Spawner", "spawn_mythril_spawner");
            EnsureSpawnerPower("adamantine_spawner", metalSpawner.id, "Adamantine Spawner", "spawn_adamantine_spawner");
        }

        private static void EnsureSpawnerPower(string id, string sourceId, string name, string dropId)
        {
            if (AssetManager.powers.get(id) != null)
            {
                return;
            }

            GodPower spawner = AssetManager.powers.clone(id, sourceId);
            spawner.name = name;
            spawner.drop_id = dropId;
        }

        private static void CacheDrops()
        {
            FieldInfo dropField = typeof(GodPower).GetField("cached_drop_asset", BindingFlags.NonPublic | BindingFlags.Instance);
            if (dropField == null)
            {
                return;
            }

            CacheDrop(dropField, "metal_spawner", "spawn_metal_spawner");
            CacheDrop(dropField, "gold_spawner", "spawn_gold_spawner");
            CacheDrop(dropField, "stone_spawner", "spawn_stone_spawner");
            CacheDrop(dropField, "silver_spawner", "spawn_silver_spawner");
            CacheDrop(dropField, "mythril_spawner", "spawn_mythril_spawner");
            CacheDrop(dropField, "adamantine_spawner", "spawn_adamantine_spawner");
        }

        private static void CacheDrop(FieldInfo dropField, string powerId, string dropId)
        {
            GodPower power = AssetManager.powers.get(powerId);
            DropAsset drop = AssetManager.drops.get(dropId);
            if (power != null && drop != null)
            {
                dropField.SetValue(power, drop);
            }
        }

        private static bool StuffDrop(WorldTile pTile, GodPower pPower)
        {
            AssetManager.powers.CallMethod("spawnDrops", pTile, pPower);
            return true;
        }
    }
}
