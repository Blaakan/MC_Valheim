using System;
using System.Collections.Generic;
using UnityEngine;

namespace MC.Combat.WeaponsDualWieldMod;

// Me = sheathed pair placed by weapon kind (design 2.7, D27; user rule 2026-09-30: swords, maces and axes on the
// back, daggers and knives on the side hip). Vanilla VisEquipment.AttachBackItem pick the joint per item (attach
// override, else item type): one-handed sword, axe, mace = m_backMelee (back, under Spine1); knife (override Tool) =
// m_backTool (right hip, under Hips). Two weapons on one joint get one pose there, so they overlap. Me run right after
// vanilla made the two back instances (SetBackEquipped postfix, only when it changed something), on every game with
// the mod, for every player it draw (back items come from the synced ZDO). Pure cosmetic: nothing synced, nothing
// saved. Me read vanilla's own choice from the joint each instance sit on (no skill list):
//   both on m_backMelee = Cross: X on the back.
//   both on m_backTool  = Hips: main-hand knife stay where vanilla put it, off-hand knife go to the mirror spot on
//                         the other hip.
//   anything else (knife + back weapon, or other mod moved one) = me touch nothing: each at its own vanilla spot.
// Cross: reference weapon keep its vanilla pose (main-hand one, unless only the off-hand one is flat). Other weapon
// go on the same joint as mirror image of the reference: mirrored across the plane through the reference centre that
// hold the reference's outward normal and the body's up, so both lie in the reference's own plane and cross at its
// centre. Other one lie a little outward (on top). Reference near vertical = me tilt it first, else mirror = the same
// line. Me use the body's up of the rebuild frame, then pose ride with the joint (twist, bend). But joint lean
// sideways too (sheathe on the stance change: first in-world run saw 7 degrees two frames later), so X go lopsided.
// Me keep it level: every frame, same postfix, me turn both about the X normal, round the crossing point, until the
// middle line between them sit on the body's up again (Level, design 2.7). Body flat then (no plane vertical) =
// vanilla for now, tried again every RetryFrames frames.
// Hips: mirror plane = body's centre plane carried by the hips bone, from the body mesh's bind pose (pelvis square,
// no pose of the rebuild frame baked in). Off-hand knife = its own vanilla pose mirrored across it, put under the
// hips bone: both knives ride the pelvis like on a belt, mirror images in every pose, no work per frame.
// Shapes come from the weapon's biggest mesh (bounds in its own space): long axis, thin axis (flat face normal).
internal static class BackCross
{
    // Reference angle from the vertical in its plane: below MinAngle me tilt it to TiltAngle, above MaxAngle back to
    // MaxAngle. Vanilla sword on the back sit near 30-40 degrees (dual.visuals note it), so it stay untouched.
    internal const float MinAngle = 20f;
    internal const float TiltAngle = 30f;
    internal const float MaxAngle = 60f;

    // Other weapon this far outward (metres), so the blades no cut each other at the crossing.
    internal const float Lift = 0.025f;

    // Thin axis clear = smallest extent under this part of the middle one (blade, axe head, knife). Mace head round =
    // not clear: me keep its own roll.
    private const float FlatRatio = 0.6f;

    // Pair left as vanilla because the body lay flat on the rebuild frame (no plane vertical): Apply again every
    // RetryFrames frames on the same untouched instances, until upright (leaving a bed make no rebuild).
    internal const int RetryFrames = 15;

    // Level: X middle line off the body's up by less than this (degrees) = me leave it (no transform write on a still
    // body).
    internal const float LevelStep = 0.25f;

    // Level: off by more than this (degrees) = odd pose (bent over, head down in water): body's up mean nothing for the
    // X then, me let it ride the joint. Back under it = level again, small turn.
    internal const float LevelMax = 45f;

    private static readonly Dictionary<int, bool> PairWeaponByHash = new Dictionary<int, bool>();
    private static readonly List<MeshFilter> Filters = new List<MeshFilter>();
    private static readonly List<VisEquipment> Retries = new List<VisEquipment>();
    private static readonly List<Crossing> Crossings = new List<Crossing>();
    private static ObjectDB _db;

    internal static bool RetryPending => Retries.Count > 0;

    // Some player has a crossed pair to keep level.
    internal static bool LevelPending => Crossings.Count > 0;

