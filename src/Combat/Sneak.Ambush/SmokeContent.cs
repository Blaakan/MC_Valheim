using System;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Combat.SneakAmbushMod;

// Me = Smoke Screen content: item, projectile, cloud prefab and recipe. Three clones built once per game session
// under inactive DontDestroyOnLoad holder (their Awake never run there), from first database that has vanilla
// BombSmoke (main menu ObjectDB once filled, or game ZNetScene on dedicated server). Registration always on
// ([AlwaysOnPatch] ObjectDB / ZNetScene postfixes): Smoke Screens never vanish from inventories while mod installed,
// also with feature off. Recipe m_enabled follow feature and rules (pending = hidden).
// Prefab names PERMANENT after release (saved in inventories, chests, ZDOs). Me never touch vanilla data: clones only.
internal static class SmokeContent
{
    internal const string ItemName = "MC_SmokeScreen";
    internal const string ProjectileName = "MC_SmokeScreen_projectile";
    internal const string CloudName = "MC_SmokeScreen_cloud";
    internal const string RecipeName = "Recipe_MC_SmokeScreen";
    internal const string VanillaBombName = "BombSmoke";
    internal const string DisplayName = "Smoke Screen";

    internal const string Description =
        "A pouch of soot and resin. Throw it to raise a thick cloud of smoke for a few seconds. Creatures outside the "
        + "cloud cannot see or hear you inside it, and creatures inside cannot see out. Creatures chasing someone close "
        + "to the burst are blinded for a moment and lose track. A creature you hit sees you. Bosses are not fooled. "
        + "Does no damage.";

    private static GameObject _holder;
    private static GameObject _item;
    private static GameObject _projectile;
    private static GameObject _cloud;
    private static ItemDrop.ItemData.SharedData _shared;
    private static Recipe _recipe;
    private static GameObject _vanillaExplosion;
    private static Material _material;
    private static bool _materialSearched;
    private static bool _missingLogged;
    private static bool _buildFailed;
    private static readonly HashSet<string> LoggedOnce = new HashSet<string>();

    internal static GameObject ItemPrefab => _item;
    internal static GameObject ProjectilePrefab => _projectile;
    internal static GameObject CloudPrefab => _cloud;
    internal static GameObject Holder => _holder;
    internal static Recipe CraftRecipe => _recipe;
    internal static bool Built => _item != null;

    // Populated database without BombSmoke seen (game update): self test say "no such error".
    internal static bool MissingReported => _missingLogged;

    // Item icon (vanilla Smoke Bomb icon), for the In smoke cue. Null before build.
    internal static Sprite Icon
    {
        get
        {
            var shared = _shared;
            return shared != null && shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null;
        }
    }

    // Is this item a Smoke Screen? Throw guard (per attack start) and self tests. Same shared data = our prefab's
    // (inventory items clone ItemData, shared data stay same object); drop prefab name = safety net.
    internal static bool IsSmokeScreen(ItemDrop.ItemData item)
    {
        if (item == null || item.m_shared == null)
        {
            return false;
        }
        if (_shared != null && ReferenceEquals(item.m_shared, _shared))
        {
            return true;
        }
        var prefab = item.m_dropPrefab;
        return prefab != null && prefab.name == ItemName;
    }

    // ObjectDB.Awake / CopyOtherDB postfix (always on). Main menu first Awake run on EMPTY lists: skip silently,
    // CopyOtherDB postfix do the job. Idempotent: add only what is missing.
    internal static void RegisterInObjectDB(ObjectDB db)
    {
        if (db == null || db.m_items == null)
        {
            return;
        }
        if (!Built)
        {
            if (db.m_items.Count == 0)
            {
                return;
            }
            if (!EnsureBuilt(db.GetItemPrefab(VanillaBombName), "item database"))
            {
                return;
            }
        }
        var hash = ItemName.GetStableHashCode();
        if (!db.m_itemByHash.ContainsKey(hash))
        {
            if (!db.m_items.Contains(_item))
            {
                db.m_items.Add(_item);
            }
            db.m_itemByHash[hash] = _item;
            db.m_itemByData[_shared] = _item;
        }
        if (_recipe == null)
        {
            _recipe = ScriptableObject.CreateInstance<Recipe>();
            _recipe.name = RecipeName;
            _recipe.hideFlags = HideFlags.HideAndDontSave;
            _recipe.m_item = _item.GetComponent<ItemDrop>();
            _recipe.m_enabled = false;
        }
        if (!HasRecipe(db))
        {
            db.m_recipes.Add(_recipe);
        }
        Rebuild(db);
    }

