using System;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = the instruments as game content: three item prefabs, their recipes, and the Music status effect.
// Items = clones of the vanilla Flint Knife made Tools like the hammer (right hand, both hands emptied, hang at the hip
// when put away; research: knife has only an "attach" child, also its ground look). Knife look taken off, our model
// (InstrumentModels) put in, sized from the knife length. Built once per game session under inactive
// DontDestroyOnLoad holder, from the first database that has the knife (main menu ObjectDB once filled, or ZNetScene
// on a dedicated server). No build pieces, no attack (a tool's click would punch with fists: PlayerAttackGuard skip
// it), no damage, no durability, no value (silver never turn into coins at the trader).
// Registration always on ([AlwaysOnPatch] ObjectDB / ZNetScene postfixes): instruments never vanish from inventories
// while mod installed, also with feature off; the status effect is known by hash on every game (owner apply it).
// Recipe m_enabled follow feature and rules (pending = hidden).
// Prefab, recipe and status effect names PERMANENT after release (saved in inventories, chests, ZDOs). Me never touch
// vanilla data: clones only.
internal static class InstrumentContent
{
    internal const string VanillaKnifeName = "KnifeFlint";
    internal const string VanillaWoodName = "Wood";
    internal const string EffectName = "SE_MC_Music";
    private const float RebuildDelay = 0.5f;
    internal const float RebuildDelaySeconds = RebuildDelay;

    internal const string Controls =
        "[<color=yellow><b>$KEY_Attack</b></color>] Songs   [<color=yellow><b>$KEY_Block</b></color>] Stop";

    private sealed class Def
    {
        internal InstrumentKind Kind;
        internal string ItemName;
        internal string RecipeName;
        internal string DisplayName;
        internal string Description;
        internal float Weight;
        internal int Hash;
        internal GameObject Item;
        internal ItemDrop.ItemData.SharedData Shared;
        internal Recipe Recipe;
    }

    private const string CommonText =
        " Hold it and attack to pick a song: let it play by itself, or perform it yourself in the rhythm game. Perform "
        + "it well and you and the players near you get Music: more comfort, so Rested lasts longer.";

    private static readonly Def[] Defs =
    {
        new Def
        {
            Kind = InstrumentKind.Flute,
            ItemName = "MC_Flute",
            RecipeName = "Recipe_MC_Flute",
            DisplayName = "Wooden Flute",
            Description = "An end-blown flute carved from fine wood, with six finger holes." + CommonText,
            Weight = 0.5f,
        },
        new Def
        {
            Kind = InstrumentKind.Lyre,
            ItemName = "MC_Lyre",
            RecipeName = "Recipe_MC_Lyre",
            DisplayName = "Silver Lyre",
            Description = "A small lyre: a silver frame strung with six strings of waxed linen." + CommonText,
            Weight = 2f,
        },
        new Def
        {
            Kind = InstrumentKind.Tambourine,
            ItemName = "MC_Tambourine",
            RecipeName = "Recipe_MC_Tambourine",
            DisplayName = "Tambourine",
            Description = "A fine wood hoop with a leather skin and pairs of small metal jingles." + CommonText,
            Weight = 1f,
        },
    };

    private static GameObject _holder;
    private static bool _built;
    private static bool _missingLogged;
    private static bool _buildFailed;
    private static bool _rebuildPending;
    private static float _rebuildAt;
    private static MusicEffect _effect;
    private static readonly HashSet<string> LoggedOnce = new HashSet<string>();

    static InstrumentContent()
    {
        foreach (var def in Defs)
        {
            def.Hash = def.ItemName.GetStableHashCode();
        }
    }

    internal static bool Built => _built;
    internal static bool RebuildPending => _rebuildPending;
    internal static bool MissingReported => _missingLogged;
    internal static int EffectHash { get; } = EffectName.GetStableHashCode();
    internal static MusicEffect Effect => _effect;

#if DEBUG
    internal static string BuildReport { get; private set; } = "";
#endif

    internal static IEnumerable<InstrumentKind> Kinds
    {
        get
        {
            foreach (var def in Defs)
            {
                yield return def.Kind;
            }
        }
    }

