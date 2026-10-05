using System;
using System.Collections.Generic;
using MC.Shared;
using UnityEngine;

namespace MC.Exploration.MusicInstrumentsMod;

// Me = playing pose, made in code (no animation clip without asset bundle). Each rendered frame, after the animator
// (Player.LateUpdate postfix, every player, every game), me bend both arms (two-bone IK, elbow hint per arm) so the
// hands hold the instrument right, blended in by a weight over the animated arms:
//   Flute       right hand put beak (MouthMarker) at mouth, tube forward-down, finger holes face forward-up; left hand
//               beside tube on upper holes.
//   Lyre        me lift lyre out of hand onto body (against belly, or on left thigh when seated); left hand hold top of
//               its left arm, right hand pluck strings (sweep on each note).
//   Tambourine  right hand raise it front-right at chin height, disc above grip, skin face forward-left, shake on each
//               note; left hand hover before skin, tap it on Hit/Thump notes.
// Weight: local player = Performance.PoseWeight while it play the instrument in hand (and its fade out after); other
// players = their ZDO int PlayingKey same as instrument in their right hand (VisEquipment hash), each game blend on its
// own. Legs never touched: seated player stay seated.
// Body frame from bone positions (chest, neck) and player forward: no bone axis convention needed. Hands turned by
// marker or finger axes measured from bones (FromToRotation, LookRotation), never by guessed hand-bone axes.
// Player animator run with physics (AnimatePhysics): frame without physics step leave bones as me wrote them. So me
// remember animated rotations: bone still holding my last write = animator not run = me put animated one back before
// posing again (never pose on top of own pose). Per bone: bones animator never write (fingers on some rigs) never drift.
// Models real size (scale ~1, InstrumentContent): tuning below in metres for flute 0.36 m, lyre 0.55 m, tambourine 0.26 m.
internal static class InstrumentPose
{
    // Player ZDO int: InstrumentKind while that player play, 0 when not. Owner (Performance) write it on change.
    internal static readonly int PlayingKey = (ModInfo.Guid + ".Playing").GetStableHashCode();

    // Synced animator bool Player.AttachStart set through m_zanim for chair, throne, ship seat, saddle (Chair,
    // ShipControlls, Sadle, Barber): every game see it on every player.
    private static readonly int ChairBool = ZSyncAnimation.GetHash("attach_chair");

    // ---------- tuning (unverified in game: self-test screenshots) ----------
    // Body frame vectors: x = right, y = up (chest to neck), z = forward. Metres.

    private const float RemoteInSeconds = 0.35f;
    private const float RemoteOutSeconds = 0.3f;
    private const float SeatSeconds = 0.35f;
    private const float PulseKeepSeconds = 1f;  // note heard this long before pose start still play; older one dropped
    private const float TwistShare = 0.5f;      // forearm take this share of extra wrist twist
    private const float MaxHeldTwist = 60f;     // holding hand turn round item axis at most this far past shortest turn
    private const float PalmWeight = 1f;        // how hard free hand turn palm to wanted side
    private const float DefaultReach = 0.085f;  // wrist to finger knuckles when rig has no finger bones
    private const float PalmReach = 0.6f;       // palm centre, share of wrist-to-knuckles
    private const float FingerReach = 1.45f;    // middle of fingers, share of wrist-to-knuckles
    private const float ArmReachShare = 0.97f;  // arm reach me count on, share of upper arm + forearm

    // Flute (end-blown, like recorder). Model: beak tip 0.29 m from grip, upper holes 0.165 m, tube radius 1.2 cm.
    private static readonly Vector3 MouthFromHead = new Vector3(0f, 0.05f, 0.11f); // head look space, from head bone
    private const float HeadFollow = 0.5f;   // head turn share of look (CharacterAnimEvent m_lookWeight 0.5)
    private const float HeadMaxLook = 90f;
    private const float FluteDown = 45f;     // tube degrees below body horizontal (recorder way: out in front, seen)
    private const float FluteDownMin = 30f;
    private const float FluteDownMax = 65f;
    private const float FlutePitchFollow = 0.8f; // head look down = tube steeper
    private const float MouthInset = 0.01f;  // beak this far between lips
    private const float BobDegrees = 4f;
    private const float BobSeconds = 0.18f;
    private const float BobAttack = 150f;    // degrees per second
    private const float LiftDegrees = 25f;
    private const float LiftSeconds = 0.16f;
    private const float LiftAttack = 12f;    // share per second
    // Left hand wrap tube from left: palm centre left of tube axis (hand bone line ~1.5 cm inside palm + tube radius),
    // level with axis; palm face back-right onto tube, fingers forward-right over front holes (thumb behind).
    private static readonly Vector3 FluteLeftPalm = new Vector3(-0.028f, 0f, 0f); // palm centre from tube axis at marker
    private static readonly Vector3 FluteLeftFingers = new Vector3(0.7f, 0f, 0.5f);
    private static readonly Vector3 FluteLeftPalmFaces = new Vector3(0.5f, 0f, -0.7f);
    private static readonly Vector3 FluteRightElbow = new Vector3(0.5f, -0.75f, -0.05f);
    private static readonly Vector3 FluteLeftElbow = new Vector3(-0.55f, -0.75f, 0f);
    private const float FluteLeftFallback = 0.125f; // no marker: upper holes this far down tube from beak tip
    private static readonly Vector3 FluteLeftModel = new Vector3(0f, 0f, 0.165f); // no marker, no mouth: model units
    // Model turn -> MouthMarker turn (half turn round Y: forward = down tube, up = holes).
    private static readonly Quaternion BeakTurn = new Quaternion(0f, 1f, 0f, 0f);

    // Lyre. Model: 0.55 m tall, origin bottom centre, back of soundbox (BodyMarker) 0.124 m up, strings middle
    // (StrumMarker) 0.268 m up, left arm grip (LeftHandMarker) 0.455 m up and 0.091 m out on +X.
    private const float LyreLean = 20f;   // top toward left shoulder
    private const float LyreTilt = 10f;   // top away from body
    private const float LyreTurn = 10f;   // strings turned toward right hand
    // Back of soundbox on belly: bottom at belt, top at left shoulder (lean), strings middle at lower chest.
    private static readonly Vector3 LyreContact = new Vector3(-0.08f, -0.24f, 0.15f);
    private static readonly Vector3 LyreLapTop = new Vector3(-0.14f, 0.02f, 0.22f);   // seated: top lean here
    private static readonly Vector3 LyreLapNoLegs = new Vector3(-0.08f, -0.45f, 0.28f);
    private const float LapAlongThigh = 0.55f;
    private const float LapAbove = 0.08f;
    private const float SeatedThigh = 0.5f;      // thigh closer to flat than this (cos from straight down) = flat
    private const float SeatHoldSeconds = 0.3f;  // both thighs flat this long = seated (no chair flag, no sit tag)
    private const float StrumSweepSeconds = 0.12f;
    private const float StrumReturnSeconds = 0.25f;
    private const float StrumSweepWidth = 0.025f; // about one string either side (strings 2 cm apart at middle)
    private const float StrumTouch = 0.012f;  // finger middle this far before string plane at pluck
    private const float StrumLift = 0.035f;
    private const float StrumRestHover = 0.06f;
    private const float PluckSmoothing = 0.03f;
    private const int LyreLowPitch = 52;
    private const int LyreHighPitch = 84;
    // Lyre frame (model axes): +X = player's left, +Y = strings' face (forward), +Z = up the lyre.
    private static readonly Vector3 PluckFingers = new Vector3(0.8f, -0.15f, -0.45f);   // +X left, -Y into strings, -Z down
    private static readonly Vector3 LyreLeftFingers = new Vector3(-0.35f, 0.25f, 0.9f); // up arm, in, forward
    private const float LyreLeftOutside = 0.035f; // palm centre this far out past marker (arm 4 cm wide, palm 1.5 cm)
    private static readonly Vector3 LyreRightElbow = new Vector3(0.6f, -0.6f, -0.25f);
    private static readonly Vector3 LyreLeftElbow = new Vector3(-0.6f, -0.7f, 0f);
    // No marker: model points (model units = metres at scale 1).
    private static readonly Vector3 LyreStrumModel = new Vector3(0f, 0.026f, 0.268f);
    private static readonly Vector3 LyreArmModel = new Vector3(0.091f, 0f, 0.455f);
    private static readonly Vector3 LyreBodyModel = new Vector3(0f, -0.015f, 0.124f);