    // ZNetScene.Awake postfix (always on): item, projectile and cloud known to network, so ZDOs of them are created
    // (not "Missing prefab hash"), host never delete them as unknown prefabs, and world load no warn about saved ones.
    // Dedicated server make no world object anyway (only a server-side simulation mod make it delete unknown ones).
    internal static void RegisterInZNetScene(ZNetScene scene)
    {
        if (scene == null || scene.m_prefabs == null)
        {
            return;
        }
        if (!Built)
        {
            if (scene.m_prefabs.Count == 0)
            {
                return;
            }
            if (!EnsureBuilt(scene.GetPrefab(VanillaBombName), "network prefab list"))
            {
                return;
            }
        }
        AddPrefab(scene, _item);
        AddPrefab(scene, _projectile);
        AddPrefab(scene, _cloud);
        // Station prefabs known now (and in-game warnings allowed): recipe again.
        Rebuild();
    }

    // Recipe from rules in force: materials, amount, station. Called at registration, on rules change
    // (ServerRules.Changed), OnActivated, OnDeactivated. m_enabled = feature on + rules here + recipe valid.
    // Feature off = recipe hidden, nothing read, nothing said: game quit run OnDeactivated (ModPlugin.OnDestroy) while
    // Unity already destroy item prefabs, so lookup there find nothing and warned "not an item". OnActivated rebuild.
    internal static void Rebuild(ObjectDB db = null)
    {
        var recipe = _recipe;
        if (recipe == null)
        {
            return;
        }
        if (!Plugin.FeatureActive)
        {
            recipe.m_enabled = false;
            return;
        }
        if (db == null)
        {
            db = ObjectDB.instance;
        }
        var rules = ServerRules.Current;
        if (db == null || rules.IsPending)
        {
            recipe.m_enabled = false;
            return;
        }
        // Main menu: no ZNetScene, no station prefab list. Me warn only in game (menu has nothing to say).
        var inGame = ZNetScene.instance != null;
        recipe.m_amount = rules.RecipeAmount;
        recipe.m_minStationLevel = rules.RecipeStationLevel;
        recipe.m_resources = ParseResources(rules.RecipeResources, db, inGame);
        recipe.m_craftingStation = FindStation(rules.RecipeStation, db, inGame, out var stationOk);
        var valid = recipe.m_resources.Length > 0 && stationOk;
        if (!valid && inGame && recipe.m_resources.Length == 0)
        {
            WarnOnce("norecipe:" + rules.RecipeResources,
                $"The Smoke Screen recipe has no valid material (RecipeResources = \"{rules.RecipeResources}\"), so it "
                + "stays hidden. Smoke Screens you already have still work.");
        }
        recipe.m_enabled = valid;
    }

    // Vanilla smoke look for the cloud visual: first particle renderer of the vanilla Smoke Bomb explosion (captured
    // before me replace it), else the world smoke renderer's particle prefab. Null = no particles (one warning).
    internal static Material SmokeMaterial
    {
        get
        {
            if (_material != null || _materialSearched)
            {
                return _material;
            }
            _material = RendererMaterial(_vanillaExplosion);
            if (_material == null)
            {
                var smoke = SmokeRenderer.Instance;
                if (smoke != null && smoke._particleSystemPrefab != null)
                {
                    _material = RendererMaterial(smoke._particleSystemPrefab.gameObject);
                }
            }
            // Search done for good only in game (world smoke renderer exist there).
            if (_material != null || ZNetScene.instance != null)
            {
                _materialSearched = true;
                if (_material == null)
                {
                    Log.Warning("Could not find the smoke look of the vanilla Smoke Bomb: Smoke Screen clouds work but "
                                + "show no smoke.");
                }
            }
            return _material;
        }
    }