    internal static string ItemName(InstrumentKind kind) => Find(kind)?.ItemName ?? "";
    internal static string DisplayName(InstrumentKind kind) => Find(kind)?.DisplayName ?? "";
    internal static int ItemHash(InstrumentKind kind) => Find(kind)?.Hash ?? 0;
    internal static GameObject Prefab(InstrumentKind kind) => Find(kind)?.Item;
    internal static Recipe RecipeOf(InstrumentKind kind) => Find(kind)?.Recipe;

    private static Def Find(InstrumentKind kind)
    {
        foreach (var def in Defs)
        {
            if (def.Kind == kind)
            {
                return def;
            }
        }
        return null;
    }

    // Which instrument is this item (None = not ours). Same shared data = our prefab's (inventory items clone
    // ItemData, shared data stay same object); else same drop prefab. Called per frame for the right hand: reference
    // compares only, name only before the build.
    internal static InstrumentKind KindOf(ItemDrop.ItemData item)
    {
        if (item == null || item.m_shared == null)
        {
            return InstrumentKind.None;
        }
        var prefab = item.m_dropPrefab;
        for (var i = 0; i < Defs.Length; i++)
        {
            var def = Defs[i];
            if (def.Shared != null)
            {
                if (ReferenceEquals(item.m_shared, def.Shared) || (!ReferenceEquals(prefab, null) && ReferenceEquals(prefab, def.Item)))
                {
                    return def.Kind;
                }
            }
            else if (prefab != null && prefab.name == def.ItemName)
            {
                return def.Kind;
            }
        }
        return InstrumentKind.None;
    }

    // Which instrument is in this right-hand item hash (VisEquipment hash, also for other players). None = not ours.
    internal static InstrumentKind KindOfHash(int hash)
    {
        if (hash == 0)
        {
            return InstrumentKind.None;
        }
        for (var i = 0; i < Defs.Length; i++)
        {
            if (Defs[i].Hash == hash)
            {
                return Defs[i].Kind;
            }
        }
        return InstrumentKind.None;
    }

    // ObjectDB.Awake / CopyOtherDB postfix (always on). Main menu first Awake run on EMPTY lists: skip silently,
    // CopyOtherDB postfix do the job. Idempotent: add only what is missing (CopyOtherDB share the lists).
    internal static void RegisterInObjectDB(ObjectDB db)
    {
        if (db == null || db.m_items == null)
        {
            return;
        }
        // Not the game's item database: empty (main menu first Awake) or another mod's own copy holding only its
        // items (seen 2026-10-05 with Jotunn-based mods installed: no Wood, no Flint Knife). Me wait for the real one,
        // and never put our items into theirs.
        if (db.m_items.Count == 0 || db.GetItemPrefab(VanillaWoodName) == null)
        {
            return;
        }
        if (!_built)
        {
            if (!EnsureBuilt(db.GetItemPrefab(VanillaKnifeName), "item database"))
            {
                return;
            }
        }
        foreach (var def in Defs)
        {
            if (!db.m_itemByHash.ContainsKey(def.Hash))
            {
                if (!db.m_items.Contains(def.Item))
                {
                    db.m_items.Add(def.Item);
                }
                db.m_itemByHash[def.Hash] = def.Item;
                db.m_itemByData[def.Shared] = def.Item;
            }
            if (def.Recipe == null)
            {
                var recipe = ScriptableObject.CreateInstance<Recipe>();
                recipe.name = def.RecipeName;
                recipe.hideFlags = HideFlags.HideAndDontSave;
                recipe.m_item = def.Item.GetComponent<ItemDrop>();
                recipe.m_amount = 1;
                recipe.m_enabled = false;
                def.Recipe = recipe;
            }
            if (!HasRecipe(db, def.RecipeName))
            {
                db.m_recipes.Add(def.Recipe);
            }
        }
        RegisterEffect(db);
        Rebuild(db);
    }

    // ZNetScene.Awake postfix (always on): items known to network, so dropped instruments have a prefab (not "Missing
    // prefab hash"), host never delete them as unknown prefabs, and world load no warn about saved ones.
    internal static void RegisterInZNetScene(ZNetScene scene)
    {
        if (scene == null || scene.m_prefabs == null)
        {
            return;
        }
        if (!_built)
        {
            if (scene.m_prefabs.Count == 0)
            {
                return;
            }
            if (!EnsureBuilt(scene.GetPrefab(VanillaKnifeName), "network prefab list"))
            {
                return;
            }
        }
        foreach (var def in Defs)
        {
            if (!scene.HasPrefab(def.Hash))
            {
                scene.m_prefabs.Add(def.Item);
                scene.m_namedPrefabs.Add(def.Hash, def.Item);
            }
        }
        // Station prefabs known now (and in-game warnings allowed): recipes again.
        Rebuild();
    }