    // Tambourine. Model: origin = grip on rim, disc centre 0.13 m along +Z, skin (LeftHandMarker) on +Y face.
    private static readonly Vector3 TambourineFromShoulder = new Vector3(0.05f, -0.02f, 0.28f); // grip, from right shoulder
    private static readonly Vector3 TambourineSkinFaces = new Vector3(-0.7f, 0.1f, 0.7f);
    private static readonly Vector3 TambourineSkinModel = new Vector3(0f, 0.0232f, 0.13f); // no marker: model units
    private const float TambourineMaxPull = 0.15f; // grip come at most this far toward left hand when it can't reach
    private const float ShakeDegrees = 15f;
    private const float ShakeHz = 9f;
    private const float ShakeSeconds = 0.25f;
    private const float ShakeLongSeconds = 0.5f;
    private const float ShakeAttack = 500f; // degrees per second
    private const float TapHover = 0.12f;
    private const float TapTouch = 0.025f;
    private const float TapSeconds = 0.14f;
    private const float TapAttack = 25f;
    private static readonly Vector3 TapHoverShift = new Vector3(-0.04f, -0.06f, 0f);
    private static readonly Vector3 TambourineRightElbow = new Vector3(0.75f, -0.6f, -0.1f);
    private static readonly Vector3 TambourineLeftElbow = new Vector3(-0.4f, -0.8f, 0f);

    // ---------- rig ----------

    private const int Right = 0;
    private const int Left = 1;
    private const int RightUpper = 0;
    private const int RightLower = 1;
    private const int RightHand = 2;
    private const int LeftUpper = 3;
    private const int LeftLower = 4;
    private const int LeftHand = 5;
    private const int FirstFinger = 6; // left index, middle, ring, then right index, middle, ring
    private const int FingerCount = 6;
    private const int BoneCount = 12;

    private static readonly string[] BoneNames =
    {
        "RightArm", "RightForeArm", "RightHand", "LeftArm", "LeftForeArm", "LeftHand",
        "LeftHandIndex1", "LeftHandMiddle1", "LeftHandRing1", "RightHandIndex1", "RightHandMiddle1", "RightHandRing1",
    };

    private static readonly HumanBodyBones[] BoneIds =
    {
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftRingProximal,
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightRingProximal,
    };

    // Rise fast, fall back over its seconds (pulse animations; no jump when notes come quick).
    private struct Env
    {
        internal float Goal;
        internal float Value;
        internal float Fade;

        internal void Kick(float amount, float seconds)
        {
            if (amount > Goal)
            {
                Goal = amount;
            }
            Fade = seconds > 0.001f ? Goal / seconds : 1000f;
        }

        internal float Step(float dt, float attack)
        {
            Goal = Mathf.Max(0f, Goal - Fade * dt);
            Value = Value < Goal ? Mathf.Min(Goal, Value + attack * dt) : Goal;
            return Value;
        }

        internal void Clear()
        {
            Goal = 0f;
            Value = 0f;
            Fade = 0f;
        }
    }

    private sealed class Rig
    {
        internal Player Player;
        internal bool Broken;      // bones missing or failure: never try again for this player
        internal bool BonesFound;
        internal readonly Transform[] Bones = new Transform[BoneCount];
        internal readonly Quaternion[] Anim = new Quaternion[BoneCount];
        internal readonly Quaternion[] Written = new Quaternion[BoneCount];
        internal readonly bool[] Wrote = new bool[BoneCount];
        internal bool AnyWrote;
        internal Transform Chest;
        internal Transform Neck;
        internal Transform Head;
        internal Transform LeftThigh;
        internal Transform LeftKnee;
        internal Transform RightThigh;
        internal Transform RightKnee;
        internal int ChairBoolState;   // 0 = not looked yet, 1 = animator has attach_chair, -1 = has not
        // Per hand (Right, Left), in hand-bone space, measured from finger bones once.
        internal readonly Vector3[] FingerLocal = new Vector3[2];
        internal readonly Vector3[] PalmLocal = new Vector3[2];
        internal readonly bool[] HasPalm = new bool[2];
        internal readonly float[] Reach = new float[2];

        // Hand copy of instrument and its markers (found again when game made new copy).
        internal GameObject Instance;
        internal InstrumentKind MarkersKind;
        internal Transform Model;
        internal Transform Mouth;
        internal Transform LeftMarker;
        internal Transform Strum;
        internal Transform BodyMarker;
        // Lyre lifted out of hand: its local pose before, put back on stop.
        internal bool InstanceMoved;
        internal GameObject MovedInstance;
        internal Vector3 InstanceLocalPos;
        internal Quaternion InstanceLocalRot;

        internal InstrumentKind Kind;  // posed now
        internal float Progress;       // other players: own blend 0..1
        internal float Weight;         // last weight used
        internal float Seat = -1f;     // 0 standing .. 1 seated (lyre on lap); -1 = not posed yet
        internal float LegsFlatFor;    // seconds both thighs flat in a row

        // Pulses (notes heard).
        internal float PulseAt = -100f;
        internal Env Bob;
        internal readonly Env[] Lift = new Env[FingerCount];
        internal Env Shake;
        internal float ShakePhase;
        internal Env Tap;
        internal float StrumAt = -100f;
        internal float StrumU = 0.5f;
        internal Vector3 PluckLocal;
        internal bool PluckValid;
        internal bool LyreMirrored;    // LeftHandMarker on -X (player's right): left hand on other arm

        // Self tests and Describe.
        internal Vector3 MouthTarget;
        internal int MouthFrame = -1;
        internal Vector3 LeftTarget;
        internal float LeftReach;
        internal int LeftFrame = -1;
        internal readonly float[] TwistWanted = new float[2]; // holding hand: degrees round item axis it wanted
        internal float TambourinePull;                         // metres grip came toward left hand
    }

    // Body frame: origin chest bone, up chest to neck, forward = player forward flat to up, right = up x forward.
    private readonly struct Body
    {
        internal readonly Vector3 Origin;
        internal readonly Vector3 Up;
        internal readonly Vector3 Forward;
        internal readonly Vector3 Right;

        internal Body(Vector3 origin, Vector3 up, Vector3 forward, Vector3 right)
        {
            Origin = origin;
            Up = up;
            Forward = forward;
            Right = right;
        }

        internal Vector3 Dir(Vector3 v) => Right * v.x + Up * v.y + Forward * v.z;

        internal Vector3 Point(Vector3 v) => Origin + Dir(v);
    }

    private static readonly Dictionary<int, Rig> Rigs = new Dictionary<int, Rig>();
    private static readonly List<int> DeadRigs = new List<int>();
    private static float _nextCleanup;
    private static bool _bonesWarned;
    private static int _markerLogMask;

#if DEBUG
    // Self test: pose local player like other players' games do (ZDO int, own blend).
    internal static bool TestAsRemote;
#else
    private const bool TestAsRemote = false;
#endif

    // ---------- API ----------

    // Player.LateUpdate postfix (every player, every game).
    internal static void Update(Player player)
    {
        if (player == null)
        {
            return;
        }
        if (Time.unscaledTime >= _nextCleanup)
        {
            Cleanup();
        }
        var visEq = player.m_visEquipment;
        var held = visEq != null ? InstrumentContent.KindOfHash(visEq.m_currentRightItemHash) : InstrumentKind.None;
        Rigs.TryGetValue(player.GetInstanceID(), out var rig);
        if (rig == null)
        {
            if (held == InstrumentKind.None)
            {
                return;
            }
            rig = new Rig { Player = player };
            Rigs[player.GetInstanceID()] = rig;
        }
        if (rig.Broken || (held == InstrumentKind.None && !rig.AnyWrote && !rig.InstanceMoved))
        {
            return;
        }
        try
        {
            var weight = TargetWeight(rig, player, held);
            Apply(rig, visEq, held, weight);
        }
        catch (Exception e)
        {
            rig.Broken = true;
            try
            {
                PutBack(rig);
            }
            catch (Exception)
            {
                // Me already report the first one.
            }
            PatchGuard.Report("InstrumentPose.Update", e);
        }
    }