    // One crossed pair kept level. Long axes in each instance's rotation space (point up: hilt), normal and crossing
    // point in the joint's space (they ride with it).
    private struct Crossing
    {
        internal VisEquipment Vis;
        internal Transform Reference;
        internal Transform Other;
        internal Transform Joint;
        internal Vector3 ReferenceLong;
        internal Vector3 OtherLong;
        internal Vector3 Normal;
        internal Vector3 Point;
    }

    // Weapon shape in the instance's rotation space (world dir = instance.rotation * dir). Centre in its local space.
    internal struct Shape
    {
        internal Vector3 Center;
        internal Vector3 Long;     // unit, point to the end that is up in the vanilla pose (hilt, handle)
        internal Vector3 Thin;     // unit, normal of the flat face (valid only when Flat)
        internal bool Flat;
        internal float Length;
    }

#if DEBUG
    // Self test: what Apply did with the last pair it saw.
    internal enum Layout
    {
        None,       // nothing seen since ResetRecord (or left as vanilla before choosing: no mesh, body flat)
        Crossed,    // two back weapons crossed on m_backMelee
        Hips,       // two knives, one per hip
        Apart,      // knife + back weapon (or unknown joints): each left at its own vanilla spot
    }

    // Self test: setting forced in memory (null = real setting).
    internal static bool? TestEnabled;

    // Self test: what the last pair got (null = nothing done since ResetRecord).
    internal static string LastRecord;
    internal static Layout LastLayout;
    internal static float LastVanillaAngle;
    internal static float LastAngle;
    internal static bool LastTilted;
    internal static Transform LastReference;
    internal static Transform LastOther;

    // Self test: knife pair's mirror plane came from the bind pose (else the rebuild frame's pose).
    internal static bool LastPlaneFromBind;

    // Self test: turns Level gave the last crossed pair since it crossed (signed sum and biggest single one, degrees).
    internal static float LastLevelTotal;
    internal static float LastLevelMax;

    internal static void ResetRecord()
    {
        LastRecord = null;
        LastLayout = Layout.None;
        LastVanillaAngle = 0f;
        LastAngle = 0f;
        LastTilted = false;
        LastReference = null;
        LastOther = null;
        LastPlaneFromBind = false;
        LastLevelTotal = 0f;
        LastLevelMax = 0f;
    }
#endif

    private static bool Wanted()
    {
#if DEBUG
        if (TestEnabled.HasValue)
        {
            return TestEnabled.Value;
        }
#endif
        return Plugin.CrossSheathedPair == null || Plugin.CrossSheathedPair.Value;
    }

    // SetBackEquipped postfix, when vanilla just rebuilt the back instances (fresh vanilla pose), or Retry (pose still
    // vanilla: skipped before any change).
    internal static void Apply(VisEquipment vis)
    {
        if (Retries.Count > 0)
        {
            Retries.Remove(vis);
        }
        if (Crossings.Count > 0)
        {
            // Old instances gone with this rebuild (and players gone from the game): nothing more to level.
            for (var i = Crossings.Count - 1; i >= 0; i--)
            {
                var old = Crossings[i];
                if (ReferenceEquals(old.Vis, vis) || old.Vis == null)
                {
                    Crossings.RemoveAt(i);
                }
            }
        }
        if (!vis.m_isPlayer || vis.m_isArmorStand || LeftTrails.NoGraphics || !Wanted())
        {
            return;
        }
        var rightInstance = vis.m_rightBackItemInstance;
        var leftInstance = vis.m_leftBackItemInstance;
        if (rightInstance == null || leftInstance == null || !IsPairWeapon(vis.m_currentRightBackItemHash)
            || !IsPairWeapon(vis.m_currentLeftBackItemHash))
        {
            return;
        }
        var right = rightInstance.transform;
        var left = leftInstance.transform;
        // Spot = the joint vanilla AttachBackItem gave each one (its own rule per item).
        var back = vis.m_backMelee;
        var hip = vis.m_backTool;
        var rightJoint = right.parent;
        var leftJoint = left.parent;
        if (back != null && rightJoint == back && leftJoint == back)
        {
            Cross(vis, right, left, back);
            return;
        }
        if (hip != null && rightJoint == hip && leftJoint == hip)
        {
            SplitHips(vis, left, hip);
            return;
        }
        // Knife + back weapon (either hand): each already on its own spot in its own pose. Me touch nothing.
#if DEBUG
        LastLayout = Layout.Apart;
        LastReference = right;
        LastOther = left;
        LastRecord = $"main-hand weapon on {JointName(rightJoint)}, off-hand weapon on {JointName(leftJoint)}: each "
                     + "left at the game's own spot";
#endif
    }

