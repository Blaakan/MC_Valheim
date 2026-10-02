using System;
using System.Collections.Generic;
using System.Globalization;
using MC.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MC.Exploration.ViewSpyglassMod;

// Me = Spyglass content: item prefab and recipe. Item = clone of vanilla Flint Knife made a Tool like the hammer and
// hoe (user decision: right hand, both hands taken, hangs on the back like the hammer when put away; knife grip),
// built once per game session under inactive DontDestroyOnLoad holder (its Awake never run there), from first
// database that has the knife (main menu ObjectDB once filled, or game ZNetScene on dedicated server). Knife look
// taken off, our model (SpyglassModel) put in, laid along the knife blade and as long as the knife, so the hand hold
// it like the knife. No build pieces (no build mode), no attack (a tool's click would punch with fists:
// PlayerAttackGuard skip it), no damage, no durability. Registration always on ([AlwaysOnPatch] ObjectDB / ZNetScene postfixes):
// spyglasses never vanish from inventories while mod installed, also with feature off. Recipe m_enabled follow
// feature and rules (pending = hidden).
// Prefab and recipe names PERMANENT after release (saved in inventories, chests, ZDOs). Me never touch vanilla data:
// clones only.
internal static class SpyglassContent
{
    internal const string ItemName = "MC_Spyglass";
    internal const string RecipeName = "Recipe_MC_Spyglass";
    internal const string VanillaKnifeName = "KnifeFlint";
    internal const string DisplayName = "Spyglass";

    internal const string Description =
        "Bronze tubes and two crystal lenses. Hold it in your right hand and attack to raise it to your eye; attack "
        + "again or block to lower it. The mouse wheel zooms. You cannot walk while you look.";

    private const float RebuildDelay = 0.5f;

    private static GameObject _holder;
    private static GameObject _item;
    private static ItemDrop.ItemData.SharedData _shared;
    private static Recipe _recipe;
    private static bool _missingLogged;
    private static bool _buildFailed;
    private static bool _rebuildPending;
    private static float _rebuildAt;
    private static readonly HashSet<string> LoggedOnce = new HashSet<string>();

    internal static GameObject ItemPrefab => _item;
    internal static Recipe CraftRecipe => _recipe;
    internal static bool Built => _item != null;
    internal static int ItemHash { get; } = ItemName.GetStableHashCode();
    internal static bool RebuildPending => _rebuildPending;

    // Populated database without the knife seen (game update): self test say "no such error".
    internal static bool MissingReported => _missingLogged;

    // The vanilla knife had a ground look of its own (then the dropped copy is a second model).
    internal static bool GroundModelSeparate { get; private set; }

#if DEBUG
    // Self test: what the build found (knife blade axis, sizes, base material).
    internal static string BuildReport { get; private set; } = "";
#endif