    // The status effect in the database: owners look it up by hash when another player's game (or ours) adds it.
    private static void RegisterEffect(ObjectDB db)
    {
        if (db.m_StatusEffects == null)
        {
            return;
        }
        if (_effect == null)
        {
            _effect = MusicEffect.CreateTemplate();
        }
        if (db.GetStatusEffect(EffectHash) == null)
        {
            db.m_StatusEffects.Add(_effect);
        }
    }

    // Rules changed: rebuild once they stay still RebuildDelay s (ZNet.Update postfix call UpdatePendingRebuild).
    internal static void RequestRebuild()
    {
        _rebuildPending = true;
        _rebuildAt = Time.unscaledTime + RebuildDelay;
    }

    internal static void UpdatePendingRebuild()
    {
        if (!_rebuildPending || Time.unscaledTime < _rebuildAt)
        {
            return;
        }
        _rebuildPending = false;
        Rebuild();
    }

    // Recipes from rules in force: materials, station, level. Called at registration, on rules change (after the
    // delay), OnActivated, OnDeactivated. m_enabled = feature on + rules here + recipe valid. Feature off = recipes
    // hidden, nothing read, nothing said (game quit run OnDeactivated while Unity already destroy item prefabs).
    internal static void Rebuild(ObjectDB db = null)
    {
        var anyRecipe = false;
        foreach (var def in Defs)
        {
            anyRecipe |= def.Recipe != null;
        }
        if (!anyRecipe)
        {
            return;
        }
        if (!Plugin.FeatureActive)
        {
            foreach (var def in Defs)
            {
                if (def.Recipe != null)
                {
                    def.Recipe.m_enabled = false;
                }
            }
            return;
        }
        if (db == null)
        {
            db = ObjectDB.instance;
        }
        var rules = ServerRules.Current;
        if (db == null || rules.IsPending)
        {
            foreach (var def in Defs)
            {
                if (def.Recipe != null)
                {
                    def.Recipe.m_enabled = false;
                }
            }
            return;
        }
        // Main menu: no ZNetScene, no station prefab list. Me warn only in game (menu has nothing to say).
        var inGame = ZNetScene.instance != null;
        foreach (var def in Defs)
        {
            var recipe = def.Recipe;
            if (recipe == null)
            {
                continue;
            }
            var label = def.DisplayName.ToLowerInvariant();
            var resources = rules.Resources(def.Kind);
            recipe.m_amount = 1;
            recipe.m_minStationLevel = rules.StationLevel(def.Kind);
            recipe.m_resources = ParseResources(resources, db, inGame, label);
            recipe.m_craftingStation = FindStation(rules.Station(def.Kind), db, inGame, label, out var stationOk);
            recipe.m_repairStation = recipe.m_craftingStation;
            var valid = recipe.m_resources.Length > 0 && stationOk;
            if (!valid && inGame && recipe.m_resources.Length == 0)
            {
                WarnOnce("norecipe:" + def.Kind + ":" + resources,
                    $"The {label} recipe has no valid material (\"{resources}\"), so it stays hidden. Instruments you "
                    + "already have still work.");
            }
            recipe.m_enabled = valid;
        }
    }

    private static bool EnsureBuilt(GameObject knife, string where)
    {
        if (_built)
        {
            return true;
        }
        if (_buildFailed)
        {
            return false;
        }
        if (knife == null)
        {
            if (!_missingLogged)
            {
                _missingLogged = true;
                Log.Error($"The vanilla Flint Knife ({VanillaKnifeName}) is missing from the {where} (the game may have "
                          + "changed), so the instruments cannot be made. Instruments already in inventories are lost "
                          + "when a character loads without them.");
            }
            return false;
        }
        try
        {
            Build(knife);
            return true;
        }
        catch (Exception e)
        {
            _buildFailed = true;
            Log.Error($"Could not make the instruments from the vanilla Flint Knife (the game may have changed). {e}");
            return false;
        }
    }