    // Two back weapons on m_backMelee: X on the back (reference kept, other mirrored onto it), registered for Level.
    private static void Cross(VisEquipment vis, Transform right, Transform left, Transform joint)
    {
        var root = vis.transform;
        var up = root.up;
        if (!Measure(right, up, out var rightShape) || !Measure(left, up, out var leftShape))
        {
            return;
        }
        var refIsRight = rightShape.Flat || !leftShape.Flat;
        var refT = refIsRight ? right : left;
        var otherT = refIsRight ? left : right;
        var refShape = refIsRight ? rightShape : leftShape;
        var otherShape = refIsRight ? leftShape : rightShape;

        // Reference frame: centre, long axis, outward normal of its plane.
        var center = refT.TransformPoint(refShape.Center);
        var along = refT.rotation * refShape.Long;
        var normal = refShape.Flat ? refT.rotation * refShape.Thin : -root.forward;
        normal -= Vector3.Dot(normal, along) * along;
        if (normal.sqrMagnitude < 1e-6f)
        {
            // Round reference pointing straight back (body lying): no plane now, try again later.
            Retries.Add(vis);
            return;
        }
        normal.Normalize();
        var axisPoint = root.position + up * Vector3.Dot(center - root.position, up);
        var away = center - axisPoint;
        if (away.sqrMagnitude < 1e-4f)
        {
            away = -root.forward;
        }
        if (Vector3.Dot(normal, away) < 0f)
        {
            normal = -normal;
        }
        // In-plane vertical and horizontal. Plane near horizontal (body lying) = no sense of "up": leave vanilla, try
        // again later (nothing changed yet).
        var vertical = up - Vector3.Dot(up, normal) * normal;
        if (vertical.sqrMagnitude < 0.01f)
        {
            Retries.Add(vis);
            return;
        }
        vertical.Normalize();
        var across = Vector3.Cross(vertical, normal);
        var a = Vector3.Dot(along, across);
        var b = Vector3.Dot(along, vertical);
        var angle = Mathf.Atan2(Mathf.Abs(a), b) * Mathf.Rad2Deg;
        var side = a >= 0f ? 1f : -1f;
        var target = angle < MinAngle ? TiltAngle : angle > MaxAngle ? MaxAngle : angle;
        var tilted = !Mathf.Approximately(target, angle);
        if (tilted)
        {
            // Turn about its outward normal, around its centre: stay flat on the body, same place.
            var rad = target * Mathf.Deg2Rad;
            var turned = vertical * Mathf.Cos(rad) + across * (side * Mathf.Sin(rad));
            refT.rotation = Quaternion.FromToRotation(along, turned) * refT.rotation;
            refT.position += center - refT.TransformPoint(refShape.Center);
            along = turned;
        }

        // Other weapon: mirror image of the reference across the plane (centre, normal 'across').
        var mirrored = along - 2f * Vector3.Dot(along, across) * across;
        Quaternion rotation;
        if (otherShape.Flat)
        {
            // Mirror image = its other face outward (a proper turn cannot mirror): face that was outward in vanilla
            // now look in, so an axe head sit on the mirrored side.
            var thin = otherShape.Thin;
            if (Vector3.Dot(otherT.rotation * thin, normal) < 0f)
            {
                thin = -thin;
            }
            rotation = Quaternion.LookRotation(mirrored, -normal)
                       * Quaternion.Inverse(Quaternion.LookRotation(otherShape.Long, thin));
        }
        else
        {
            rotation = Quaternion.FromToRotation(otherT.rotation * otherShape.Long, mirrored) * otherT.rotation;
        }
        otherT.rotation = rotation;
        otherT.position += center + normal * Lift - otherT.TransformPoint(otherShape.Center);
        var jointInverse = Quaternion.Inverse(joint.rotation);
        Crossings.Add(new Crossing
        {
            Vis = vis,
            Reference = refT,
            Other = otherT,
            Joint = joint,
            ReferenceLong = refShape.Long,
            OtherLong = otherShape.Long,
            Normal = jointInverse * normal,
            Point = joint.InverseTransformPoint(center),
        });
#if DEBUG
        LastLayout = Layout.Crossed;
        LastLevelTotal = 0f;
        LastLevelMax = 0f;
        LastVanillaAngle = angle;
        LastAngle = target;
        LastTilted = tilted;
        LastReference = refT;
        LastOther = otherT;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        LastRecord = $"crossed on {joint.name}: reference {(refIsRight ? "main" : "off-hand")} weapon, vanilla angle "
                     + $"{angle.ToString("0.0", inv)} deg from vertical, crossed at +/-{target.ToString("0.0", inv)} deg"
                     + $"{(tilted ? " (reference tilted)" : "")}, flat: reference {refShape.Flat}, other "
                     + $"{otherShape.Flat}, lengths {refShape.Length.ToString("0.00", inv)} / "
                     + $"{otherShape.Length.ToString("0.00", inv)} m";
#endif
    }