    // Note sound now for this player (local performer or remote one heard). Kept in rig, used by pose.
    internal static void Pulse(Player player, InstrumentKind kind, byte pitch, byte velocity)
    {
        try
        {
            if (player == null || kind == InstrumentKind.None)
            {
                return;
            }
            var id = player.GetInstanceID();
            if (!Rigs.TryGetValue(id, out var rig))
            {
                rig = new Rig { Player = player };
                Rigs[id] = rig;
            }
            if (rig.Broken)
            {
                return;
            }
            rig.PulseAt = Time.time;
            var loud = 0.45f + 0.55f * Mathf.Clamp01(velocity / 127f);
            switch (kind)
            {
                case InstrumentKind.Flute:
                    rig.Bob.Kick(BobDegrees * loud, BobSeconds);
                    rig.Lift[pitch % FingerCount].Kick(loud, LiftSeconds);
                    break;
                case InstrumentKind.Lyre:
                    rig.StrumAt = Time.time;
                    rig.StrumU = Mathf.Clamp01((pitch - LyreLowPitch) / (float)(LyreHighPitch - LyreLowPitch));
                    break;
                case InstrumentKind.Tambourine:
                    var accent = pitch <= (byte)TambourineHit.Hit;
                    var shake = accent ? 1f : pitch == (byte)TambourineHit.Shake ? 0.8f : 0.7f;
                    rig.Shake.Kick(ShakeDegrees * shake * loud,
                        pitch == (byte)TambourineHit.Shake ? ShakeLongSeconds : ShakeSeconds);
                    if (accent)
                    {
                        rig.Tap.Kick(Mathf.Lerp(0.7f, 1f, loud), TapSeconds);
                    }
                    break;
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("InstrumentPose.Pulse", e);
        }
    }

    // Local performance aborted: animated arms and item's hand pose back now.
    internal static void ReleaseLocal()
    {
        try
        {
            var p = Player.m_localPlayer;
            if (p != null && Rigs.TryGetValue(p.GetInstanceID(), out var rig))
            {
                PutBack(rig);
                rig.Progress = 0f;
                rig.Weight = 0f;
                rig.Seat = -1f;
                rig.Kind = InstrumentKind.None;
                ClearPulses(rig);
            }
        }
        catch (Exception e)
        {
            PatchGuard.Report("InstrumentPose.ReleaseLocal", e);
        }
    }

    // Feature off: every arm and item back, nothing remembered.
    internal static void Shutdown()
    {
        foreach (var rig in Rigs.Values)
        {
            try
            {
                if (rig.Player != null)
                {
                    PutBack(rig);
                }
            }
            catch (Exception e)
            {
                PatchGuard.Report("InstrumentPose.Shutdown", e);
            }
        }
        Rigs.Clear();
        DeadRigs.Clear();
    }

    // ---------- frame ----------

    private static float TargetWeight(Rig rig, Player player, InstrumentKind held)
    {
        if (held == InstrumentKind.None || player.IsDead())
        {
            rig.Progress = 0f;
            return 0f;
        }
        if (ReferenceEquals(player, Player.m_localPlayer) && !TestAsRemote)
        {
            // Performance blend it (in 0.35 s, out 0.3 s). Me take it only for instrument it play now, or for fade out
            // of the one me pose (nothing play); other instrument in hand = arms as animated.
            var playing = Performance.Instrument;
            var p = playing == held || (playing == InstrumentKind.None && rig.Kind == held)
                ? Mathf.Clamp01(Performance.PoseWeight)
                : 0f;
            rig.Progress = p;
            return Smooth(p);
        }
        var view = player.m_nview;
        var flagged = view != null && view.IsValid() && view.GetZDO().GetInt(PlayingKey) == (int)held;
        if (!flagged && rig.Kind != InstrumentKind.None && rig.Kind != held)
        {
            // Other instrument in hand than the posed one, not played: no fade out on wrong item.
            rig.Progress = 0f;
            return 0f;
        }
        var dt = Time.deltaTime;
        rig.Progress = flagged
            ? Mathf.Min(1f, rig.Progress + dt / RemoteInSeconds)
            : Mathf.Max(0f, rig.Progress - dt / RemoteOutSeconds);
        return Smooth(rig.Progress);
    }

    private static void Apply(Rig rig, VisEquipment visEq, InstrumentKind held, float weight)
    {
        // Skeleton gone under me (model rebuilt): look again, nothing to put back on dead bones.
        if (rig.BonesFound && (rig.Chest == null || rig.Bones[RightHand] == null || rig.Bones[LeftHand] == null))
        {
            rig.BonesFound = false;
            for (var i = 0; i < BoneCount; i++)
            {
                rig.Wrote[i] = false;
            }
        }
        if (!rig.BonesFound && !FindBones(rig))
        {
            return;
        }
        RestoreOrCapture(rig);

        var instance = visEq != null ? visEq.m_rightItemInstance : null;
        if (!ReferenceEquals(instance, rig.Instance) || rig.MarkersKind != held)
        {
            // New hand copy (equip, quality change...): old one back if it still live, markers again.
            PutBackInstance(rig);
            rig.Instance = instance;
            FindMarkers(rig, held);
            rig.PluckValid = false;
        }
        if (weight <= 0.001f || held == InstrumentKind.None || instance == null || !instance.activeInHierarchy)
        {
            PutBackInstance(rig);
            // Posed instrument gone from hand: its pulses mean nothing more. Same one still held: keep them (first
            // notes of next play can come before ZDO flag; pose start drop old ones).
            if (rig.Kind != InstrumentKind.None && rig.Kind != held)
            {
                ClearPulses(rig);
            }
            rig.Kind = InstrumentKind.None;
            rig.Weight = 0f;
            rig.Seat = -1f;
            return;
        }
        if (rig.Kind != held)
        {
            // Pose start (or other instrument). Other instrument's pulses go; at start, notes heard just before stay
            // (remote first batch can come before ZDO flag), old ones go (Env stand still while not posed).
            if (rig.Kind != InstrumentKind.None || Time.time - rig.PulseAt > PulseKeepSeconds)
            {
                ClearPulses(rig);
            }
            rig.Kind = held;
            rig.LegsFlatFor = 0f;
        }
        rig.Weight = weight;
        var body = BodyFrame(rig);
        var dt = Time.deltaTime;
        switch (held)
        {
            case InstrumentKind.Flute:
                PoseFlute(rig, body, weight, dt);
                break;
            case InstrumentKind.Lyre:
                PoseLyre(rig, body, weight, dt);
                break;
            case InstrumentKind.Tambourine:
                PoseTambourine(rig, body, weight, dt);
                break;
        }
    }

    // Per bone: still holding my write = animator not run = animated one back; else animator's new pose kept.
    private static void RestoreOrCapture(Rig rig)
    {
        for (var i = 0; i < BoneCount; i++)
        {
            var bone = rig.Bones[i];
            if (bone == null)
            {
                continue;
            }
            if (rig.Wrote[i] && bone.localRotation == rig.Written[i])
            {
                bone.localRotation = rig.Anim[i];
            }
            else
            {
                rig.Anim[i] = bone.localRotation;
            }
            rig.Wrote[i] = false;
        }
        rig.AnyWrote = false;
    }

    private static Body BodyFrame(Rig rig)
    {
        var t = rig.Player.transform;
        var origin = rig.Chest.position;
        var top = rig.Neck != null ? rig.Neck.position : rig.Head.position;
        var up = top - origin;
        up = up.sqrMagnitude > 0.0001f ? up.normalized : t.up;
        var fwd = t.forward - up * Vector3.Dot(t.forward, up);
        if (fwd.sqrMagnitude < 0.0001f)
        {
            fwd = Vector3.Cross(t.right, up);
        }
        fwd.Normalize();
        var right = Vector3.Cross(up, fwd);
        return new Body(origin, up, fwd, right);
    }

    // ---------- flute ----------

    private static void PoseFlute(Rig rig, in Body body, float weight, float dt)
    {
        var hand = rig.Bones[RightHand];
        var model = ModelOf(rig);
        // Beak and its turn now (hand as animated, item stiff in it). MouthMarker: forward = down tube, up = holes.
        Vector3 mouthPos;
        Quaternion mouthRot;
        if (rig.Mouth != null)
        {
            mouthPos = rig.Mouth.position;
            mouthRot = rig.Mouth.rotation;
        }
        else
        {
            mouthPos = model.TransformPoint(new Vector3(0f, 0f, InstrumentModels.Length(InstrumentKind.Flute)));
            mouthRot = model.rotation * BeakTurn;
        }

        var bob = rig.Bob.Step(dt, BobAttack);
        var target = FluteTarget(rig, body, bob, out var tube);
        rig.MouthTarget = target;
        rig.MouthFrame = Time.frameCount;
        // Whole beak turn wanted: tube way exact, holes (marker up) face forward-up away from player (wrist twist capped).
        var want = Quaternion.LookRotation(tube, Perp(body.Up, tube, body.Forward));
        var inv = Quaternion.Inverse(hand.rotation);
        SolveHeld(rig, Right, target, want, inv * mouthRot, Vector3.forward, inv * (mouthPos - hand.position),
            body.Dir(FluteRightElbow), body);
        CommitArm(rig, Right, weight);
        StepLifts(rig, dt);
        for (var k = 3; k < FingerCount; k++)
        {
            LiftFinger(rig, k, Right, weight);
        }

        // Left hand on upper holes, where flute is now: palm centre beside tube axis, level with marker.
        var tubeNow = rig.Mouth != null ? rig.Mouth.forward : -model.forward;
        var fingers = Perp(body.Dir(FluteLeftFingers), tubeNow, body.Right);
        var contact = FluteHoles(rig, model) + body.Dir(FluteLeftPalm);
        SolveFree(rig, Left, contact, PalmReach, fingers, body.Dir(FluteLeftPalmFaces), body.Dir(FluteLeftElbow), body);
        CommitArm(rig, Left, weight);
        for (var k = 0; k < 3; k++)
        {
            LiftFinger(rig, k, Left, weight);
        }
    }

    // Tube axis point under upper holes. LeftHandMarker sit on tube top: me drop it onto model Z line (tube axis).
    private static Vector3 FluteHoles(Rig rig, Transform model)
    {
        if (rig.LeftMarker != null)
        {
            var axis = model.forward;
            return model.position + axis * Vector3.Dot(rig.LeftMarker.position - model.position, axis);
        }
        if (rig.Mouth != null)
        {
            return rig.Mouth.position + rig.Mouth.forward * FluteLeftFallback;
        }
        return model.TransformPoint(FluteLeftModel);
    }

    // Where beak go (mouth, bit between lips) and tube way (forward-down, turned with head). Pure: write nothing.
    private static Vector3 FluteTarget(Rig rig, in Body body, float bobDegrees, out Vector3 tube)
    {
        var facing = HeadFacing(rig, body);
        var mouth = rig.Head.position + Quaternion.LookRotation(facing, body.Up) * MouthFromHead;
        var flat = facing - body.Up * Vector3.Dot(facing, body.Up);
        flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : body.Forward;
        var pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(facing, body.Up), -1f, 1f)) * Mathf.Rad2Deg;
        var down = Mathf.Clamp(FluteDown - pitch * FlutePitchFollow, FluteDownMin, FluteDownMax) - bobDegrees;
        var rad = down * Mathf.Deg2Rad;
        tube = flat * Mathf.Cos(rad) - body.Up * Mathf.Sin(rad);
        return mouth - tube * MouthInset;
    }

    // Where head face: look target every game has (CharacterAnimEvent m_headLookDir, smoothed from ZDO for others),
    // but only part way, as look-at IK turn head (weight 0.5).
    private static Vector3 HeadFacing(Rig rig, in Body body)
    {
        var anim = rig.Player.m_animEvent;
        var look = anim != null ? anim.m_headLookDir : body.Forward;
        if (look.sqrMagnitude < 0.0001f)
        {
            return body.Forward;
        }
        look.Normalize();
        var angle = Vector3.Angle(body.Forward, look);
        var turn = Mathf.Min(angle, HeadMaxLook) * HeadFollow;
        var facing = Vector3.RotateTowards(body.Forward, look, turn * Mathf.Deg2Rad, 0f);
        return facing.sqrMagnitude > 0.0001f ? facing.normalized : body.Forward;
    }

    private static void StepLifts(Rig rig, float dt)
    {
        for (var k = 0; k < FingerCount; k++)
        {
            rig.Lift[k].Step(dt, LiftAttack);
        }
    }

    // Finger k (0..2 left, 3..5 right) lifted off its hole: turned toward back of hand.
    private static void LiftFinger(Rig rig, int k, int side, float weight)
    {
        var index = FirstFinger + k;
        var finger = rig.Bones[index];
        var amount = rig.Lift[k].Value;
        if (finger == null || !rig.HasPalm[side] || amount <= 0.01f)
        {
            return;
        }
        var hand = rig.Bones[side == Right ? RightHand : LeftHand];
        var dir = finger.childCount > 0 ? finger.GetChild(0).position - finger.position : hand.rotation * rig.FingerLocal[side];
        if (dir.sqrMagnitude < 0.000001f)
        {
            return;
        }
        dir.Normalize();
        var back = -(hand.rotation * rig.PalmLocal[side]);
        var to = dir + back * Mathf.Tan(LiftDegrees * amount * Mathf.Deg2Rad);
        finger.rotation = Quaternion.FromToRotation(dir, to) * finger.rotation;
        Commit(rig, index, weight);
    }

    // ---------- lyre ----------

    private static void PoseLyre(Rig rig, in Body body, float weight, float dt)
    {
        var model = ModelOf(rig);
        var invModel = Quaternion.Inverse(model.rotation);
        var strumRel = Rel(model, invModel, rig.Strum, LyreStrumModel);
        var armRel = Rel(model, invModel, rig.LeftMarker, LyreArmModel);
        var bodyRel = Rel(model, invModel, rig.BodyMarker, LyreBodyModel);
        // Upright (+Z up), strings (+Y) forward: model +X = player's LEFT, marker sit on that arm. Safety net: marker
        // on other arm (-X) = left hand take mirror point (lyre same both sides).
        rig.LyreMirrored = armRel.x < strumRel.x;
        if (rig.LyreMirrored)
        {
            armRel.x = 2f * strumRel.x - armRel.x;
        }

        UpdateSeat(rig, body, dt);
        WantedLyre(rig, body, bodyRel, out var pos, out var rot);

        // Right hand: pluck point on strings (rest beside them, sweep across on each note).
        var xHalf = Mathf.Clamp((armRel.x - strumRel.x) * 0.55f, 0.025f, 0.08f);
        var goal = PluckGoal(rig, xHalf);
        if (!rig.PluckValid)
        {
            rig.PluckLocal = goal;
            rig.PluckValid = true;
        }
        else
        {
            rig.PluckLocal = Vector3.Lerp(rig.PluckLocal, goal, 1f - Mathf.Exp(-dt / PluckSmoothing));
        }
        var pluck = pos + rot * (strumRel + rig.PluckLocal);
        SolveFree(rig, Right, pluck, FingerReach, rot * PluckFingers.normalized, rot * Vector3.down,
            body.Dir(LyreRightElbow), body);
        CommitArm(rig, Right, weight);

        // Lyre out of hand onto body (blended from hand-held pose).
        PlaceInstance(rig, model, pos, rot, weight);

        // Left hand hold top of lyre's left arm (from outside, palm inward), where lyre is now.
        var turn = model.rotation;
        var outside = turn * Vector3.right;
        var grip = model.position + turn * armRel + outside * LyreLeftOutside;
        SolveFree(rig, Left, grip, PalmReach, turn * LyreLeftFingers.normalized, -outside, body.Dir(LyreLeftElbow), body);
        CommitArm(rig, Left, weight);
    }

    // Seated (lyre on lap), blended. Sure signs: sit tag (IsSitting: sit emote, seats) or synced chair bool (chair,
    // throne, ship seat, saddle). Else both thighs near flat to body for SeatHoldSeconds (seats of other mods); one
    // thigh up = stride (walk, jog), crouch = knees bent, swim = legs kick: never seated by legs alone.
    private static void UpdateSeat(Rig rig, in Body body, float dt)
    {
        var player = rig.Player;
        var seated = player.IsSitting() || ChairOn(rig);
        var flat = !seated && !player.IsCrouching() && !player.IsSwimming()
                   && ThighFlat(rig.LeftThigh, rig.LeftKnee, body) && ThighFlat(rig.RightThigh, rig.RightKnee, body);
        rig.LegsFlatFor = flat ? rig.LegsFlatFor + dt : 0f;
        if (rig.LegsFlatFor >= SeatHoldSeconds)
        {
            seated = true;
        }
        // First posed frame (-1): take seat as it is; then blend.
        var goal = seated ? 1f : 0f;
        rig.Seat = rig.Seat < 0f ? goal : Mathf.MoveTowards(rig.Seat, goal, dt / SeatSeconds);
    }

    // Synced attach_chair bool on. Me check once the animator has it (GetBool on missing one warn every call).
    private static bool ChairOn(Rig rig)
    {
        var animator = rig.Player.m_animator;
        if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized)
        {
            return false;
        }
        if (rig.ChairBoolState == 0)
        {
            rig.ChairBoolState = HasBool(animator, ChairBool) ? 1 : -1;
        }
        return rig.ChairBoolState > 0 && animator.GetBool(ChairBool);
    }

    // Animator has bool parameter with this hash. Parameters array = copy: call once per rig, never per frame.
    private static bool HasBool(Animator animator, int hash)
    {
        if (animator.runtimeAnimatorController == null)
        {
            return false;
        }
        var parameters = animator.parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == hash && parameters[i].type == AnimatorControllerParameterType.Bool)
            {
                return true;
            }
        }
        return false;
    }

    // Thigh (hip to knee) closer to flat to body than SeatedThigh. Missing bone = not flat.
    private static bool ThighFlat(Transform thigh, Transform knee, in Body body)
    {
        if (thigh == null || knee == null)
        {
            return false;
        }
        var v = knee.position - thigh.position;
        return v.sqrMagnitude > 0.0001f && -Vector3.Dot(v.normalized, body.Up) < SeatedThigh;
    }

    // Wanted model pose: standing = upright against left of belly (lean left, top bit out, strings forward); seated =
    // bottom on left thigh, top lean back to chest.
    private static void WantedLyre(Rig rig, in Body body, Vector3 bodyRel, out Vector3 pos, out Quaternion rot)
    {
        var lean = LyreLean * Mathf.Deg2Rad;
        var tilt = LyreTilt * Mathf.Deg2Rad;
        var turn = LyreTurn * Mathf.Deg2Rad;
        var z = body.Up * Mathf.Cos(lean) - body.Right * Mathf.Sin(lean);
        z = (z * Mathf.Cos(tilt) + body.Forward * Mathf.Sin(tilt)).normalized;
        var face = body.Forward * Mathf.Cos(turn) + body.Right * Mathf.Sin(turn);
        var standRot = Quaternion.LookRotation(z, face);
        var standPos = body.Point(LyreContact) - standRot * bodyRel;
        if (rig.Seat <= 0.001f)
        {
            pos = standPos;
            rot = standRot;
            return;
        }
        var bottom = rig.LeftThigh != null && rig.LeftKnee != null
            ? Vector3.Lerp(rig.LeftThigh.position, rig.LeftKnee.position, LapAlongThigh) + body.Up * LapAbove
            : body.Point(LyreLapNoLegs);
        var along = body.Point(LyreLapTop) - bottom;
        along = along.sqrMagnitude > 0.0001f ? along.normalized : body.Up;
        var seatRot = Quaternion.LookRotation(along, face);
        pos = Vector3.Lerp(standPos, bottom, rig.Seat);
        rot = Quaternion.Slerp(standRot, seatRot, rig.Seat);
    }

    // Pluck point from strum marker, lyre frame, metres: x across strings (+ = player's left), y off string plane, z
    // along strings. Rest right of middle; note = sweep toward left across its string (low notes on left).
    private static Vector3 PluckGoal(Rig rig, float xHalf)
    {
        var rest = new Vector3(-xHalf * 0.5f, StrumRestHover, -0.02f);
        var since = Time.time - rig.StrumAt;
        if (since < 0f || since >= StrumSweepSeconds + StrumReturnSeconds)
        {
            return rest;
        }
        var sx = Mathf.Lerp(xHalf, -xHalf, rig.StrumU);
        if (since < StrumSweepSeconds)
        {
            var s = since / StrumSweepSeconds;
            return new Vector3(sx - StrumSweepWidth * (1f - 2f * s), Mathf.Lerp(StrumTouch, StrumLift, Mathf.Abs(2f * s - 1f)), 0f);
        }
        var r = Smooth((since - StrumSweepSeconds) / StrumReturnSeconds);
        return Vector3.Lerp(new Vector3(sx + StrumSweepWidth, StrumLift, 0f), rest, r);
    }

    // Item copy to wanted model pose, blended from where hand hold it. Its local pose saved first.
    private static void PlaceInstance(Rig rig, Transform model, Vector3 modelPos, Quaternion modelRot, float weight)
    {
        var inst = rig.Instance.transform;
        if (!rig.InstanceMoved)
        {
            rig.InstanceLocalPos = inst.localPosition;
            rig.InstanceLocalRot = inst.localRotation;
            rig.MovedInstance = rig.Instance;
            rig.InstanceMoved = true;
        }
        var parent = inst.parent;
        var heldPos = parent != null ? parent.TransformPoint(rig.InstanceLocalPos) : rig.InstanceLocalPos;
        var heldRot = parent != null ? parent.rotation * rig.InstanceLocalRot : rig.InstanceLocalRot;
        var invInst = Quaternion.Inverse(inst.rotation);
        var relRot = invInst * model.rotation;
        var relPos = invInst * (model.position - inst.position);
        var wantRot = modelRot * Quaternion.Inverse(relRot);
        var wantPos = modelPos - wantRot * relPos;
        if (weight >= 0.999f)
        {
            inst.SetPositionAndRotation(wantPos, wantRot);
        }
        else
        {
            inst.SetPositionAndRotation(Vector3.Lerp(heldPos, wantPos, weight), Quaternion.Slerp(heldRot, wantRot, weight));
        }
    }

    // ---------- tambourine ----------

    private static void PoseTambourine(Rig rig, in Body body, float weight, float dt)
    {
        var hand = rig.Bones[RightHand];
        var model = ModelOf(rig);
        var skinRel = Rel(model, Quaternion.Inverse(model.rotation), rig.LeftMarker, TambourineSkinModel);
        // Whole model turn wanted: skin (+Y) face forward-left, grip-to-centre (+Z) up, so disc sit above grip. Skin
        // face exact, spin in disc plane capped (wrist).
        var faces = body.Dir(TambourineSkinFaces).normalized;
        var want = Quaternion.LookRotation(Perp(body.Up, faces, body.Forward), faces);
        var target = rig.Bones[RightUpper].position + body.Dir(TambourineFromShoulder);
        target += ReachPull(rig, body, target + want * skinRel, faces);
        var inv = Quaternion.Inverse(hand.rotation);
        SolveHeld(rig, Right, target, want, inv * model.rotation, Vector3.up, inv * (model.position - hand.position),
            body.Dir(TambourineRightElbow), body);
        CommitArm(rig, Right, weight);

        // Skin as posed, before shake: left hand hover there (no wobble with shake).
        var still = rig.LeftMarker != null ? rig.LeftMarker.position : model.TransformPoint(TambourineSkinModel);
        var stillNormal = model.up;

        // Shake: forearm roll (wrist and tambourine with it), on top of blended pose, faded in with it.
        var amp = rig.Shake.Step(dt, ShakeAttack) * weight;
        rig.ShakePhase += 2f * Mathf.PI * ShakeHz * dt;
        if (rig.ShakePhase > 2f * Mathf.PI)
        {
            rig.ShakePhase -= 2f * Mathf.PI;
        }
        if (amp > 0.01f)
        {
            var lower = rig.Bones[RightLower];
            var axis = hand.position - lower.position;
            if (axis.sqrMagnitude > 0.000001f)
            {
                lower.rotation = Quaternion.AngleAxis(amp * Mathf.Sin(rig.ShakePhase), axis) * lower.rotation;
                Commit(rig, RightLower, 1f);
            }
        }

        // Left hand: hover before still skin; tap go onto skin as it shake now (follow shake only while tap run).
        var u = rig.Tap.Step(dt, TapAttack);
        var skin = still;
        var normal = stillNormal;
        if (u > 0.001f)
        {
            var shaken = rig.LeftMarker != null ? rig.LeftMarker.position : model.TransformPoint(TambourineSkinModel);
            skin = Vector3.Lerp(still, shaken, u);
            normal = Vector3.Slerp(stillNormal, model.up, u);
        }
        var hover = still + stillNormal * TapHover + body.Dir(TapHoverShift);
        var contact = Vector3.Lerp(hover, skin + normal * TapTouch, u);
        var fingers = Perp(body.Up, normal, body.Forward);
        SolveFree(rig, Left, contact, PalmReach, fingers, -normal, body.Dir(TambourineLeftElbow), body);
        CommitArm(rig, Left, weight);
    }

    // Left arm reach to planned skin (hover and touch, wrist points like SolveFree): farther one past arm reach = grip
    // target come toward left shoulder by the gap, never back into chest, never up, at most TambourineMaxPull. Rest
    // (still out of reach) = TwoBone stop hand short.
    private static Vector3 ReachPull(Rig rig, in Body body, Vector3 skin, Vector3 normal)
    {
        rig.TambourinePull = 0f;
        var shoulder = rig.Bones[LeftUpper].position;
        var elbow = rig.Bones[LeftLower].position;
        var reach = ((elbow - shoulder).magnitude + (rig.Bones[LeftHand].position - elbow).magnitude) * ArmReachShare;
        var back = Perp(body.Up, normal, body.Forward) * (rig.Reach[Left] * PalmReach);
        var touch = skin + normal * TapTouch - back;
        var hover = skin + normal * TapHover + body.Dir(TapHoverShift) - back;
        var far = (hover - shoulder).sqrMagnitude > (touch - shoulder).sqrMagnitude ? hover : touch;
        var full = shoulder - far;
        var dist = full.magnitude;
        if (dist - reach <= 0f || dist < 0.001f)
        {
            return Vector3.zero;
        }
        var way = full;
        var ahead = Vector3.Dot(way, body.Forward);
        if (ahead < 0f)
        {
            way -= body.Forward * ahead;
        }
        var rise = Vector3.Dot(way, body.Up);
        if (rise > 0f)
        {
            way -= body.Up * rise;
        }
        var len = way.magnitude;
        if (len < 0.0001f)
        {
            return Vector3.zero;
        }
        way /= len;
        var gain = Mathf.Max(Vector3.Dot(way, full / dist), 0.3f); // gap closed per metre moved
        var shift = Mathf.Min((dist - reach) / gain, TambourineMaxPull);
        rig.TambourinePull = shift;
        return way * shift;
    }

    // ---------- solving ----------

    // Hand that hold item. refLocal = item frame (marker or model turn) in hand space, want = that frame wanted in
    // world, primary = frame axis that must lie exact (beak way, skin face). Hand take shortest turn that lay primary
    // axis on its way (from hand as forearm carry it), then turn round that axis toward wanted second axis, at most
    // MaxHeldTwist (HeldTwist; rest left undone, wrist no wring). Hand placed so item point (contactLocal, hand space)
    // land on target. Two passes: second start from hand as new forearm carry it.
    private static void SolveHeld(Rig rig, int side, Vector3 target, Quaternion want, Quaternion refLocal,
        Vector3 primary, Vector3 contactLocal, Vector3 hint, in Body body)
    {
        var b = side * 3;
        var upper = rig.Bones[b];
        var lower = rig.Bones[b + 1];
        var hand = rig.Bones[b + 2];
        var second = Mathf.Abs(primary.z) < 0.9f ? Vector3.forward : Vector3.up;
        var axisLocal = refLocal * primary;
        var secondLocal = refLocal * second;
        var wantAxis = want * primary;
        var wantSecond = want * second;
        wantSecond -= wantAxis * Vector3.Dot(wantSecond, wantAxis);
        var rot = hand.rotation;
        for (var pass = 0; pass < 2; pass++)
        {
            var current = hand.rotation;
            rot = Quaternion.FromToRotation(current * axisLocal, wantAxis) * current;
            var have = rot * secondLocal;
            have -= wantAxis * Vector3.Dot(have, wantAxis);
            if (have.sqrMagnitude > 0.000001f && wantSecond.sqrMagnitude > 0.000001f)
            {
                var angle = Vector3.SignedAngle(have, wantSecond, wantAxis);
                rig.TwistWanted[side] = angle;
                rot = Quaternion.AngleAxis(HeldTwist(angle), wantAxis) * rot;
            }
            TwoBone(upper, lower, hand, target - rot * contactLocal, hint, body);
        }
        ShareTwist(lower, hand, rot, rig.Anim[b + 2]);
    }

    // Twist me give for wanted twist (degrees, -180..180): all of it up to MaxHeldTwist, then capped, and fading to 0
    // toward a half turn (its way round undefined there: no jump between +cap and -cap, no state).
    private static float HeldTwist(float angle)
    {
        var a = Mathf.Abs(angle);
        var t = Mathf.Max(0f, Mathf.Min(a, Mathf.Min(MaxHeldTwist, 180f - a)));
        return angle < 0f ? -t : t;
    }

    // Free hand: point reach along fingers (palm centre, finger middle) on contact, fingers along wantFinger, palm
    // turned toward wantPalm (when rig has finger bones to know palm).
    private static void SolveFree(Rig rig, int side, Vector3 contact, float reach, Vector3 wantFinger, Vector3 wantPalm,
        Vector3 hint, in Body body)
    {
        var b = side * 3;
        var upper = rig.Bones[b];
        var lower = rig.Bones[b + 1];
        var hand = rig.Bones[b + 2];
        if (wantFinger.sqrMagnitude < 0.000001f)
        {
            wantFinger = body.Forward;
        }
        wantFinger.Normalize();
        var r = rig.Reach[side] * reach;
        TwoBone(upper, lower, hand, contact - wantFinger * r, hint, body);
        var rot = Quaternion.FromToRotation(hand.rotation * rig.FingerLocal[side], wantFinger) * hand.rotation;
        if (rig.HasPalm[side])
        {
            var palm = rot * rig.PalmLocal[side];
            var now = palm - wantFinger * Vector3.Dot(palm, wantFinger);
            var want = wantPalm - wantFinger * Vector3.Dot(wantPalm, wantFinger);
            if (now.sqrMagnitude > 0.000001f && want.sqrMagnitude > 0.000001f)
            {
                var angle = Vector3.SignedAngle(now, want, wantFinger) * PalmWeight;
                rot = Quaternion.AngleAxis(angle, wantFinger) * rot;
            }
        }
        ShareTwist(lower, hand, rot, rig.Anim[b + 2]);
        if (side == Left)
        {
            rig.LeftTarget = contact;
            rig.LeftReach = r;
            rig.LeftFrame = Time.frameCount;
        }
    }

    // Two-bone IK in world space: elbow from law of cosines, bent toward hint (body space, out and down).
    private static void TwoBone(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 hint, in Body body)
    {
        var s = upper.position;
        var a = (lower.position - s).magnitude;
        var b = (hand.position - lower.position).magnitude;
        if (a < 0.001f || b < 0.001f)
        {
            return;
        }
        var toTarget = target - s;
        var d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 0.001f, (a + b) * 0.999f);
        var dir = toTarget.sqrMagnitude > 0.000001f ? toTarget.normalized : body.Forward;
        var pole = hint - dir * Vector3.Dot(hint, dir);
        if (pole.sqrMagnitude < 0.000001f)
        {
            pole = -body.Up - dir * Vector3.Dot(-body.Up, dir);
        }
        pole = pole.sqrMagnitude > 0.000001f ? pole.normalized : -body.Forward;
        var cos = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
        var sin = Mathf.Sqrt(1f - cos * cos);
        var elbow = s + dir * (a * cos) + pole * (a * sin);

        upper.rotation = Quaternion.FromToRotation(lower.position - s, elbow - s) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
    }

    // Hand to its wanted world turn; forearm take TwistShare of extra wrist twist (round forearm axis, against animated
    // wrist), so wrist skin never wring. Hand position never move.
    private static void ShareTwist(Transform lower, Transform hand, Quaternion handWorld, Quaternion handAnimLocal)
    {
        if (hand.parent == lower)
        {
            var axis = hand.localPosition;
            if (axis.sqrMagnitude > 0.00000001f)
            {
                axis.Normalize();
                var extra = Quaternion.Inverse(lower.rotation) * handWorld * Quaternion.Inverse(handAnimLocal);
                var along = axis * Vector3.Dot(new Vector3(extra.x, extra.y, extra.z), axis);
                var twist = new Quaternion(along.x, along.y, along.z, extra.w);
                var size = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
                if (size > 0.00001f)
                {
                    var inv = (twist.w < 0f ? -1f : 1f) / size;
                    twist = new Quaternion(twist.x * inv, twist.y * inv, twist.z * inv, twist.w * inv);
                    lower.localRotation = lower.localRotation * Quaternion.Slerp(Quaternion.identity, twist, TwistShare);
                }
            }
        }
        hand.rotation = handWorld;
    }

    private static void CommitArm(Rig rig, int side, float weight)
    {
        var b = side * 3;
        Commit(rig, b, weight);
        Commit(rig, b + 1, weight);
        Commit(rig, b + 2, weight);
    }

    // Full pose blended from animated one by weight (weight 1 = keep as is); what bone hold now remembered.
    private static void Commit(Rig rig, int i, float weight)
    {
        var bone = rig.Bones[i];
        if (bone == null)
        {
            return;
        }
        if (weight < 0.999f)
        {
            bone.localRotation = Quaternion.Slerp(rig.Anim[i], bone.localRotation, weight);
        }
        rig.Written[i] = bone.localRotation;
        rig.Wrote[i] = true;
        rig.AnyWrote = true;
    }

    // ---------- put back ----------

    // Bones still holding my pose back to animated one; item copy back in hand.
    private static void PutBack(Rig rig)
    {
        for (var i = 0; i < BoneCount; i++)
        {
            var bone = rig.Bones[i];
            if (rig.Wrote[i] && bone != null && bone.localRotation == rig.Written[i])
            {
                bone.localRotation = rig.Anim[i];
            }
            rig.Wrote[i] = false;
        }
        rig.AnyWrote = false;
        PutBackInstance(rig);
    }

    private static void PutBackInstance(Rig rig)
    {
        if (!rig.InstanceMoved)
        {
            return;
        }
        var moved = rig.MovedInstance;
        if (moved != null)
        {
            moved.transform.localPosition = rig.InstanceLocalPos;
            moved.transform.localRotation = rig.InstanceLocalRot;
        }
        rig.InstanceMoved = false;
        rig.MovedInstance = null;
    }

    private static void ClearPulses(Rig rig)
    {
        rig.Bob.Clear();
        for (var k = 0; k < FingerCount; k++)
        {
            rig.Lift[k].Clear();
        }
        rig.Shake.Clear();
        rig.Tap.Clear();
        rig.StrumAt = -100f;
        rig.PluckValid = false;
    }

    private static void Cleanup()
    {
        _nextCleanup = Time.unscaledTime + 10f;
        DeadRigs.Clear();
        foreach (var kv in Rigs)
        {
            if (kv.Value.Player == null)
            {
                DeadRigs.Add(kv.Key);
            }
        }
        for (var i = 0; i < DeadRigs.Count; i++)
        {
            Rigs.Remove(DeadRigs[i]);
        }
        DeadRigs.Clear();
    }

    // ---------- finding ----------

    private static bool FindBones(Rig rig)
    {
        var player = rig.Player;
        var animator = player.m_animator;
        var human = animator != null && animator.isHuman;
        var root = animator != null ? animator.transform : player.transform;
        for (var i = 0; i < BoneCount; i++)
        {
            rig.Bones[i] = Bone(animator, human, root, BoneIds[i], BoneNames[i]);
        }
        rig.Chest = Bone(animator, human, root, HumanBodyBones.UpperChest, "Spine2");
        if (rig.Chest == null)
        {
            rig.Chest = Bone(animator, human, root, HumanBodyBones.Chest, "Spine1");
        }
        rig.Neck = Bone(animator, human, root, HumanBodyBones.Neck, "Neck");
        rig.Head = player.m_head != null ? player.m_head : Bone(animator, human, root, HumanBodyBones.Head, "Head");
        rig.LeftThigh = Bone(animator, human, root, HumanBodyBones.LeftUpperLeg, "LeftUpLeg");
        rig.LeftKnee = Bone(animator, human, root, HumanBodyBones.LeftLowerLeg, "LeftLeg");
        rig.RightThigh = Bone(animator, human, root, HumanBodyBones.RightUpperLeg, "RightUpLeg");
        rig.RightKnee = Bone(animator, human, root, HumanBodyBones.RightLowerLeg, "RightLeg");
        rig.ChairBoolState = 0;
        for (var i = 0; i < 6; i++)
        {
            if (rig.Bones[i] == null)
            {
                return Fail(rig);
            }
        }
        if (rig.Chest == null || rig.Head == null)
        {
            return Fail(rig);
        }
        MeasureHand(rig, Right, animator, human, root);
        MeasureHand(rig, Left, animator, human, root);
        rig.BonesFound = true;
        return true;
    }

    private static bool Fail(Rig rig)
    {
        rig.Broken = true;
        for (var i = 0; i < BoneCount; i++)
        {
            rig.Bones[i] = null;
        }
        if (!_bonesWarned)
        {
            _bonesWarned = true;
            Log.Warning("The instrument playing pose found no arm or chest bones on a player model; that player plays "
                        + "with the arms as animated.");
        }
        return false;
    }

    // Finger way and palm side in hand-bone space, from finger bones (wrist to middle knuckle; index to little knuckle
    // across). No finger bones: forearm way, palm unknown.
    private static void MeasureHand(Rig rig, int side, Animator animator, bool human, Transform root)
    {
        var hand = rig.Bones[side == Right ? RightHand : LeftHand];
        var lower = rig.Bones[side == Right ? RightLower : LeftLower];
        var first = FirstFinger + (side == Right ? 3 : 0);
        var index = rig.Bones[first];
        var middle = rig.Bones[first + 1];
        var ring = rig.Bones[first + 2];
        var little = side == Right
            ? Bone(animator, human, root, HumanBodyBones.RightLittleProximal, "RightHandPinky1")
            : Bone(animator, human, root, HumanBodyBones.LeftLittleProximal, "LeftHandPinky1");
        var knuckle = middle != null ? middle : index != null ? index : ring;
        var inv = Quaternion.Inverse(hand.rotation);
        var finger = knuckle != null ? knuckle.position - hand.position : hand.position - lower.position;
        if (finger.sqrMagnitude < 0.000001f)
        {
            finger = hand.position - lower.position;
        }
        rig.Reach[side] = knuckle != null && finger.magnitude > 0.02f ? finger.magnitude : DefaultReach;
        finger.Normalize();
        rig.FingerLocal[side] = inv * finger;
        rig.HasPalm[side] = false;
        var outer = little != null ? little : ring;
        if (knuckle != null && index != null && outer != null && !ReferenceEquals(index, outer))
        {
            var across = index.position - outer.position;
            var palm = Vector3.Cross(finger, across) * (side == Right ? 1f : -1f);
            palm -= finger * Vector3.Dot(palm, finger);
            if (palm.sqrMagnitude > 0.000001f)
            {
                rig.PalmLocal[side] = inv * palm.normalized;
                rig.HasPalm[side] = true;
            }
        }
    }

    private static Transform Bone(Animator animator, bool human, Transform root, HumanBodyBones id, string name)
    {
        Transform t = null;
        if (human)
        {
            t = animator.GetBoneTransform(id);
        }
        if (t == null)
        {
            t = Find(root, name);
        }
        return t;
    }

    // Markers inside hand copy (found again when game made new copy). Missing ones: offsets from model.
    private static void FindMarkers(Rig rig, InstrumentKind kind)
    {
        rig.MarkersKind = kind;
        rig.Model = null;
        rig.Mouth = null;
        rig.LeftMarker = null;
        rig.Strum = null;
        rig.BodyMarker = null;
        if (rig.Instance == null || kind == InstrumentKind.None)
        {
            return;
        }
        var root = rig.Instance.transform;
        rig.Model = Find(root, InstrumentModels.ModelName);
        rig.LeftMarker = Find(root, InstrumentModels.LeftHandMarker);
        NoteMissing(kind, 0, rig.Model, InstrumentModels.ModelName);
        NoteMissing(kind, 1, rig.LeftMarker, InstrumentModels.LeftHandMarker);
        switch (kind)
        {
            case InstrumentKind.Flute:
                rig.Mouth = Find(root, InstrumentModels.MouthMarker);
                NoteMissing(kind, 2, rig.Mouth, InstrumentModels.MouthMarker);
                break;
            case InstrumentKind.Lyre:
                rig.Strum = Find(root, InstrumentModels.StrumMarker);
                rig.BodyMarker = Find(root, InstrumentModels.BodyMarker);
                NoteMissing(kind, 3, rig.Strum, InstrumentModels.StrumMarker);
                NoteMissing(kind, 4, rig.BodyMarker, InstrumentModels.BodyMarker);
                break;
        }
    }

    private static void NoteMissing(InstrumentKind kind, int marker, Transform found, string name)
    {
        var bit = 1 << ((int)kind * 5 + marker);
        if (found != null || (_markerLogMask & bit) != 0)
        {
            return;
        }
        _markerLogMask |= bit;
        Log.Debug($"The {kind} model has no {name}; the playing pose guesses where it is.");
    }

    private static Transform ModelOf(Rig rig) => rig.Model != null ? rig.Model : rig.Instance.transform;

    // Marker point from model origin, in model turn, metres (fallback given in model units).
    private static Vector3 Rel(Transform model, Quaternion invModel, Transform marker, Vector3 fallbackModelLocal)
    {
        var world = marker != null ? marker.position : model.TransformPoint(fallbackModelLocal);
        return invModel * (world - model.position);
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

    // ---------- math ----------

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // v made square to axis, normalized; fallback when v lie along axis.
    private static Vector3 Perp(Vector3 v, Vector3 axis, Vector3 fallback)
    {
        if (axis.sqrMagnitude > 0.000001f)
        {
            var n = axis.normalized;
            v -= n * Vector3.Dot(v, n);
        }
        return v.sqrMagnitude > 0.000001f ? v.normalized : fallback;
    }

#if DEBUG
    // ---------- self tests ----------

    // Rig state in one line. Bones may be dead (model rebuilt): Unity null check before every read.
    internal static string Describe(Player player)
    {
        if (player == null)
        {
            return "no player";
        }
        if (!Rigs.TryGetValue(player.GetInstanceID(), out var rig))
        {
            return "no rig";
        }
        if (rig.Broken)
        {
            return "broken (bones missing or a failure; arms stay animated)";
        }
        if (!rig.BonesFound)
        {
            return "bones not looked for yet";
        }
        var sb = new System.Text.StringBuilder();
        sb.Append("kind ").Append(rig.Kind).Append(", weight ").Append(rig.Weight.ToString("0.##"))
            .Append(", progress ").Append(rig.Progress.ToString("0.##"))
            .Append(", seat ").Append(rig.Seat.ToString("0.##"))
            .Append(" (sitting ").Append(player.IsSitting())
            .Append(", chair ").Append(rig.ChairBoolState > 0 ? ChairOn(rig).ToString() : rig.ChairBoolState < 0 ? "no bool" : "-")
            .Append(", legs flat ").Append(rig.LegsFlatFor.ToString("0.##")).Append(" s)");
        sb.Append("; bones");
        for (var i = 0; i < 6; i++)
        {
            sb.Append(' ').Append(NameOf(rig.Bones[i]));
        }
        sb.Append(", chest ").Append(NameOf(rig.Chest))
            .Append(", neck ").Append(NameOf(rig.Neck))
            .Append(", head ").Append(NameOf(rig.Head))
            .Append(", thighs ").Append(NameOf(rig.LeftThigh)).Append('/').Append(NameOf(rig.RightThigh))
            .Append(", knees ").Append(NameOf(rig.LeftKnee)).Append('/').Append(NameOf(rig.RightKnee));
        var fingers = 0;
        for (var i = FirstFinger; i < BoneCount; i++)
        {
            if (rig.Bones[i] != null)
            {
                fingers++;
            }
        }
        sb.Append(", fingers ").Append(fingers).Append("/6, palm R ").Append(rig.HasPalm[Right])
            .Append(" L ").Append(rig.HasPalm[Left])
            .Append(", reach R ").Append(rig.Reach[Right].ToString("0.###"))
            .Append(" L ").Append(rig.Reach[Left].ToString("0.###"));
        sb.Append("; markers (").Append(rig.MarkersKind).Append(") model ").Append(rig.Model != null)
            .Append(", mouth ").Append(rig.Mouth != null)
            .Append(", left ").Append(rig.LeftMarker != null)
            .Append(", strum ").Append(rig.Strum != null)
            .Append(", body ").Append(rig.BodyMarker != null);
        if (rig.Kind == InstrumentKind.Lyre)
        {
            sb.Append(rig.LyreMirrored
                ? ", left hand on mirror point (LeftHandMarker on -X, the player's right)"
                : ", left hand on the LeftHandMarker arm (+X)");
        }
        if (rig.Kind == InstrumentKind.Flute || rig.Kind == InstrumentKind.Tambourine)
        {
            sb.Append(", wrist twist wanted ").Append(rig.TwistWanted[Right].ToString("0"))
                .Append(" deg, given ").Append(HeldTwist(rig.TwistWanted[Right]).ToString("0")).Append(" deg");
        }
        if (rig.Kind == InstrumentKind.Tambourine)
        {
            sb.Append(", grip pulled ").Append(rig.TambourinePull.ToString("0.###")).Append(" m toward the left hand");
        }
        var hand = rig.Bones[RightHand];
        sb.Append("; item in hand bone ").Append(rig.Instance != null && hand != null && rig.Instance.transform.IsChildOf(hand))
            .Append(", item moved ").Append(rig.InstanceMoved);
        var written = 0;
        for (var i = 0; i < BoneCount; i++)
        {
            if (rig.Wrote[i])
            {
                written++;
            }
        }
        sb.Append(", bones written ").Append(written);
        var gap = LeftHandGap(player);
        if (gap >= 0f)
        {
            sb.Append(", left hand gap ").Append(gap.ToString("0.###"));
        }
        return sb.ToString();
    }

    private static string NameOf(Transform t) => t != null ? t.name : "-";

    // Marker of instrument in player's right hand, world position.
    internal static bool TryGetMarker(Player player, string markerName, out Vector3 world)
    {
        world = Vector3.zero;
        var visEq = player != null ? player.m_visEquipment : null;
        var instance = visEq != null ? visEq.m_rightItemInstance : null;
        if (instance == null || string.IsNullOrEmpty(markerName))
        {
            return false;
        }
        var marker = Find(instance.transform, markerName);
        if (marker == null)
        {
            return false;
        }
        world = marker.position;
        return true;
    }

    // Where flute beak should be (target pose used this frame, else computed now). Write nothing in rig.
    internal static Vector3 MouthTarget(Player player)
    {
        if (player == null)
        {
            return Vector3.zero;
        }
        if (Rigs.TryGetValue(player.GetInstanceID(), out var rig) && !rig.Broken && rig.BonesFound
            && rig.Chest != null && rig.Head != null)
        {
            if (rig.MouthFrame == Time.frameCount)
            {
                return rig.MouthTarget;
            }
            var body = BodyFrame(rig);
            return FluteTarget(rig, body, rig.Bob.Value, out _);
        }
        var head = player.m_head;
        if (head == null)
        {
            return player.m_eye != null ? player.m_eye.position : player.transform.position;
        }
        var t = player.transform;
        return head.position + Quaternion.LookRotation(t.forward, t.up) * MouthFromHead;
    }

    // Distance from left hand's contact point (palm centre / finger middle) to where pose put it; -1 = none.
    internal static float LeftHandGap(Player player)
    {
        if (player == null || !Rigs.TryGetValue(player.GetInstanceID(), out var rig) || !rig.BonesFound
            || rig.Broken || rig.LeftFrame != Time.frameCount || rig.Weight < 0.999f)
        {
            return -1f;
        }
        var hand = rig.Bones[LeftHand];
        if (hand == null)
        {
            return -1f;
        }
        var point = hand.position + hand.rotation * rig.FingerLocal[Left] * rig.LeftReach;
        return (point - rig.LeftTarget).magnitude;
    }

    internal static float Weight(Player player) =>
        player != null && Rigs.TryGetValue(player.GetInstanceID(), out var rig) ? rig.Weight : 0f;

    internal static bool InstanceMoved(Player player) =>
        player != null && Rigs.TryGetValue(player.GetInstanceID(), out var rig) && rig.InstanceMoved;
#endif
}
