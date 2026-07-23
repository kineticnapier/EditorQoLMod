using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ADOFAI;
using Kiner.ADOFAIEditorQoL.Core;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    /// <summary>
    /// Mirrors ADOFAI's native planet transforms into generated multi-tile coordinates.
    /// The previous independent orbit calculation remains only as a compatibility fallback
    /// when a game version does not expose its planet components.
    /// </summary>
    [DefaultExecutionOrder(12000)]
    public sealed class MultiTilePlanetRuntime : MonoBehaviour, IRuntimeEffect
    {
        private sealed class PreviewSession
        {
            internal MultiTilePlanetCommand Command;
            internal scrDecoration[] Path;
            internal scrDecoration Blue;
            internal scrDecoration Red;
            internal Vector3 OriginalBluePosition;
            internal Vector3 OriginalRedPosition;
            internal Quaternion OriginalBlueRotation;
            internal Quaternion OriginalRedRotation;
            internal bool PlaybackStarted;
            internal double PlaybackAudioStart;
            internal double LastAudioTime = double.NaN;
            internal float PlaybackGameTimeStart;
            internal int LastObservedFloor = -1;
        }

        private sealed class InferredGroup
        {
            internal int Number;
            internal int StartFloor = int.MaxValue;
            internal bool HasBlue;
            internal bool HasRed;
            internal readonly HashSet<int> TileIndices = new HashSet<int>();
        }

        private sealed class NativePlanetCandidate
        {
            internal Transform Transform;
            internal string Label;
            internal int Score;
        }

        private scnGame game;
        private readonly List<PreviewSession> sessions = new List<PreviewSession>();
        private int eventFingerprint;
        private int nextRefreshFrame;
        private bool loggedReady;
        private bool loggedNativeMirror;
        private bool loggedLegacyFallback;
        private scrController nativeController;
        private readonly List<Transform> nativePlanets = new List<Transform>();
        private int nextNativePlanetResolveFrame;

        private static Type nativePlanetMemberType;
        private static MemberInfo[] nativePlanetMembers = new MemberInfo[0];

        private static bool audioFieldsResolved;
        private static FieldInfo songField;
        private static FieldInfo song2Field;
        private static AudioSource lastAudioSource;
        private static double lastAudioTime;

        internal void Configure(scnGame source)
        {
            StopAndRestore();
            game = source;
            enabled = true;
            eventFingerprint = 0;
            nextRefreshFrame = Time.frameCount + 2;
            loggedReady = false;
            loggedNativeMirror = false;
            loggedLegacyFallback = false;
            nativeController = null;
            nativePlanets.Clear();
            nextNativePlanetResolveFrame = 0;
            lastAudioSource = null;
            lastAudioTime = 0d;
        }

        private void LateUpdate()
        {
            if (!Main.Enabled) return;
            if (game == null) game = scnGame.instance;
            if (game == null || scrLevelMaker.instance == null ||
                scrLevelMaker.instance.listFloors == null ||
                scrLevelMaker.instance.listFloors.Count == 0) return;

            List<LevelEvent> runtimeEvents = GetRuntimeEvents();
            int fingerprint = GetFingerprint(runtimeEvents);
            if (fingerprint != eventFingerprint ||
                Time.frameCount >= nextRefreshFrame &&
                (sessions.Count == 0 || sessions.Any(x => !IsBound(x))))
            {
                RefreshSessions(runtimeEvents, fingerprint);
            }
            if (sessions.Count == 0) return;

            int currentFloor = GetCurrentFloor();
            IList<scrFloor> floors = scrLevelMaker.instance.listFloors;
            IList<Transform> native = ResolveNativePlanets();
            bool usedNative = false;
            bool usedFallback = false;
            bool audioResolved = false;
            bool hasAudioTime = false;
            double audioTime = 0d;
            for (int i = 0; i < sessions.Count; i++)
            {
                PreviewSession session = sessions[i];
                if (TryApplyNativePlanets(session, floors, currentFloor, native))
                {
                    usedNative = true;
                    continue;
                }

                if (!audioResolved)
                {
                    hasAudioTime = TryGetAudioTime(out audioTime);
                    audioResolved = true;
                }
                UpdateSession(session, floors, runtimeEvents, currentFloor,
                    hasAudioTime, audioTime);
                int start = Mathf.Clamp(session.Command.StartFloor, 0, floors.Count - 1);
                int end = Mathf.Clamp(session.Command.EndFloor, start, floors.Count - 1);
                usedFallback |= currentFloor >= start && currentFloor <= end;
            }

            if (usedNative && !loggedNativeMirror)
            {
                loggedNativeMirror = true;
                Main.Logger.Log("Multi-tile planets are mirroring ADOFAI native transforms: " +
                                string.Join(", ", native.Where(x => x != null)
                                    .Select(x => x.name).ToArray()));
            }
            if (usedFallback && !loggedLegacyFallback)
            {
                loggedLegacyFallback = true;
                Main.Logger.Warning("Native planet transforms were unavailable; using the legacy orbit fallback.");
            }
        }

        private void RefreshSessions(IList<LevelEvent> runtimeEvents, int fingerprint)
        {
            StopAndRestore();
            eventFingerprint = fingerprint;
            nextRefreshFrame = Time.frameCount + 10;

            List<MultiTilePlanetCommand> commands = new List<MultiTilePlanetCommand>();
            foreach (LevelEvent evnt in runtimeEvents)
            {
                MultiTilePlanetCommand command;
                if (MultiTilePlanetEvent.TryDecode(evnt, out command)) commands.Add(command);
            }

            scrDecoration[] decorations = Resources.FindObjectsOfTypeAll<scrDecoration>();
            int sceneHandle = game.gameObject.scene.handle;
            AddInferredLegacyCommands(commands, decorations, sceneHandle);
            if (commands.Count == 0) return;
            foreach (MultiTilePlanetCommand command in commands)
            {
                PreviewSession session = Bind(command, decorations, sceneHandle);
                if (session != null) sessions.Add(session);
            }

            if (!loggedReady && sessions.Count > 0)
            {
                loggedReady = true;
                Main.Logger.Log("Multi-tile planet runtime ready: " + sessions.Count +
                                " preview group(s), no MoveDecorations.");
            }
        }

        private static void AddInferredLegacyCommands(ICollection<MultiTilePlanetCommand> commands,
            IEnumerable<scrDecoration> decorations, int sceneHandle)
        {
            HashSet<string> explicitGroups = new HashSet<string>(
                commands.Select(x => x.PlanetGroup), StringComparer.Ordinal);
            Dictionary<int, InferredGroup> groups = new Dictionary<int, InferredGroup>();

            foreach (scrDecoration decoration in decorations)
            {
                if (!IsSceneDecoration(decoration, sceneHandle)) continue;
                LevelEvent source = decoration.sourceLevelEvent;
                string[] tags = Tags(source);
                foreach (string tag in tags)
                {
                    int number;
                    bool blue;
                    bool red;
                    if (!TryParsePlanetColorTag(tag, out number, out blue, out red)) continue;
                    string groupName = "P" + number.ToString(CultureInfo.InvariantCulture);
                    if (explicitGroups.Contains(groupName)) continue;

                    InferredGroup group;
                    if (!groups.TryGetValue(number, out group))
                    {
                        group = new InferredGroup { Number = number };
                        groups[number] = group;
                    }
                    group.HasBlue |= blue;
                    group.HasRed |= red;
                    group.StartFloor = Math.Min(group.StartFloor, Math.Max(0, source.floor));
                }
            }

            if (groups.Count == 0) return;
            foreach (scrDecoration decoration in decorations)
            {
                if (!IsSceneDecoration(decoration, sceneHandle) ||
                    !IsFloorObject(decoration.sourceLevelEvent)) continue;
                foreach (string tag in Tags(decoration.sourceLevelEvent))
                {
                    int number;
                    int index;
                    if (!TryParseTileItemTag(tag, out number, out index)) continue;
                    InferredGroup group;
                    if (groups.TryGetValue(number, out group)) group.TileIndices.Add(index);
                }
            }

            foreach (InferredGroup group in groups.Values.OrderBy(x => x.Number))
            {
                if (!group.HasBlue || !group.HasRed || group.StartFloor == int.MaxValue) continue;
                int count = 0;
                while (group.TileIndices.Contains(count)) count++;
                if (count < 2) continue;
                string numberText = group.Number.ToString(CultureInfo.InvariantCulture);
                commands.Add(new MultiTilePlanetCommand
                {
                    StartFloor = group.StartFloor,
                    EndFloor = group.StartFloor + count - 1,
                    TileCount = count,
                    TileGroup = "T" + numberText,
                    PlanetGroup = "P" + numberText
                });
            }
        }

        private static bool IsSceneDecoration(scrDecoration decoration, int sceneHandle)
        {
            return decoration != null && decoration.gameObject != null &&
                   decoration.gameObject.scene.IsValid() &&
                   decoration.gameObject.scene.handle == sceneHandle &&
                   decoration.sourceLevelEvent != null;
        }

        private static bool TryParsePlanetColorTag(string tag, out int number,
            out bool blue, out bool red)
        {
            number = -1;
            blue = false;
            red = false;
            if (string.IsNullOrEmpty(tag) || tag[0] != 'P') return false;
            const string blueSuffix = "_BluePlanet";
            const string redSuffix = "_RedPlanet";
            string digits;
            if (tag.EndsWith(blueSuffix, StringComparison.Ordinal))
            {
                blue = true;
                digits = tag.Substring(1, tag.Length - 1 - blueSuffix.Length);
            }
            else if (tag.EndsWith(redSuffix, StringComparison.Ordinal))
            {
                red = true;
                digits = tag.Substring(1, tag.Length - 1 - redSuffix.Length);
            }
            else return false;
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture,
                out number) && number >= 0;
        }

        private static bool TryParseTileItemTag(string tag, out int number, out int index)
        {
            number = -1;
            index = -1;
            if (string.IsNullOrEmpty(tag) || tag[0] != 'T') return false;
            int separator = tag.IndexOf('_');
            if (separator <= 1 || separator >= tag.Length - 1) return false;
            return int.TryParse(tag.Substring(1, separator - 1), NumberStyles.None,
                       CultureInfo.InvariantCulture, out number) && number >= 0 &&
                   int.TryParse(tag.Substring(separator + 1), NumberStyles.None,
                       CultureInfo.InvariantCulture, out index) && index >= 0;
        }

        private static PreviewSession Bind(MultiTilePlanetCommand command,
            IEnumerable<scrDecoration> decorations, int sceneHandle)
        {
            Dictionary<string, scrDecoration> byItemTag = new Dictionary<string, scrDecoration>(
                StringComparer.Ordinal);
            scrDecoration blue = null;
            scrDecoration red = null;
            string blueTag = command.PlanetGroup + "_BluePlanet";
            string redTag = command.PlanetGroup + "_RedPlanet";

            foreach (scrDecoration decoration in decorations)
            {
                if (!IsSceneDecoration(decoration, sceneHandle)) continue;

                string[] tags = Tags(decoration.sourceLevelEvent);
                if (tags.Contains(blueTag)) blue = PreferDecoration(blue, decoration);
                if (tags.Contains(redTag)) red = PreferDecoration(red, decoration);

                if (!IsFloorObject(decoration.sourceLevelEvent)) continue;
                for (int i = 0; i < command.TileCount; i++)
                {
                    string itemTag = command.TileGroup + "_" +
                                     i.ToString(CultureInfo.InvariantCulture);
                    if (tags.Contains(itemTag) && !byItemTag.ContainsKey(itemTag))
                        byItemTag[itemTag] = decoration;
                }
            }

            if (blue == null || red == null) return null;
            scrDecoration[] path = new scrDecoration[command.TileCount];
            for (int i = 0; i < path.Length; i++)
            {
                string itemTag = command.TileGroup + "_" +
                                 i.ToString(CultureInfo.InvariantCulture);
                if (!byItemTag.TryGetValue(itemTag, out path[i]) || path[i] == null)
                    return null;
            }

            return new PreviewSession
            {
                Command = command,
                Path = path,
                Blue = blue,
                Red = red,
                OriginalBluePosition = blue.transform.position,
                OriginalRedPosition = red.transform.position,
                OriginalBlueRotation = blue.transform.rotation,
                OriginalRedRotation = red.transform.rotation
            };
        }

        private static scrDecoration PreferDecoration(scrDecoration current,
            scrDecoration candidate)
        {
            if (current == null) return candidate;
            if (!current.gameObject.activeInHierarchy && candidate.gameObject.activeInHierarchy)
                return candidate;
            return current;
        }

        private static bool IsBound(PreviewSession session)
        {
            if (session == null || session.Blue == null || session.Red == null ||
                session.Path == null || session.Path.Length < 2) return false;
            for (int i = 0; i < session.Path.Length; i++)
                if (session.Path[i] == null) return false;
            return true;
        }

        private IList<Transform> ResolveNativePlanets()
        {
            scrController controller = ADOBase.controller == null
                ? scrController.instance
                : ADOBase.controller;
            bool cacheValid = controller != null && ReferenceEquals(controller, nativeController) &&
                              nativePlanets.Count >= 2 &&
                              nativePlanets.All(x => x != null && x.gameObject.activeInHierarchy);
            if (cacheValid && Time.frameCount < nextNativePlanetResolveFrame)
                return nativePlanets;

            nativeController = controller;
            nativePlanets.Clear();
            if (controller == null)
            {
                nextNativePlanetResolveFrame = Time.frameCount + 15;
                return nativePlanets;
            }

            EnsureNativePlanetMembers(controller.GetType());
            List<NativePlanetCandidate> candidates = new List<NativePlanetCandidate>();
            for (int i = 0; i < nativePlanetMembers.Length; i++)
            {
                object value;
                if (!TryReadMember(nativePlanetMembers[i], controller, out value)) continue;
                AddNativePlanetValue(candidates, value, nativePlanetMembers[i].Name);
            }

            if (candidates.Count < 2)
            {
                Component[] children = controller.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    Component component = children[i];
                    if (component == null || component is scrDecoration) continue;
                    string typeName = component.GetType().Name;
                    if (typeName.IndexOf("Planet", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    AddNativePlanetTransform(candidates, component.transform,
                        typeName + "#" + i, component.GetType());
                }
            }

            HashSet<int> seen = new HashSet<int>();
            foreach (NativePlanetCandidate candidate in candidates
                         .OrderBy(x => x.Score).ThenBy(x => x.Label, StringComparer.Ordinal))
            {
                Transform transform = candidate.Transform;
                if (transform == null || transform.gameObject == null ||
                    !transform.gameObject.activeInHierarchy ||
                    !transform.gameObject.scene.IsValid() ||
                    transform.GetComponent<scrDecoration>() != null ||
                    !seen.Add(transform.GetInstanceID())) continue;
                nativePlanets.Add(transform);
                if (nativePlanets.Count >= 4) break;
            }

            nextNativePlanetResolveFrame = Time.frameCount +
                                           (nativePlanets.Count >= 2 ? 120 : 15);
            return nativePlanets;
        }

        private static void EnsureNativePlanetMembers(Type controllerType)
        {
            if (controllerType == null || controllerType == nativePlanetMemberType) return;
            nativePlanetMemberType = controllerType;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic;
            List<MemberInfo> members = new List<MemberInfo>();
            foreach (FieldInfo field in controllerType.GetFields(flags))
                if (IsPlanetMember(field.Name, field.FieldType)) members.Add(field);
            foreach (System.Reflection.PropertyInfo property in controllerType.GetProperties(flags))
                if (property.GetIndexParameters().Length == 0 && property.CanRead &&
                    IsPlanetMember(property.Name, property.PropertyType)) members.Add(property);
            nativePlanetMembers = members.ToArray();
        }

        private static bool IsPlanetMember(string name, Type memberType)
        {
            return (name ?? string.Empty).IndexOf("planet",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   TypeMentionsPlanet(memberType);
        }

        private static bool TypeMentionsPlanet(Type type)
        {
            if (type == null) return false;
            if (type.Name.IndexOf("planet", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (type.IsArray) return TypeMentionsPlanet(type.GetElementType());
            if (!type.IsGenericType) return false;
            Type[] arguments = type.GetGenericArguments();
            for (int i = 0; i < arguments.Length; i++)
                if (TypeMentionsPlanet(arguments[i])) return true;
            return false;
        }

        private static bool TryReadMember(MemberInfo member, object owner, out object value)
        {
            value = null;
            try
            {
                FieldInfo field = member as FieldInfo;
                if (field != null)
                {
                    value = field.GetValue(owner);
                    return value != null;
                }
                System.Reflection.PropertyInfo property = member as System.Reflection.PropertyInfo;
                if (property != null)
                {
                    value = property.GetValue(owner, null);
                    return value != null;
                }
            }
            catch
            {
                // Optional game internals can disappear or throw between scene changes.
            }
            return false;
        }

        private static void AddNativePlanetValue(ICollection<NativePlanetCandidate> destination,
            object value, string label)
        {
            if (value == null) return;
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string) && !(value is Component) &&
                !(value is GameObject) && !(value is Transform))
            {
                int index = 0;
                try
                {
                    foreach (object item in enumerable)
                    {
                        AddNativePlanetObject(destination, item, label + "[" + index + "]");
                        if (++index >= 8) break;
                    }
                }
                catch
                {
                    // A collection can change while the controller switches planet count.
                }
                return;
            }
            AddNativePlanetObject(destination, value, label);
        }

        private static void AddNativePlanetObject(ICollection<NativePlanetCandidate> destination,
            object value, string label)
        {
            if (value == null) return;
            Transform transform = value as Transform;
            Component component = value as Component;
            GameObject gameObject = value as GameObject;
            Type sourceType = value.GetType();
            if (transform == null && component != null) transform = component.transform;
            if (transform == null && gameObject != null) transform = gameObject.transform;
            AddNativePlanetTransform(destination, transform, label, sourceType);
        }

        private static void AddNativePlanetTransform(
            ICollection<NativePlanetCandidate> destination, Transform transform,
            string label, Type sourceType)
        {
            if (transform == null || transform.GetComponent<scrDecoration>() != null) return;
            string normalized = (label ?? string.Empty).ToLowerInvariant();
            int score = 200;
            if (normalized.Contains("planet1") || normalized.Contains("blue")) score -= 120;
            else if (normalized.Contains("planet2") || normalized.Contains("red")) score -= 100;
            else if (normalized.Contains("planet3")) score -= 80;
            if (sourceType != null && sourceType.Name.IndexOf("scrPlanet",
                    StringComparison.OrdinalIgnoreCase) >= 0) score -= 60;
            if (normalized.Contains("particle") || normalized.Contains("sound") ||
                normalized.Contains("color") || normalized.Contains("sprite")) score += 500;
            if (!transform.gameObject.activeInHierarchy) score += 300;
            destination.Add(new NativePlanetCandidate
            {
                Transform = transform,
                Label = label ?? string.Empty,
                Score = score
            });
        }

        private static bool TryApplyNativePlanets(PreviewSession session,
            IList<scrFloor> floors, int currentFloor, IList<Transform> native)
        {
            if (!IsBound(session) || native == null || native.Count < 2 || floors.Count == 0)
                return false;
            int start = Mathf.Clamp(session.Command.StartFloor, 0, floors.Count - 1);
            int end = Mathf.Clamp(session.Command.EndFloor, start, floors.Count - 1);
            if (currentFloor < start || currentFloor > end) return false;

            int sourceFloor = Mathf.Clamp(currentFloor, start, end);
            int sourceIndex = sourceFloor - session.Command.StartFloor;
            if (sourceIndex < 0 || sourceIndex >= session.Path.Length) return false;

            Vector2 nativeCenter = FloorPosition(floors[sourceFloor]);
            Vector2 previewCenter = Position(session.Path[sourceIndex]);
            Vector2 nativeBasis;
            Vector2 previewBasis;
            if (!TryResolveMappingBasis(session, floors, sourceFloor, end,
                    nativeCenter, previewCenter, out nativeBasis, out previewBasis))
                return false;

            float nativeLength = nativeBasis.magnitude;
            float previewLength = previewBasis.magnitude;
            if (nativeLength < 0.000001f || previewLength < 0.000001f) return false;
            float rotationDelta = DirectionAngle(previewBasis) - DirectionAngle(nativeBasis);
            float scale = previewLength / nativeLength;

            Transform stationary = null;
            Transform moving = null;
            float nearest = float.MaxValue;
            float farthest = float.MinValue;
            for (int i = 0; i < native.Count; i++)
            {
                Transform candidate = native[i];
                if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
                Vector2 candidatePosition = TransformPosition(candidate);
                float distance = (candidatePosition - nativeCenter).sqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    stationary = candidate;
                }
            }
            for (int i = 0; i < native.Count; i++)
            {
                Transform candidate = native[i];
                if (candidate == null || candidate == stationary ||
                    !candidate.gameObject.activeInHierarchy) continue;
                float distance = (TransformPosition(candidate) - nativeCenter).sqrMagnitude;
                if (distance > farthest)
                {
                    farthest = distance;
                    moving = candidate;
                }
            }
            if (stationary == null || moving == null) return false;

            Vector2 stationaryPosition = MapNativePosition(stationary, nativeCenter,
                previewCenter, rotationDelta, scale);
            Vector2 movingPosition = MapNativePosition(moving, nativeCenter,
                previewCenter, rotationDelta, scale);
            float stationaryRotation = stationary.eulerAngles.z + rotationDelta;
            float movingRotation = moving.eulerAngles.z + rotationDelta;
            bool moveRed = CountPriorSegments(session, floors, sourceFloor) % 2 == 0;
            if (moveRed)
            {
                SetPlanet(session.Blue, stationaryPosition, stationaryRotation);
                SetPlanet(session.Red, movingPosition, movingRotation);
            }
            else
            {
                SetPlanet(session.Red, stationaryPosition, stationaryRotation);
                SetPlanet(session.Blue, movingPosition, movingRotation);
            }

            session.PlaybackStarted = false;
            session.LastObservedFloor = currentFloor;
            return true;
        }

        private static bool TryResolveMappingBasis(PreviewSession session,
            IList<scrFloor> floors, int sourceFloor, int end, Vector2 nativeCenter,
            Vector2 previewCenter, out Vector2 nativeBasis, out Vector2 previewBasis)
        {
            nativeBasis = Vector2.zero;
            previewBasis = Vector2.zero;
            for (int floor = sourceFloor + 1; floor <= end; floor++)
            {
                int index = floor - session.Command.StartFloor;
                if (index < 0 || index >= session.Path.Length) break;
                Vector2 candidateNative = FloorPosition(floors[floor]) - nativeCenter;
                Vector2 candidatePreview = Position(session.Path[index]) - previewCenter;
                if (candidateNative.sqrMagnitude < 0.000001f ||
                    candidatePreview.sqrMagnitude < 0.000001f) continue;
                nativeBasis = candidateNative;
                previewBasis = candidatePreview;
                return true;
            }

            for (int floor = sourceFloor - 1; floor >= session.Command.StartFloor; floor--)
            {
                int index = floor - session.Command.StartFloor;
                if (index < 0 || index >= session.Path.Length) continue;
                Vector2 candidateNative = FloorPosition(floors[floor]) - nativeCenter;
                Vector2 candidatePreview = Position(session.Path[index]) - previewCenter;
                if (candidateNative.sqrMagnitude < 0.000001f ||
                    candidatePreview.sqrMagnitude < 0.000001f) continue;
                nativeBasis = candidateNative;
                previewBasis = candidatePreview;
                return true;
            }
            return false;
        }

        private static Vector2 MapNativePosition(Transform native, Vector2 nativeCenter,
            Vector2 previewCenter, float rotationDelta, float scale)
        {
            Vector2 offset = TransformPosition(native) - nativeCenter;
            return previewCenter + Rotate(offset, rotationDelta) * scale;
        }

        private static Vector2 FloorPosition(scrFloor floor)
        {
            Vector3 value = floor.transform.position;
            return new Vector2(value.x, value.y);
        }

        private static Vector2 TransformPosition(Transform transform)
        {
            Vector3 value = transform.position;
            return new Vector2(value.x, value.y);
        }

        private static void UpdateSession(PreviewSession session, IList<scrFloor> floors,
            IList<LevelEvent> events, int currentFloor, bool hasAudioTime, double audioTime)
        {
            if (!IsBound(session)) return;
            MultiTilePlanetCommand command = session.Command;
            int start = Mathf.Clamp(command.StartFloor, 0, floors.Count - 1);
            int end = Mathf.Clamp(command.EndFloor, start, floors.Count - 1);

            if (currentFloor < start)
            {
                ApplyBeforeStart(session);
                session.PlaybackStarted = false;
                session.LastAudioTime = hasAudioTime ? audioTime : double.NaN;
                session.LastObservedFloor = currentFloor;
                return;
            }

            int observedFloor = Mathf.Clamp(currentFloor, start, end);
            bool audioWentBackward = hasAudioTime && !double.IsNaN(session.LastAudioTime) &&
                                     audioTime + 0.05d < session.LastAudioTime;
            bool floorWentBackward = session.LastObservedFloor >= 0 &&
                                     currentFloor < session.LastObservedFloor;
            if (!session.PlaybackStarted || audioWentBackward)
            {
                // On retry, the audio source can jump to zero one frame before currFloor
                // leaves the previous attempt's final floor. Anchoring to that stale floor
                // would immediately pin the preview at its end forever. If the floor has
                // not rewound with the audio yet, restart from the command's first floor.
                int anchorFloor = audioWentBackward && !floorWentBackward
                    ? start
                    : observedFloor;
                double observedOffset = Math.Max(0d,
                    floors[anchorFloor].entryTime - floors[start].entryTime);
                session.PlaybackStarted = true;
                session.PlaybackAudioStart = hasAudioTime
                    ? audioTime - observedOffset
                    : 0d;
                session.PlaybackGameTimeStart = Time.time - (float)observedOffset;
            }

            // Use one continuous playback clock for the whole preview. currFloor is only
            // used to anchor/restart it; retraces and justThisTile PositionTrack events can
            // no longer leave the preview stuck on its first segment.
            double elapsedFromStart = hasAudioTime
                ? audioTime - session.PlaybackAudioStart
                : Time.time - session.PlaybackGameTimeStart;
            elapsedFromStart = Math.Max(0d, elapsedFromStart);

            int sourceFloor = start;
            int nextFloor = FindNextFloor(session, floors, sourceFloor, end);
            while (nextFloor > sourceFloor)
            {
                double nextOffset = floors[nextFloor].entryTime - floors[start].entryTime;
                if (elapsedFromStart + 0.000001d < nextOffset) break;
                sourceFloor = nextFloor;
                nextFloor = FindNextFloor(session, floors, sourceFloor, end);
            }

            if (sourceFloor >= end || nextFloor <= sourceFloor)
            {
                int lastStart = FindLastSegmentStart(session, floors, start, end);
                if (lastStart >= start)
                {
                    int lastNext = FindNextFloor(session, floors, lastStart, end);
                    ApplySegment(session, floors, events, lastStart, lastNext, 1f);
                }
                session.LastAudioTime = hasAudioTime ? audioTime : double.NaN;
                session.LastObservedFloor = currentFloor;
                return;
            }

            double segmentStart = floors[sourceFloor].entryTime - floors[start].entryTime;
            double segmentEnd = floors[nextFloor].entryTime - floors[start].entryTime;
            double activeDuration = segmentEnd - segmentStart;
            float progress = activeDuration <= 0.000001d
                ? 1f
                : Mathf.Clamp01((float)((elapsedFromStart - segmentStart) / activeDuration));
            session.LastAudioTime = hasAudioTime ? audioTime : double.NaN;
            session.LastObservedFloor = currentFloor;

            ApplySegment(session, floors, events, sourceFloor, nextFloor, progress);
        }

        private static void ApplyBeforeStart(PreviewSession session)
        {
            Vector2 first = Position(session.Path[0]);
            Vector2 second = Position(session.Path[1]);
            Vector2 incoming = first - (second - first);
            SetPlanet(session.Blue, first, 0f);
            SetPlanet(session.Red, incoming, DirectionAngle(incoming - first) - 180f);
        }

        private static void ApplySegment(PreviewSession session, IList<scrFloor> floors,
            IList<LevelEvent> events, int sourceFloor, int nextFloor, float progress)
        {
            int sourceIndex = sourceFloor - session.Command.StartFloor;
            int nextIndex = nextFloor - session.Command.StartFloor;
            if (sourceIndex < 0 || sourceIndex >= session.Path.Length ||
                nextIndex < 0 || nextIndex >= session.Path.Length) return;

            Vector2 current = Position(session.Path[sourceIndex]);
            Vector2 next = Position(session.Path[nextIndex]);
            Vector2 endVector = next - current;
            if (endVector.sqrMagnitude < 0.000001f) return;

            Vector2 startVector = FindIncomingVector(session, sourceIndex, current, endVector);
            float startAngle = DirectionAngle(startVector);
            float endAngle = DirectionAngle(endVector);
            LevelEvent pathEvent = session.Path[sourceIndex].sourceLevelEvent;
            float trackAngle = MultiTileOperations.ReadManagedRhythmAngle(pathEvent,
                ReadFloat(pathEvent, "trackAngle", 180f));
            if (trackAngle <= 0.0001f) trackAngle = 360f;
            float extraAngle = SumEventValue(events, sourceFloor, LevelEventType.Pause,
                                   "duration") * 180f +
                               SumEventValue(events, sourceFloor, LevelEventType.Hold,
                                   "duration") * 360f;
            float desiredTravel = Mathf.Max(0f, trackAngle) + Mathf.Max(0f, extraAngle);
            float signedDesired = ResolveSignedTravel(startAngle, endAngle, trackAngle,
                desiredTravel, floors[sourceFloor].isCCW);
            float signedTravel = MatchEndDirection(startAngle, endAngle, signedDesired);

            float angle = startAngle + signedTravel * progress;
            float radius = Mathf.Lerp(startVector.magnitude, endVector.magnitude, progress);
            Vector2 moving = current + Rotate(Vector2.right * radius, angle);
            bool moveRed = CountPriorSegments(session, floors, sourceFloor) % 2 == 0;
            if (moveRed)
            {
                SetPlanet(session.Blue, current, 0f);
                SetPlanet(session.Red, moving, angle - 180f);
            }
            else
            {
                SetPlanet(session.Red, current, 0f);
                SetPlanet(session.Blue, moving, angle - 180f);
            }
        }

        private static Vector2 FindIncomingVector(PreviewSession session, int sourceIndex,
            Vector2 current, Vector2 fallbackEnd)
        {
            for (int i = sourceIndex - 1; i >= 0; i--)
            {
                Vector2 incoming = Position(session.Path[i]) - current;
                if (incoming.sqrMagnitude >= 0.000001f) return incoming;
            }
            return -fallbackEnd;
        }

        private static int FindNextFloor(PreviewSession session, IList<scrFloor> floors,
            int sourceFloor, int end)
        {
            int sourceIndex = sourceFloor - session.Command.StartFloor;
            if (sourceIndex < 0 || sourceIndex >= session.Path.Length) return -1;
            Vector2 current = Position(session.Path[sourceIndex]);
            for (int floor = sourceFloor + 1; floor <= end; floor++)
            {
                int index = floor - session.Command.StartFloor;
                if (index < 0 || index >= session.Path.Length) break;
                if (floors[floor].entryTime <= floors[sourceFloor].entryTime + 0.000001d)
                    continue;
                if ((Position(session.Path[index]) - current).sqrMagnitude < 0.000001f)
                    continue;
                return floor;
            }
            return -1;
        }

        private static int FindLastSegmentStart(PreviewSession session, IList<scrFloor> floors,
            int start, int end)
        {
            for (int floor = end - 1; floor >= start; floor--)
                if (FindNextFloor(session, floors, floor, end) > floor) return floor;
            return -1;
        }

        private static int CountPriorSegments(PreviewSession session, IList<scrFloor> floors,
            int sourceFloor)
        {
            int result = 0;
            int start = session.Command.StartFloor;
            int end = Math.Min(sourceFloor, session.Command.EndFloor);
            for (int floor = start; floor < end; floor++)
            {
                if (floors[floor].midSpin) continue;
                if (FindNextFloor(session, floors, floor, session.Command.EndFloor) > floor)
                    result++;
            }
            return result;
        }

        private static float MatchEndDirection(float startAngle, float endAngle,
            float desiredSignedTravel)
        {
            const float epsilon = 0.0001f;
            if (desiredSignedTravel >= 0f)
            {
                float direct = Mathf.Repeat(endAngle - startAngle, 360f);
                if (direct < epsilon && desiredSignedTravel > epsilon) direct = 360f;
                int turns = Math.Max(0, Mathf.FloorToInt(
                    (desiredSignedTravel - direct) / 360f + 0.5f));
                return direct + turns * 360f;
            }

            float clockwise = -Mathf.Repeat(startAngle - endAngle, 360f);
            if (Mathf.Abs(clockwise) < epsilon && desiredSignedTravel < -epsilon)
                clockwise = -360f;
            int clockwiseTurns = Math.Max(0, Mathf.FloorToInt(
                ((-desiredSignedTravel) - (-clockwise)) / 360f + 0.5f));
            return clockwise - clockwiseTurns * 360f;
        }

        private static float ResolveSignedTravel(float startAngle, float endAngle,
            float trackAngle, float totalTravel, bool sourceIsCcw)
        {
            const float epsilon = 0.01f;
            float positiveArc = Mathf.Repeat(endAngle - startAngle, 360f);
            if (positiveArc < epsilon) positiveArc = 360f;
            float negativeArc = 360f - positiveArc;
            if (negativeArc < epsilon) negativeArc = 360f;

            // For non-180-degree turns the decoration geometry tells us unambiguously
            // which side is inward. Use isCCW only for the genuinely ambiguous semicircle.
            float positiveError = Mathf.Abs(positiveArc - trackAngle);
            float negativeError = Mathf.Abs(negativeArc - trackAngle);
            if (positiveError + epsilon < negativeError) return totalTravel;
            if (negativeError + epsilon < positiveError) return -totalTravel;
            return sourceIsCcw ? totalTravel : -totalTravel;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sine = Mathf.Sin(radians);
            float cosine = Mathf.Cos(radians);
            return new Vector2(value.x * cosine - value.y * sine,
                value.x * sine + value.y * cosine);
        }

        private static float DirectionAngle(Vector2 value)
        {
            return Mathf.Atan2(value.y, value.x) * Mathf.Rad2Deg;
        }

        private static Vector2 Position(scrDecoration decoration)
        {
            Vector3 value = decoration.transform.position;
            return new Vector2(value.x, value.y);
        }

        private static void SetPlanet(scrDecoration decoration, Vector2 position, float rotation)
        {
            if (decoration == null) return;
            Vector3 current = decoration.transform.position;
            decoration.transform.position = new Vector3(position.x, position.y, current.z);
            decoration.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
        }

        private static float SumEventValue(IEnumerable<LevelEvent> events, int floor,
            LevelEventType eventType, string key)
        {
            float result = 0f;
            foreach (LevelEvent evnt in events)
            {
                if (evnt == null || evnt.floor != floor || evnt.eventType != eventType) continue;
                result += Mathf.Max(0f, ReadFloat(evnt, key, 0f));
            }
            return result;
        }

        private static float ReadFloat(LevelEvent evnt, string key, float fallback)
        {
            if (evnt == null) return fallback;
            object raw;
            if (!evnt.data.TryGetValue(key, out raw) || raw == null) return fallback;
            try { return Convert.ToSingle(raw, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static bool IsFloorObject(LevelEvent evnt)
        {
            if (evnt == null || evnt.eventType != LevelEventType.AddObject) return false;
            object raw;
            if (!evnt.data.TryGetValue("objectType", out raw) || raw == null) return false;
            if (raw is ObjectDecorationType)
                return (ObjectDecorationType)raw == ObjectDecorationType.Floor;
            return string.Equals(Convert.ToString(raw), "Floor",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string[] Tags(LevelEvent evnt)
        {
            if (evnt == null) return new string[0];
            object raw;
            if (!evnt.data.TryGetValue("tag", out raw) || raw == null) return new string[0];
            return Convert.ToString(raw).Split(new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
        }

        private List<LevelEvent> GetRuntimeEvents()
        {
            List<LevelEvent> result = new List<LevelEvent>();
            HashSet<LevelEvent> seen = new HashSet<LevelEvent>();
            AddEvents(game == null ? null : game.events, result, seen);
            AddEvents(ADOBase.customLevel == null ? null : ADOBase.customLevel.events,
                result, seen);
            return result;
        }

        private static void AddEvents(IEnumerable<LevelEvent> source,
            ICollection<LevelEvent> destination, ISet<LevelEvent> seen)
        {
            if (source == null) return;
            foreach (LevelEvent evnt in source)
                if (evnt != null && seen.Add(evnt)) destination.Add(evnt);
        }

        private static int GetFingerprint(IEnumerable<LevelEvent> events)
        {
            unchecked
            {
                int hash = 17;
                foreach (LevelEvent evnt in events)
                {
                    MultiTilePlanetCommand command;
                    if (!MultiTilePlanetEvent.TryDecode(evnt, out command)) continue;
                    hash = hash * 31 + command.StartFloor;
                    hash = hash * 31 + command.EndFloor;
                    hash = hash * 31 + command.TileCount;
                    hash = hash * 31 + command.TileGroup.GetHashCode();
                    hash = hash * 31 + command.PlanetGroup.GetHashCode();
                }
                return hash;
            }
        }

        private static int GetCurrentFloor()
        {
            scrController controller = ADOBase.controller == null
                ? scrController.instance
                : ADOBase.controller;
            if (controller != null && controller.currFloor != null)
                return Math.Max(0, controller.currFloor.seqID);
            return controller == null ? 0 : Math.Max(0, controller.currentFloorID);
        }

        private static bool TryGetAudioTime(out double time)
        {
            time = 0d;
            ResolveAudioFields();
            scrConductor conductor = scrConductor.instance;
            if (conductor == null) return false;
            AudioSource first = ReadAudioSource(songField, conductor);
            AudioSource second = ReadAudioSource(song2Field, conductor);
            AudioSource source = SelectAudioSource(first, second);
            if (source == null || source.clip == null || source.clip.frequency <= 0) return false;
            try
            {
                time = (double)source.timeSamples / source.clip.frequency;
                lastAudioSource = source;
                lastAudioTime = time;
                return true;
            }
            catch
            {
                if (lastAudioSource == null || lastAudioSource.clip == null) return false;
                time = lastAudioTime;
                return true;
            }
        }

        private static void ResolveAudioFields()
        {
            if (audioFieldsResolved) return;
            audioFieldsResolved = true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic;
            songField = typeof(scrConductor).GetField("song", flags);
            song2Field = typeof(scrConductor).GetField("song2", flags);
        }

        private static AudioSource ReadAudioSource(FieldInfo field, scrConductor conductor)
        {
            if (field == null || conductor == null) return null;
            try { return field.GetValue(conductor) as AudioSource; }
            catch { return null; }
        }

        private static AudioSource SelectAudioSource(AudioSource first, AudioSource second)
        {
            if (lastAudioSource != null && lastAudioSource.clip != null &&
                lastAudioSource.isPlaying) return lastAudioSource;

            bool firstPlaying = first != null && first.clip != null && first.isPlaying;
            bool secondPlaying = second != null && second.clip != null && second.isPlaying;
            if (firstPlaying && !secondPlaying) return first;
            if (secondPlaying && !firstPlaying) return second;
            if (firstPlaying && secondPlaying)
                return first.timeSamples >= second.timeSamples ? first : second;
            if (first != null && first.clip != null) return first;
            if (second != null && second.clip != null) return second;
            return null;
        }

        public void StopAndRestore()
        {
            for (int i = 0; i < sessions.Count; i++)
            {
                PreviewSession session = sessions[i];
                if (session.Blue != null)
                {
                    session.Blue.transform.position = session.OriginalBluePosition;
                    session.Blue.transform.rotation = session.OriginalBlueRotation;
                }
                if (session.Red != null)
                {
                    session.Red.transform.position = session.OriginalRedPosition;
                    session.Red.transform.rotation = session.OriginalRedRotation;
                }
            }
            sessions.Clear();
        }
    }
}