    private static Material RendererMaterial(GameObject root)
    {
        if (root == null)
        {
            return null;
        }
        foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            if (renderer != null && renderer.sharedMaterial != null)
            {
                return renderer.sharedMaterial;
            }
        }
        return null;
    }

    private static bool EnsureBuilt(GameObject bomb, string where)
    {
        if (Built)
        {
            return true;
        }
        if (_buildFailed)
        {
            return false;
        }
        if (bomb == null)
        {
            if (!_missingLogged)
            {
                _missingLogged = true;
                Log.Error($"The vanilla Smoke Bomb ({VanillaBombName}) is missing from the {where} (the game may have "
                          + "changed), so the Smoke Screen cannot be made. Stealth, sneak-attack XP and the rest of "
                          + $"{ModInfo.Name} still work.");
            }
            return false;
        }
        try
        {
            Build(bomb);
            return true;
        }
        catch (Exception e)
        {
            _buildFailed = true;
            Log.Error($"Could not make the Smoke Screen from the vanilla Smoke Bomb (the game may have changed). "
                      + $"Stealth, sneak-attack XP and the rest of {ModInfo.Name} still work. {e}");
            return false;
        }
    }

    // All or nothing: statics set only at end; half-built holder destroyed.
    private static void Build(GameObject bomb)
    {
        var bombDrop = bomb.GetComponent<ItemDrop>();
        if (bombDrop == null || bombDrop.m_itemData == null || bombDrop.m_itemData.m_shared == null)
        {
            throw new InvalidOperationException($"{VanillaBombName} has no item data");
        }
        var vanillaShared = bombDrop.m_itemData.m_shared;
        var vanillaProjectile = vanillaShared.m_attack != null ? vanillaShared.m_attack.m_attackProjectile : null;
        if (vanillaProjectile == null || vanillaProjectile.GetComponent<Projectile>() == null)
        {
            throw new InvalidOperationException($"{VanillaBombName} has no projectile");
        }

        var holder = new GameObject(ModInfo.Guid + ".Prefabs");
        try
        {
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);

            // Item: Instantiate deep copy serialized ItemData / SharedData / Attack, so vanilla bomb keep its own.
            var item = Object.Instantiate(bomb, holder.transform, false);
            item.name = ItemName;
            var itemDrop = item.GetComponent<ItemDrop>();
            var shared = itemDrop.m_itemData.m_shared;
            if (ReferenceEquals(shared, vanillaShared))
            {
                throw new InvalidOperationException("the clone shares the vanilla Smoke Bomb data");
            }
            shared.m_name = DisplayName;
            shared.m_description = Description;
            shared.m_damages = default;
            shared.m_damagesPerLevel = default;
            shared.m_attackForce = 0f;
            shared.m_backstabBonus = 1f;
            shared.m_attackStatusEffect = null;
            var attack = shared.m_attack.Clone();
            shared.m_attack = attack;
            attack.m_attackHitNoise = 0f;
            attack.m_attackStartNoise = 0f;
            // Attack-level spawn would overwrite our projectile's cloud (Attack.ProjectileAttackTriggered).
            attack.m_spawnOnHit = null;
            attack.m_spawnOnHitChance = 0f;
            itemDrop.m_itemData.m_dropPrefab = item;

            // Projectile: one projectile, one cloud. Destroyed at first hit, so TTL spawn never fire twice.
            var projectile = Object.Instantiate(vanillaProjectile, holder.transform, false);
            projectile.name = ProjectileName;
            var p = projectile.GetComponent<Projectile>();
            var explosion = p.m_spawnOnHit;

            // Cloud: new object, parent first (inactive holder), then components (so no Awake now).
            var cloud = new GameObject(CloudName);
            cloud.transform.SetParent(holder.transform, false);
            var view = cloud.AddComponent<ZNetView>();
            view.m_persistent = true;
            view.m_type = ZDO.ObjectType.Default;
            cloud.AddComponent<SmokeCloud>();

            p.m_spawnOnHit = cloud;
            p.m_randomSpawnOnHit = new List<GameObject>();
            p.m_spawnItem = null;
            p.m_respawnItemOnHit = false;
            p.m_spawnOnHitChance = 1f;
            p.m_spawnCount = 1;
            p.m_spawnOnTerrain = true;
            p.m_spawnOnCharacters = true;
            p.m_spawnOnWearNTear = true;
            p.m_spawnOnTtl = true;
            p.m_groundHitOnly = false;
            p.m_staticHitOnly = false;
            p.m_onlyStopOnTerrain = false;
            p.m_stayAfterHitStatic = false;
            p.m_stayAfterHitDynamic = false;
            p.m_attachToRigidBody = false;
            p.m_attachToClosestBone = false;
            p.m_canHitWater = true;
            p.m_bounceOnWater = false;
            p.m_aoe = 0f;
            p.m_damage = default;
            p.m_statusEffect = "";
            p.m_hitNoise = 0f;
            var removed = new List<string>();
            p.m_hitEffects = Harmless(p.m_hitEffects, removed);
            p.m_hitWaterEffects = Harmless(p.m_hitWaterEffects, removed);
            p.m_spawnOnHitEffects = Harmless(p.m_spawnOnHitEffects, removed);

            attack.m_attackProjectile = projectile;
            // Secondary attack aiming same vanilla projectile (none in 1.0.16): ours too, never vanilla smoke.
            if (shared.m_secondaryAttack != null && shared.m_secondaryAttack.m_attackProjectile == vanillaProjectile)
            {
                shared.m_secondaryAttack = shared.m_secondaryAttack.Clone();
                shared.m_secondaryAttack.m_attackProjectile = projectile;
                shared.m_secondaryAttack.m_attackHitNoise = 0f;
                shared.m_secondaryAttack.m_attackStartNoise = 0f;
            }

            _holder = holder;
            _item = item;
            _projectile = projectile;
            _cloud = cloud;
            _shared = shared;
            _vanillaExplosion = explosion;
            Log.Debug($"Made the Smoke Screen from {VanillaBombName}: item {ItemName}, projectile {ProjectileName}, "
                      + $"cloud {CloudName}"
                      + (removed.Count > 0 ? $"; left out of its hit effects: {string.Join(", ", removed.ToArray())}." : "."));
        }
        catch
        {
            Object.Destroy(holder);
            throw;
        }
    }

    // Keep only effects with nothing networked or harmful (impact sound and puff stay).
    private static EffectList Harmless(EffectList list, List<string> removed)
    {
        var kept = new List<EffectList.EffectData>();
        if (list != null && list.m_effectPrefabs != null)
        {
            foreach (var effect in list.m_effectPrefabs)
            {
                if (effect == null || effect.m_prefab == null)
                {
                    continue;
                }
                var go = effect.m_prefab;
                if (go.GetComponentInChildren<ZNetView>(true) != null || go.GetComponentInChildren<Aoe>(true) != null
                    || go.GetComponentInChildren<SmokeSpawner>(true) != null || go.GetComponentInChildren<Smoke>(true) != null
                    || go.GetComponentInChildren<SpawnAbility>(true) != null)
                {
                    removed.Add(go.name);
                    continue;
                }
                kept.Add(effect);
            }
        }
        return new EffectList { m_effectPrefabs = kept.ToArray() };
    }

    private static void AddPrefab(ZNetScene scene, GameObject prefab)
    {
        var hash = prefab.name.GetStableHashCode();
        if (scene.HasPrefab(hash))
        {
            return;
        }
        scene.m_prefabs.Add(prefab);
        scene.m_namedPrefabs.Add(hash, prefab);
    }

    private static bool HasRecipe(ObjectDB db)
    {
        foreach (var recipe in db.m_recipes)
        {
            if (recipe != null && recipe.name == RecipeName)
            {
                return true;
            }
        }
        return false;
    }

    // "Name:amount,Name:amount". Unknown names and bad amounts skipped (one warning each, in game only).
    private static Piece.Requirement[] ParseResources(string text, ObjectDB db, bool inGame)
    {
        var list = new List<Piece.Requirement>();
        if (string.IsNullOrEmpty(text))
        {
            return list.ToArray();
        }
        foreach (var raw in text.Split(','))
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                continue;
            }
            var colon = part.IndexOf(':');
            var name = (colon < 0 ? part : part.Substring(0, colon)).Trim();
            var amountText = colon < 0 ? "1" : part.Substring(colon + 1).Trim();
            if (!int.TryParse(amountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount < 1)
            {
                if (inGame)
                {
                    WarnOnce("amount:" + part, $"The Smoke Screen recipe material \"{part}\" has no valid amount; it is "
                                               + "skipped (write it as Name:amount, for example Resin:2).");
                }
                continue;
            }
            var prefab = name.Length > 0 ? db.GetItemPrefab(name) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                if (inGame)
                {
                    WarnOnce("item:" + name, $"The Smoke Screen recipe material \"{name}\" is not an item in this game; "
                                             + "it is skipped.");
                }
                continue;
            }
            list.Add(new Piece.Requirement
            {
                m_resItem = drop,
                m_amount = amount,
                m_amountPerLevel = 0,
                m_recover = true,
            });
        }
        return list.ToArray();
    }

    // Station of any recipe in database with that prefab name, else network prefab. Empty name = no station
    // (craft by hand). Unknown name = recipe hidden (one warning, in game).
    private static CraftingStation FindStation(string name, ObjectDB db, bool inGame, out bool ok)
    {
        ok = true;
        name = name == null ? "" : name.Trim();
        if (name.Length == 0)
        {
            return null;
        }
        foreach (var recipe in db.m_recipes)
        {
            if (recipe != null && recipe.m_craftingStation != null && recipe.m_craftingStation.name == name)
            {
                return recipe.m_craftingStation;
            }
        }
        var scene = ZNetScene.instance;
        var prefab = scene != null ? scene.GetPrefab(name) : null;
        var station = prefab != null ? prefab.GetComponent<CraftingStation>() : null;
        if (station != null)
        {
            return station;
        }
        ok = false;
        if (inGame)
        {
            WarnOnce("station:" + name, $"The Smoke Screen recipe station \"{name}\" is not a crafting station in this "
                                        + "game, so the recipe stays hidden.");
        }
        return null;
    }

    private static void WarnOnce(string key, string text)
    {
        if (LoggedOnce.Add(key))
        {
            Log.Warning(text);
        }
    }
}