    // Two knives on m_backTool (vanilla: right hip): main-hand one stay as vanilla put it. Off-hand one = mirror image
    // of its own vanilla pose across the body's centre plane (HipsPlane), put under the hips bone, so the two ride
    // the pelvis together and stay mirror images whatever it do (sway, twist, lying). No Level, no Retry.
    private static void SplitHips(VisEquipment vis, Transform off, Transform hip)
    {
        var hips = hip.parent;
        if (hips == null || !Measure(off, vis.transform.up, out var shape)
            || !HipsPlane(vis, hips, out var planeNormal, out var fromBind))
        {
            return;
        }
        var normal = hips.TransformDirection(planeNormal);
        var point = hips.position;
        var thinLocal = ThinAxis(shape);
        var center = off.TransformPoint(shape.Center);
        var mirroredCenter = center - 2f * Vector3.Dot(center - point, normal) * normal;
        var mirroredLong = Vector3.Reflect(off.rotation * shape.Long, normal);
        // Mirror image = its other face outward (a proper turn cannot mirror): long axis and edge mirrored, flat face
        // that looked out on the right hip look in on the left one. Same rule as the other weapon of an X.
        var mirroredThin = -Vector3.Reflect(off.rotation * thinLocal, normal);
        var rotation = Quaternion.LookRotation(mirroredLong, mirroredThin)
                       * Quaternion.Inverse(Quaternion.LookRotation(shape.Long, thinLocal));
        off.SetParent(hips, true);
        off.rotation = rotation;
        off.position += mirroredCenter - off.TransformPoint(shape.Center);
#if DEBUG
        LastLayout = Layout.Hips;
        LastPlaneFromBind = fromBind;
        LastReference = vis.m_rightBackItemInstance != null ? vis.m_rightBackItemInstance.transform : null;
        LastOther = off;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var turn = Vector3.Angle(normal, vis.transform.right);
        turn = Mathf.Min(turn, 180f - turn);
        LastRecord = $"knife pair: main-hand knife left on {hip.name}, off-hand knife mirrored to the other hip on "
                     + $"{hips.name}, mirror plane from {(fromBind ? "the body's bind pose" : "this frame's pose")} "
                     + $"({turn.ToString("0.0", inv)} deg from the player root's centre plane now), off-hand flat "
                     + $"{shape.Flat}, length {shape.Length.ToString("0.00", inv)} m";
#endif
    }

    // Body's centre plane in the hips bone's own space: through the bone (pelvis centre), normal = body's right.
    // Me take it from the body mesh's bind pose: hips frame in the mesh (bindpose inverse), mesh fixed under the player
    // root, root's right = normal of the model's mirror plane. So pelvis square, same in every pose (a plane from the
    // pose now would bake in the hips' sway or twist of that frame). Hips not skinned in the body mesh (odd model) = me
    // use the pose now (fromBind false). Allocate two small arrays (bones, bindposes): only on a rebuild, never per
    // frame.
    internal static bool HipsPlane(VisEquipment vis, Transform hips, out Vector3 normal, out bool fromBind)
    {
        normal = Vector3.zero;
        fromBind = false;
        var root = vis.transform;
        var body = vis.m_bodyModel;
        var mesh = body != null ? body.sharedMesh : null;
        if (mesh != null)
        {
            try
            {
                var bones = body.bones;
                var binds = mesh.bindposes;
                var index = -1;
                for (var i = 0; i < bones.Length && i < binds.Length; i++)
                {
                    if (ReferenceEquals(bones[i], hips))
                    {
                        index = i;
                        break;
                    }
                }
                if (index >= 0)
                {
                    // Hips space (bind) -> mesh space -> root space. Plane normal carried back with the transpose
                    // (right for any scale, no shear).
                    var hipsToRoot = root.worldToLocalMatrix * body.transform.localToWorldMatrix
                                     * binds[index].inverse;
                    normal = hipsToRoot.transpose.MultiplyVector(Vector3.right);
                    fromBind = normal.sqrMagnitude > 1e-8f;
                }
            }
            catch (Exception)
            {
                // Mesh no give its bind data (odd modded model): pose now below.
                fromBind = false;
            }
        }
        if (!fromBind)
        {
            normal = hips.InverseTransformDirection(root.right);
        }
        if (normal.sqrMagnitude < 1e-8f)
        {
            return false;
        }
        normal.Normalize();
        return true;
    }