    // All or nothing: statics set only at end; half-built holder destroyed.
    private static void Build(GameObject knife)
    {
        var knifeDrop = knife.GetComponent<ItemDrop>();
        if (knifeDrop == null || knifeDrop.m_itemData == null || knifeDrop.m_itemData.m_shared == null)
        {
            throw new InvalidOperationException($"{VanillaKnifeName} has no item data");
        }
        var vanillaShared = knifeDrop.m_itemData.m_shared;
        var holder = new GameObject(ModInfo.Guid + ".Prefabs");
        var items = new GameObject[Defs.Length];
        var shareds = new ItemDrop.ItemData.SharedData[Defs.Length];
        var graphics = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
        var report = "";
        try
        {
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            for (var i = 0; i < Defs.Length; i++)
            {
                var def = Defs[i];
                // Instantiate deep copy serialized ItemData / SharedData / Attack, so the vanilla knife keep its own.
                var item = Object.Instantiate(knife, holder.transform, false);
                item.name = def.ItemName;
                var itemDrop = item.GetComponent<ItemDrop>();
                var shared = itemDrop.m_itemData.m_shared;
                if (ReferenceEquals(shared, vanillaShared))
                {
                    throw new InvalidOperationException("the clone shares the vanilla knife data");
                }
                var attach = FindChild(item.transform, "attach");
                if (attach == null)
                {
                    throw new InvalidOperationException($"{VanillaKnifeName} has no attach child");
                }

                // Where the knife sits: blade axis and length in the hand copy (the knife's only look is "attach",
                // also shown on the ground in 1.0.16).
                var baseMaterial = FirstMaterial(attach) ?? FirstMaterial(item.transform);
                var hand = Measure(attach);
                var itemLayer = hand.ColliderLayer >= 0 ? hand.ColliderLayer : LayerMask.NameToLayer("item");
                StripLook(item.transform);

                // Our model at the grip, long axis along the blade, sized from the knife length.
                var scale = hand.Valid ? hand.Length * InstrumentModels.SizeVsKnife(def.Kind) / InstrumentModels.Length(def.Kind) : 1f;
                var model = InstrumentModels.Create(def.Kind, attach, baseMaterial, scale);
                var bladeDir = hand.Valid ? hand.Axis : Vector3.forward;
                model.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, bladeDir);
                model.transform.localPosition = Vector3.zero;
                // Collider on the item layer (pickup, physics). The game switch colliders off in the hand copy
                // (VisEquipment.CleanupInstance).
                InstrumentModels.AddCollider(def.Kind, model, itemLayer);

                shared.m_name = def.DisplayName;
                shared.m_description = def.Description;
                shared.m_subtitle = Controls;
                // Tool like the hammer: right hand, both hands emptied on equip, back slot of tools when put away.
                shared.m_itemType = ItemDrop.ItemData.ItemType.Tool;
                shared.m_attachOverride = ItemDrop.ItemData.ItemType.None;
                shared.m_buildPieces = null;
                shared.m_animationState = ItemDrop.ItemData.AnimationState.Unarmed;
                shared.m_skillType = Skills.SkillType.None;
                shared.m_maxStackSize = 1;
                shared.m_maxQuality = 1;
                shared.m_weight = def.Weight;
                shared.m_value = 0;
                shared.m_teleportable = true;
                shared.m_useDurability = false;
                shared.m_canBeReparied = false;
                shared.m_damages = default;
                shared.m_damagesPerLevel = default;
                shared.m_attackForce = 0f;
                shared.m_backstabBonus = 1f;
                shared.m_attackStatusEffect = null;
                shared.m_equipStatusEffect = null;
                shared.m_alwaysRotate = false;
                shared.m_centerCamera = false;
                // No attack at all: the game start no swing (Humanoid.StartAttack need an attack animation).
                shared.m_attack = shared.m_attack.Clone();
                shared.m_attack.m_attackAnimation = "";
                shared.m_attack.m_attackStamina = 0f;
                shared.m_secondaryAttack = shared.m_secondaryAttack.Clone();
                shared.m_secondaryAttack.m_attackAnimation = "";
                shared.m_secondaryAttack.m_attackStamina = 0f;
                shared.m_hitEffect = new EffectList();
                shared.m_hitTerrainEffect = new EffectList();
                shared.m_triggerEffect = new EffectList();
                shared.m_trailStartEffect = new EffectList();
                // Our icon; drawing failed = knife icon stays (a null sprite would show a white square everywhere).
                var icon = graphics ? InstrumentIcons.Get(def.Kind) : null;
                if (icon != null)
                {
                    shared.m_icons = new[] { icon };
                }
                itemDrop.m_itemData.m_dropPrefab = item;
                items[i] = item;
                shareds[i] = shared;
                report += $"{def.Kind}: knife {hand} scale {scale:0.###}; ";
            }
#if DEBUG
            BuildReport = report + "material " + InstrumentModels.Describe(FirstMaterial(knife.transform));
#endif
            _holder = holder;
            for (var i = 0; i < Defs.Length; i++)
            {
                Defs[i].Item = items[i];
                Defs[i].Shared = shareds[i];
            }
            _built = true;
            Log.Debug($"Made the instruments from {VanillaKnifeName}: {report}");
        }
        catch
        {
            Object.Destroy(holder);
            throw;
        }
    }

    // Size of the vanilla look under root (bounds of its meshes in root's local space).
    private struct Shape
    {
        internal bool Valid;
        internal Vector3 Axis;      // longest side, pointing to the far end (from the root origin = grip)
        internal float Length;
        internal int ColliderLayer;

        public override string ToString() => Valid ? $"axis {Axis} length {Length:0.###}" : "no mesh found";
    }

    private static Shape Measure(Transform root)
    {
        var shape = new Shape { ColliderLayer = -1 };
        var found = false;
        var min = Vector3.zero;
        var max = Vector3.zero;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }
            var b = filter.sharedMesh.bounds;
            var toSpace = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (var i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = toSpace.MultiplyPoint3x4(corner);
                if (!found)
                {
                    min = max = p;
                    found = true;
                }
                else
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
        }
        foreach (var col in root.GetComponentsInChildren<Collider>(true))
        {
            if (col != null)
            {
                shape.ColliderLayer = col.gameObject.layer;
                break;
            }
        }
        if (!found)
        {
            return shape;
        }
        var size = max - min;
        var centre = (min + max) * 0.5f;
        var axisIndex = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        var axis = Vector3.zero;
        axis[axisIndex] = centre[axisIndex] >= 0f ? 1f : -1f;
        shape.Valid = size[axisIndex] > 0.01f;
        shape.Axis = axis;
        shape.Length = size[axisIndex];
        return shape;
    }

    private static Material FirstMaterial(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer != null && renderer.sharedMaterial != null)
            {
                return renderer.sharedMaterial;
            }
        }
        return null;
    }

    // Knife look and knife-only parts off: meshes, LOD group, weapon trail, colliders (we add our own).
    private static void StripLook(Transform root)
    {
        foreach (var lod in root.GetComponentsInChildren<LODGroup>(true))
        {
            Object.DestroyImmediate(lod);
        }
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer || renderer is TrailRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                Object.DestroyImmediate(renderer);
                if (filter != null)
                {
                    Object.DestroyImmediate(filter);
                }
            }
        }
        foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null && behaviour.GetType().Name == "MeleeWeaponTrail")
            {
                Object.DestroyImmediate(behaviour);
            }
        }
        foreach (var col in root.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(col);
        }
    }

    private static Transform FindChild(Transform parent, string name)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name)
            {
                return child;
            }
        }
        return null;
    }

    private static bool HasRecipe(ObjectDB db, string name)
    {
        foreach (var recipe in db.m_recipes)
        {
            if (recipe != null && recipe.name == name)
            {
                return true;
            }
        }
        return false;
    }

    // "Name:amount,Name:amount". Unknown names and bad amounts skipped (one warning each, in game only).
    private static Piece.Requirement[] ParseResources(string text, ObjectDB db, bool inGame, string label)
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
                    WarnOnce("amount:" + part, $"The {label} recipe material \"{part}\" has no valid amount; it is "
                                               + "skipped (write it as Name:amount, for example FineWood:4).");
                }
                continue;
            }
            var prefab = name.Length > 0 ? db.GetItemPrefab(name) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                if (inGame)
                {
                    WarnOnce("item:" + name, $"The {label} recipe material \"{name}\" is not an item in this game; "
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
    private static CraftingStation FindStation(string name, ObjectDB db, bool inGame, string label, out bool ok)
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
            WarnOnce("station:" + name, $"The {label} recipe station \"{name}\" is not a crafting station in this "
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
