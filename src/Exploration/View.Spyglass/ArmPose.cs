using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.ViewSpyglassMod;

// Me = the "spyglass to the eye" arm pose, made in code (no new animation clip without an asset bundle): every
// rendered frame, after the animator, the right upper arm and forearm are bent (two-bone IK, elbow out and down) so
// the hand bring the spyglass's eyepiece in front of the right eye, tube along the look direction. Weight 0..1 blends
// from the animated arm: raising = arm coming up. Run on every game with me for every player holding a spyglass:
// local player from Scope.Progress, other players from their ZDO bool Raised (each game run its own blend).
// Player.LateUpdate postfix (every player, every game).
// Player animator runs with the physics (AnimatePhysics): frames without a physics step leave the bones as me wrote
// them. So me remember the animated rotations: bones still holding my last write = animator did not run = me put the
// animated ones back before posing again (never pose on top of my own pose).
internal static class ArmPose
{
    private const float EyeForward = 0.03f;  // eyepiece this far in front of the eyes
    private const float EyeRight = 0.032f;   // right eye: half the eye distance
    private static readonly Vector3 EyesFromHead = new Vector3(0f, 0.15f, 0.07f); // look space, from the head bone
    private const float RemoteRaiseSeconds = Scope.RaiseSeconds;
    private const float RemoteLowerSeconds = Scope.LowerSeconds;

    private sealed class Rig
    {
        internal Player Player;
        internal Transform Upper;
        internal Transform Lower;
        internal Transform Hand;
        internal GameObject ItemInstance;
        internal Transform Eyepiece;
        internal Quaternion AnimUpper;
        internal Quaternion AnimLower;
        internal Quaternion AnimHand;
        internal Quaternion WrittenUpper;
        internal Quaternion WrittenLower;
        internal Quaternion WrittenHand;
        internal bool Written;
        internal float Progress;  // remote players: own blend
        internal bool Broken;     // no arm bones found: never try again
    }

    private static readonly Dictionary<int, Rig> Rigs = new Dictionary<int, Rig>();

#if DEBUG
    // Self test: pose the local player like another player (ZDO flag, head look direction, own blend).
    internal static bool TestAsRemote;
#else
    private const bool TestAsRemote = false;
#endif
    private static float _nextCleanup;

    // Player.LateUpdate postfix.
    internal static void Update(Player player)
    {
        var visEq = player.m_visEquipment;
        var holds = visEq != null && visEq.m_currentRightItemHash == SpyglassContent.ItemHash;
        Rigs.TryGetValue(player.GetInstanceID(), out var rig);
        if (!holds && rig == null)
        {
            return;
        }
        if (rig == null)
        {
            rig = new Rig { Player = player };
            Rigs[player.GetInstanceID()] = rig;
        }
        if (rig.Broken)
        {
            return;
        }

        float weight;
        if (ReferenceEquals(player, Player.m_localPlayer) && !TestAsRemote)
        {
            weight = holds ? ScopeCamera.Smooth(0f, 0.6f, Scope.Progress) : 0f;
        }
        else
        {
            var raised = holds && player.m_nview != null && player.m_nview.IsValid()
                         && player.m_nview.GetZDO().GetBool(Scope.RaisedKey);
            var dt = Time.deltaTime;
            rig.Progress = raised
                ? Mathf.Min(1f, rig.Progress + dt / RemoteRaiseSeconds)
                : Mathf.Max(0f, rig.Progress - dt / RemoteLowerSeconds);
            weight = ScopeCamera.Smooth(0f, 0.6f, rig.Progress);
        }
        Apply(rig, weight, holds);
        if (Time.unscaledTime >= _nextCleanup)
        {
            Cleanup();
        }
    }

    // Local player: new raise starts from the animated arm.
    internal static void ForgetLocal()
    {
        var p = Player.m_localPlayer;
        if (p != null && Rigs.TryGetValue(p.GetInstanceID(), out var rig))
        {
            rig.Progress = 0f;
        }
    }

    // Local spyglass dropped at once: animated arm back now.
    internal static void ReleaseLocal()
    {
        var p = Player.m_localPlayer;
        if (p != null && Rigs.TryGetValue(p.GetInstanceID(), out var rig))
        {
            RestoreAnimated(rig);
        }
    }

    // Feature off: every arm back, nothing remembered.
    internal static void Shutdown()
    {
        foreach (var rig in Rigs.Values)
        {
            if (rig.Player != null)
            {
                RestoreAnimated(rig);
            }
        }
        Rigs.Clear();
    }