    // Is this item a spyglass? Same shared data = our prefab's (inventory items clone ItemData, shared data stay same
    // object); else same drop prefab (every spyglass's comes from the item database, = _item). Called every physics
    // tick for the right hand: references only, no string (Object.name allocate). Name only before the build.
    internal static bool IsSpyglass(ItemDrop.ItemData item)
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
        if (ReferenceEquals(prefab, null))
        {
            return false;
        }
        if (_item != null)
        {
            return ReferenceEquals(prefab, _item);
        }
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
            if (!EnsureBuilt(db.GetItemPrefab(VanillaKnifeName), "item database"))
            {
                return;
            }
        }
        if (!db.m_itemByHash.ContainsKey(ItemHash))
        {
            if (!db.m_items.Contains(_item))
            {
                db.m_items.Add(_item);
            }
            db.m_itemByHash[ItemHash] = _item;
            db.m_itemByData[_shared] = _item;
        }
        if (_recipe == null)
        {
            _recipe = ScriptableObject.CreateInstance<Recipe>();
            _recipe.name = RecipeName;
            _recipe.hideFlags = HideFlags.HideAndDontSave;
            _recipe.m_item = _item.GetComponent<ItemDrop>();
            _recipe.m_amount = 1;
            _recipe.m_enabled = false;
        }
        if (!HasRecipe(db))
        {
            db.m_recipes.Add(_recipe);
        }
        Rebuild(db);
    }

    // ZNetScene.Awake postfix (always on): item known to network, so dropped spyglasses have a prefab (not "Missing
    // prefab hash"), host never delete them as unknown prefabs, and world load no warn about saved ones.
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
            if (!EnsureBuilt(scene.GetPrefab(VanillaKnifeName), "network prefab list"))
            {
                return;
            }
        }
        if (!scene.HasPrefab(ItemHash))
        {
            scene.m_prefabs.Add(_item);
            scene.m_namedPrefabs.Add(ItemHash, _item);
        }
        // Station prefabs known now (and in-game warnings allowed): recipe again.
        Rebuild();
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

    // Recipe from rules in force: materials, station. Called at registration, on rules change (after the delay),
    // OnActivated, OnDeactivated. m_enabled = feature on + rules here + recipe valid. Feature off = recipe hidden,
    // nothing read, nothing said (game quit run OnDeactivated while Unity already destroy item prefabs).
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
        recipe.m_amount = 1;
        recipe.m_minStationLevel = rules.RecipeStationLevel;
        recipe.m_resources = ParseResources(rules.RecipeResources, db, inGame);
        recipe.m_craftingStation = FindStation(rules.RecipeStation, db, inGame, out var stationOk);
        recipe.m_repairStation = recipe.m_craftingStation;
        var valid = recipe.m_resources.Length > 0 && stationOk;
        if (!valid && inGame && recipe.m_resources.Length == 0)
        {
            WarnOnce("norecipe:" + rules.RecipeResources,
                $"The spyglass recipe has no valid material (RecipeResources = \"{rules.RecipeResources}\"), so it "
                + "stays hidden. Spyglasses you already have still work.");
        }
        recipe.m_enabled = valid;
    }

    private static bool EnsureBuilt(GameObject knife, string where)
    {
        if (Built)
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
                          + "changed), so the spyglass cannot be made. Spyglasses already in inventories are lost "
                          + "when a character loads without it.");
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
            Log.Error($"Could not make the spyglass from the vanilla Flint Knife (the game may have changed). {e}");
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
        try
        {
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);

            // Instantiate deep copy serialized ItemData / SharedData / Attack, so the vanilla knife keep its own.
            var item = Object.Instantiate(knife, holder.transform, false);
            item.name = ItemName;
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

            // Where the knife sits: blade axis and length in the hand copy, centre and length in the dropped copy.
            var baseMaterial = FirstMaterial(attach) ?? FirstMaterial(item.transform);
            var hand = Measure(attach, attach);
            var ground = Measure(item.transform, item.transform, attach);

            StripLook(item.transform);

            // Hand copy: grip at the attach point, tube toward the pinky side (blade comes out at the thumb), as long
            // as the knife (+5 %).
            var handScale = hand.Valid ? hand.Length * 1.05f / SpyglassModel.Length : 1f;
            var handModel = SpyglassModel.Create(attach, baseMaterial, handScale);
            var bladeDir = hand.Valid ? hand.Axis : Vector3.forward;
            handModel.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, -bladeDir);
            handModel.transform.localPosition = hand.Valid ? hand.GripPoint : Vector3.zero;

            // Dropped copy. Vanilla knife (1.0.16): its only look is the attach child, shown on the ground too, so the
            // hand model is the dropped look as well. A knife with its own ground look (later game version): our
            // model there instead, centred where the knife lay.
            var itemLayer = ground.ColliderLayer >= 0 ? ground.ColliderLayer : LayerMask.NameToLayer("item");
            var groundModel = handModel;
            if (ground.Valid)
            {
                var groundScale = ground.Length * 1.05f / SpyglassModel.Length;
                groundModel = SpyglassModel.Create(ground.Owner != null ? ground.Owner : item.transform, baseMaterial, groundScale);
                groundModel.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, ground.Axis);
                var centreOffset = groundModel.transform.localRotation * SpyglassModel.Centre * groundScale;
                groundModel.transform.localPosition = ground.Centre - centreOffset;
            }
            // Collider around the tube on the item layer (pickup, physics). The game switch colliders off in the hand
            // copy (VisEquipment.CleanupInstance).
            var colliderGo = new GameObject("MC_SpyglassCollider");
            colliderGo.layer = itemLayer;
            colliderGo.transform.SetParent(groundModel.transform, false);
            var collider = colliderGo.AddComponent<CapsuleCollider>();
            collider.direction = 2; // along Z
            collider.center = SpyglassModel.Centre;
            collider.height = SpyglassModel.Length;
            collider.radius = SpyglassModel.LensRadius * 1.2f;
            GroundModelSeparate = ground.Valid;

            shared.m_name = DisplayName;
            shared.m_description = Description;
            // Tool like the hammer: right hand, both hands emptied on equip, back slot of tools when put away.
            shared.m_itemType = ItemDrop.ItemData.ItemType.Tool;
            shared.m_attachOverride = ItemDrop.ItemData.ItemType.None;
            shared.m_buildPieces = null;
            shared.m_animationState = ItemDrop.ItemData.AnimationState.Unarmed; // knife grip
            shared.m_skillType = Skills.SkillType.None;
            shared.m_maxStackSize = 1;
            shared.m_maxQuality = 1;
            shared.m_weight = 1f;
            shared.m_value = 0;
            shared.m_teleportable = true;
            shared.m_useDurability = false;
            shared.m_canBeReparied = false;
            shared.m_damages = default;
            shared.m_damagesPerLevel = default;
            shared.m_attackForce = 0f;
            shared.m_backstabBonus = 1f;
            shared.m_attackStatusEffect = null;
            shared.m_alwaysRotate = false;
            shared.m_centerCamera = false;
            // No attack at all: the game start no swing (Humanoid.StartAttack need an attack animation), Scope use
            // the click.
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
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                shared.m_icons = new[] { SpyglassIcon.Get() };
            }
            itemDrop.m_itemData.m_dropPrefab = item;