    // Flat face normal of a shape (instance rotation space). Box with no thickness = any axis across the long one.
    private static Vector3 ThinAxis(Shape shape)
    {
        if (shape.Thin.sqrMagnitude > 0.5f)
        {
            return shape.Thin;
        }
        var thin = Vector3.Cross(shape.Long, Vector3.up);
        if (thin.sqrMagnitude < 1e-4f)
        {
            thin = Vector3.Cross(shape.Long, Vector3.right);
        }
        return thin.normalized;
    }

#if DEBUG
    private static string JointName(Transform joint) => joint != null ? joint.name : "nothing";
#endif

    // Biggest mesh of the instance (LOD copies share its orientation): its bounds give the long and thin axes.
    // False = no mesh (leave vanilla).
    internal static bool Measure(Transform instance, Vector3 up, out Shape shape)
    {
        shape = default;
        Filters.Clear();
        instance.GetComponentsInChildren(false, Filters);
        var best = -1f;
        var inverse = Quaternion.Inverse(instance.rotation);
        for (var i = 0; i < Filters.Count; i++)
        {
            var filter = Filters[i];
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                continue;
            }
            var bounds = mesh.bounds;
            var t = filter.transform;
            // Three box edges in world space (mesh scale and rotation in), then sorted: long, middle, thin.
            Axes[0] = t.TransformVector(bounds.size.x, 0f, 0f);
            Axes[1] = t.TransformVector(0f, bounds.size.y, 0f);
            Axes[2] = t.TransformVector(0f, 0f, bounds.size.z);
            SortByLength(Axes);
            var longest = Axes[0].magnitude;
            if (longest <= best || longest < 1e-4f)
            {
                continue;
            }
            best = longest;
            var middle = Axes[1].magnitude;
            var thinnest = Axes[2].magnitude;
            var longWorld = Axes[0] / longest;
            if (Vector3.Dot(longWorld, up) < 0f)
            {
                longWorld = -longWorld;
            }
            shape.Center = instance.InverseTransformPoint(t.TransformPoint(bounds.center));
            shape.Long = inverse * longWorld;
            shape.Thin = thinnest > 1e-5f ? inverse * (Axes[2] / thinnest) : Vector3.zero;
            shape.Flat = thinnest > 1e-5f && thinnest < FlatRatio * middle;
            shape.Length = longest;
        }
        Filters.Clear();
        return best > 0f;
    }

    private static readonly Vector3[] Axes = new Vector3[3];

    // Three vectors, longest first (tiny insertion sort, no alloc).
    private static void SortByLength(Vector3[] v)
    {
        for (var i = 1; i < v.Length; i++)
        {
            var item = v[i];
            var length = item.sqrMagnitude;
            var j = i - 1;
            while (j >= 0 && v[j].sqrMagnitude < length)
            {
                v[j + 1] = v[j];
                j--;
            }
            v[j + 1] = item;
        }
    }

    // Sheathed like a pair member: one-handed sword, axe, club or knife with a melee swing (the weapons Eligibility
    // let pair), attach override None or Tool. Spears (two-handed back joint), bombs, tankards, torches, shields = no.
    // Where it hang (back or hip) me no decide here: Apply read the joint vanilla gave it. Per prefab hash, cached
    // per ObjectDB.
    private static bool IsPairWeapon(int hash)
    {
        if (hash == 0)
        {
            return false;
        }
        var db = ObjectDB.instance;
        if (db == null)
        {
            return false;
        }
        if (!ReferenceEquals(db, _db))
        {
            _db = db;
            PairWeaponByHash.Clear();
        }
        if (PairWeaponByHash.TryGetValue(hash, out var known))
        {
            return known;
        }
        var prefab = db.GetItemPrefab(hash);
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
        var result = false;
        if (shared != null && shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon
            && (shared.m_attachOverride == ItemDrop.ItemData.ItemType.None
                || shared.m_attachOverride == ItemDrop.ItemData.ItemType.Tool))
        {
            var skill = shared.m_skillType;
            var attack = shared.m_attack;
            result = (skill == Skills.SkillType.Swords || skill == Skills.SkillType.Axes
                      || skill == Skills.SkillType.Clubs || skill == Skills.SkillType.Knives)
                     && attack != null && !attack.m_consumeItem
                     && (attack.m_attackType == Attack.AttackType.Horizontal
                         || attack.m_attackType == Attack.AttackType.Vertical);
        }
        PairWeaponByHash[hash] = result;
        return result;
    }

    // Every player this game draw: back visuals built again at the next frame (vanilla SetBackEquipped see a changed
    // value). Toggle on = holstered pairs placed at once; toggle off (patch gone by then) = vanilla spots and poses
    // back (vanilla destroy the moved knife too, wherever it hang); setting change = either. Quality never negative,
    // so -1 always differ.
    internal static void RebuildAll()
    {
        var players = Player.GetAllPlayers();
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var vis = player != null ? player.m_visEquipment : null;
            if (vis != null && (vis.m_leftBackItemInstance != null || vis.m_rightBackItemInstance != null))
            {
                vis.m_currentRightBackItemQuality = -1;
            }
        }
    }

    // SetBackEquipped postfix on frames with no rebuild, only while a retry wait (RetryPending). Every RetryFrames
    // frames: players gone out of the list, then this one's pair tried again (Apply put it back if still flat).
    internal static void Retry(VisEquipment vis)
    {
        if (Time.frameCount % RetryFrames != 0)
        {
            return;
        }
        for (var i = Retries.Count - 1; i >= 0; i--)
        {
            if (Retries[i] == null)
            {
                Retries.RemoveAt(i);
            }
        }
        if (Retries.Contains(vis))
        {
            Apply(vis);
        }
    }

    // SetBackEquipped postfix on frames with no rebuild, only while some pair is crossed (LevelPending). This
    // player's X: normal and crossing point from its joint now (twist and bend ride along), middle line = sum of the
    // two long axes; off the body's up (projected into the plane) by LevelStep or more = both turned about the normal,
    // round the crossing point, back onto it. Run in Update, so bones = last frame's animation (one frame late, fine for
    // a sway). Plane near horizontal (lying) or off by more than LevelMax (odd pose) = left riding the joint.
    internal static void Level(VisEquipment vis)
    {
        if (Time.frameCount % RetryFrames == 0)
        {
            // Player gone from the game (left, logged out) with a crossed pair: drop it, so LevelPending go false.
            for (var i = Crossings.Count - 1; i >= 0; i--)
            {
                if (Crossings[i].Vis == null)
                {
                    Crossings.RemoveAt(i);
                }
            }
        }
        var index = -1;
        for (var i = 0; i < Crossings.Count; i++)
        {
            if (ReferenceEquals(Crossings[i].Vis, vis))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            return;
        }
        var crossing = Crossings[index];
        var joint = crossing.Joint;
        var reference = crossing.Reference;
        var other = crossing.Other;
        if (joint == null || reference == null || other == null || reference.parent != joint || other.parent != joint)
        {
            // Instances gone or moved by someone else: stop.
            Crossings.RemoveAt(index);
            return;
        }
        var normal = joint.rotation * crossing.Normal;
        var up = vis.transform.up;
        var vertical = up - Vector3.Dot(up, normal) * normal;
        if (vertical.sqrMagnitude < 0.01f)
        {
            return;
        }
        var middle = reference.rotation * crossing.ReferenceLong + other.rotation * crossing.OtherLong;
        middle -= Vector3.Dot(middle, normal) * normal;
        if (middle.sqrMagnitude < 1e-6f)
        {
            return;
        }
        var turn = Vector3.SignedAngle(middle, vertical, normal);
        var size = turn < 0f ? -turn : turn;
        if (size < LevelStep || size > LevelMax)
        {
            return;
        }
        var q = Quaternion.AngleAxis(turn, normal);
        var point = joint.TransformPoint(crossing.Point);
        reference.SetPositionAndRotation(point + q * (reference.position - point), q * reference.rotation);
        other.SetPositionAndRotation(point + q * (other.position - point), q * other.rotation);
#if DEBUG
        if (ReferenceEquals(reference, LastReference))
        {
            LastLevelTotal += turn;
            if (size > LastLevelMax)
            {
                LastLevelMax = size;
            }
        }
#endif
    }

    internal static void Clear()
    {
        PairWeaponByHash.Clear();
        Filters.Clear();
        Retries.Clear();
        Crossings.Clear();
        _db = null;
    }
}