    private static void Cleanup()
    {
        _nextCleanup = Time.unscaledTime + 10f;
        List<int> dead = null;
        foreach (var kv in Rigs)
        {
            if (kv.Value.Player == null)
            {
                (dead ??= new List<int>()).Add(kv.Key);
            }
        }
        if (dead != null)
        {
            foreach (var id in dead)
            {
                Rigs.Remove(id);
            }
        }
    }

    private static void Apply(Rig rig, float weight, bool holds)
    {
        if (rig.Upper == null && !FindBones(rig))
        {
            return;
        }
        // Animator ran since my last write? (bones no longer hold what me wrote.) Else put its pose back first.
        if (rig.Written && rig.Upper.localRotation == rig.WrittenUpper && rig.Lower.localRotation == rig.WrittenLower
            && rig.Hand.localRotation == rig.WrittenHand)
        {
            rig.Upper.localRotation = rig.AnimUpper;
            rig.Lower.localRotation = rig.AnimLower;
            rig.Hand.localRotation = rig.AnimHand;
        }
        else
        {
            rig.AnimUpper = rig.Upper.localRotation;
            rig.AnimLower = rig.Lower.localRotation;
            rig.AnimHand = rig.Hand.localRotation;
        }
        rig.Written = false;
        if (weight <= 0.001f || !holds)
        {
            return;
        }
        if (!FindEyepiece(rig))
        {
            return;
        }
        Solve(rig);
        rig.Upper.localRotation = Quaternion.Slerp(rig.AnimUpper, rig.Upper.localRotation, weight);
        rig.Lower.localRotation = Quaternion.Slerp(rig.AnimLower, rig.Lower.localRotation, weight);
        rig.Hand.localRotation = Quaternion.Slerp(rig.AnimHand, rig.Hand.localRotation, weight);
        rig.WrittenUpper = rig.Upper.localRotation;
        rig.WrittenLower = rig.Lower.localRotation;
        rig.WrittenHand = rig.Hand.localRotation;
        rig.Written = true;
    }

    // Full pose (weight 1) from the animated one: hand placed so the eyepiece sits at the right eye, tube along the
    // look; elbow out to the side and down.
    private static void Solve(Rig rig)
    {
        var player = rig.Player;
        var look = LookDirection(player);
        var right = Vector3.Cross(Vector3.up, look);
        right = right.sqrMagnitude < 0.0001f ? player.transform.right : right.normalized;
        var target = Eyes(player, look) + look * EyeForward + right * EyeRight;

        // Turn that brings the tube onto the look, smallest twist from the animated grip.
        var hand = rig.Hand;
        var tubeNow = rig.Eyepiece.forward;
        var turn = Quaternion.FromToRotation(tubeNow, look);
        var handRot = turn * hand.rotation;
        var handPos = target - turn * (rig.Eyepiece.position - hand.position);

        TwoBone(rig, handPos, player.transform);
        hand.rotation = handRot;
    }

    // Where this player looks: own camera aim for the local player; others = the head look direction every game
    // already gets (vanilla look target on the player ZDO, smoothed by CharacterAnimEvent).
    private static Vector3 LookDirection(Player player)
    {
        var anim = player.m_animEvent;
        var look = ReferenceEquals(player, Player.m_localPlayer) && !TestAsRemote ? player.m_eye.forward
            : anim != null ? anim.m_headLookDir : player.transform.forward;
        if (look.sqrMagnitude < 0.0001f)
        {
            look = player.transform.forward;
        }
        return look.normalized;
    }

    // Between the eyes: the head bone sits low in the head (jaw level), eyes are higher and in front (same offset
    // first-person camera mods use), turned with the look.
    private static Vector3 Eyes(Player player, Vector3 look)
    {
        var head = player.m_head;
        if (head == null)
        {
            return player.m_eye.position;
        }
        return head.position + Quaternion.LookRotation(look) * EyesFromHead;
    }

    private static void TwoBone(Rig rig, Vector3 target, Transform body)
    {
        var upper = rig.Upper;
        var lower = rig.Lower;
        var hand = rig.Hand;
        var s = upper.position;
        var a = (lower.position - s).magnitude;
        var b = (hand.position - lower.position).magnitude;
        if (a < 0.001f || b < 0.001f)
        {
            return;
        }
        var toTarget = target - s;
        var d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 0.001f, (a + b) * 0.999f);
        var dir = toTarget.sqrMagnitude > 0.000001f ? toTarget.normalized : body.forward;
        // Elbow hint: out to the right and down.
        var hint = -body.up * 0.6f + body.right * 0.55f - body.forward * 0.1f;
        var pole = hint - dir * Vector3.Dot(hint, dir);
        pole = pole.sqrMagnitude > 0.000001f ? pole.normalized : -body.up;
        var cos = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
        var sin = Mathf.Sqrt(1f - cos * cos);
        var elbow = s + dir * (a * cos) + pole * (a * sin);