#if DEBUG
            BuildReport = $"hand: {hand} scale {handScale:0.###}; ground: {ground}; item layer {LayerMask.LayerToName(itemLayer)}; "
                          + $"material: {SpyglassModel.Describe(baseMaterial)}";
#endif
            _holder = holder;
            _item = item;
            _shared = shared;
            Log.Debug($"Made the spyglass ({ItemName}) from {VanillaKnifeName}.");
        }
        catch
        {
            Object.Destroy(holder);
            throw;
        }
    }

    // Size of the vanilla look under root (bounds of its meshes in space's local space, children of skip left out).
    private struct Shape
    {
        internal bool Valid;
        internal Vector3 Axis;      // longest side, pointing to the far end (from the space origin)
        internal float Length;
        internal Vector3 Centre;
        internal Vector3 GripPoint; // space origin, kept (the hand hold the knife there)
        internal Transform Owner;   // space
        internal int ColliderLayer;

        public override string ToString() => Valid
            ? $"axis {Axis} length {Length:0.###} centre {Centre}"
            : "no mesh found";
    }

    private static Shape Measure(Transform root, Transform space, Transform skip = null)
    {
        var shape = new Shape { Owner = space, ColliderLayer = -1 };
        var found = false;
        var min = Vector3.zero;
        var max = Vector3.zero;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null || (skip != null && filter.transform.IsChildOf(skip)))
            {
                continue;
            }
            var b = filter.sharedMesh.bounds;
            var toSpace = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
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
            if (col != null && (skip == null || !col.transform.IsChildOf(skip)))
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
        shape.Centre = centre;
        shape.GripPoint = Vector3.zero;
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

    // Knife look and knife-only parts off: meshes, LOD group, weapon trail, colliders (the dropped copy get its own).
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
                    WarnOnce("amount:" + part, $"The spyglass recipe material \"{part}\" has no valid amount; it is "
                                               + "skipped (write it as Name:amount, for example Bronze:2).");
                }
                continue;
            }
            var prefab = name.Length > 0 ? db.GetItemPrefab(name) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                if (inGame)
                {
                    WarnOnce("item:" + name, $"The spyglass recipe material \"{name}\" is not an item in this game; "
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
            WarnOnce("station:" + name, $"The spyglass recipe station \"{name}\" is not a crafting station in this "
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