        upper.rotation = Quaternion.FromToRotation(lower.position - s, elbow - s) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
    }

    private static void RestoreAnimated(Rig rig)
    {
        if (rig.Written && rig.Upper != null && rig.Lower != null && rig.Hand != null)
        {
            rig.Upper.localRotation = rig.AnimUpper;
            rig.Lower.localRotation = rig.AnimLower;
            rig.Hand.localRotation = rig.AnimHand;
        }
        rig.Written = false;
    }

    private static bool FindBones(Rig rig)
    {
        var animator = rig.Player.m_animator;
        Transform upper = null, lower = null, hand = null;
        if (animator != null && animator.isHuman)
        {
            upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        var root = animator != null ? animator.transform : rig.Player.transform;
        if (upper == null)
        {
            upper = Find(root, "RightArm");
        }
        if (lower == null)
        {
            lower = Find(root, "RightForeArm");
        }
        if (hand == null)
        {
            hand = Find(root, "RightHand");
        }
        if (upper == null || lower == null || hand == null)
        {
            rig.Broken = true;
            Log.Warning("The spyglass arm pose found no right arm bones on a player model; the arm stays as animated.");
            return false;
        }
        rig.Upper = upper;
        rig.Lower = lower;
        rig.Hand = hand;
        return true;
    }

    // Eyepiece marker inside the hand copy of the spyglass (re-found when the game made a new copy).
    private static bool FindEyepiece(Rig rig)
    {
        var instance = rig.Player.m_visEquipment != null ? rig.Player.m_visEquipment.m_rightItemInstance : null;
        if (instance == null)
        {
            return false;
        }
        if (!ReferenceEquals(instance, rig.ItemInstance) || rig.Eyepiece == null)
        {
            rig.ItemInstance = instance;
            rig.Eyepiece = Find(instance.transform, SpyglassModel.EyepieceName);
        }
        return rig.Eyepiece != null;
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }
        for (var i = 0; i < root.childCount; i++)
        {
            var found = Find(root.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

#if DEBUG
    internal static string Describe(Player player)
    {
        if (player == null || !Rigs.TryGetValue(player.GetInstanceID(), out var rig) || rig.Upper == null)
        {
            return "no rig";
        }
        return $"bones {rig.Upper.name}/{rig.Lower.name}/{rig.Hand.name}, eyepiece {(rig.Eyepiece != null ? "found" : "missing")}, written {rig.Written}";
    }

    internal static float EyepieceGap(Player player)
    {
        if (player == null || !Rigs.TryGetValue(player.GetInstanceID(), out var rig) || rig.Eyepiece == null)
        {
            return -1f;
        }
        return (rig.Eyepiece.position - Eyes(player, LookDirection(player))).magnitude;
    }

    // Eyepiece minus eyes, in the look frame (right, up, forward), metres.
    internal static string EyepieceOffset(Player player)
    {
        if (player == null || !Rigs.TryGetValue(player.GetInstanceID(), out var rig) || rig.Eyepiece == null)
        {
            return "no eyepiece";
        }
        var look = LookDirection(player);
        var eyes = Eyes(player, look);
        var right = Vector3.Cross(Vector3.up, look).normalized;
        var up = Vector3.Cross(look, right);
        var d = rig.Eyepiece.position - eyes;
        return $"eyepiece offset right {Vector3.Dot(d, right):0.###} up {Vector3.Dot(d, up):0.###} forward {Vector3.Dot(d, look):0.###}; "
               + $"eyes {eyes} head {(player.m_head != null ? player.m_head.position.ToString() : "-")} eye {player.m_eye.position}";
    }

    // Cosine between the tube and the look (1 = along it).
    internal static float TubeAlignment(Player player)
    {
        if (player == null || !Rigs.TryGetValue(player.GetInstanceID(), out var rig) || rig.Eyepiece == null)
        {
            return -1f;
        }
        return Vector3.Dot(rig.Eyepiece.forward, LookDirection(player));
    }
#endif
}
