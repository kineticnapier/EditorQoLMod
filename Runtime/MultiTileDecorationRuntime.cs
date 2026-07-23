using System;
using ADOFAI;
using Kiner.ADOFAIEditorQoL.Core;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    /// <summary>
    /// Keeps generated multi-tile floor decorations at the absolute position stored in
    /// their AddObject event. ADOFAI may rebuild the decoration hierarchy while applying
    /// PositionTrack; writing the intended world position after that rebuild prevents the
    /// generated path from accumulating the real floor's offset.
    /// </summary>
    [DefaultExecutionOrder(11000)]
    public sealed class MultiTileDecorationRuntime : MonoBehaviour, IRuntimeEffect
    {
        private scrDecoration decoration;
        private LevelEvent source;

        internal void Configure(scrDecoration target)
        {
            decoration = target;
            source = target == null ? null : target.sourceLevelEvent;
            enabled = decoration != null && IsManagedFloor(source);
            if (enabled) ApplyStoredPosition();
        }

        private void LateUpdate()
        {
            if (!Main.Enabled || decoration == null)
            {
                enabled = false;
                return;
            }

            if (!ReferenceEquals(source, decoration.sourceLevelEvent))
                source = decoration.sourceLevelEvent;
            if (source == null || !IsManagedFloor(source))
            {
                enabled = false;
                return;
            }

            ApplyStoredPosition();
        }

        private void ApplyStoredPosition()
        {
            object raw;
            if (source == null || source.data == null ||
                !source.data.TryGetValue("position", out raw) || !(raw is Vector2)) return;

            Vector2 stored = (Vector2)raw;
            float tileSize = 1f;
            if (ADOBase.controller != null && Mathf.Abs(ADOBase.controller.tileSize) > 0.000001f)
                tileSize = ADOBase.controller.tileSize;

            Vector3 current = decoration.transform.position;
            Vector3 expected = new Vector3(stored.x * tileSize, stored.y * tileSize, current.z);
            if ((current - expected).sqrMagnitude > 0.00000001f)
                decoration.transform.position = expected;
        }

        internal static bool IsManagedFloor(LevelEvent evnt)
        {
            if (!IsFloorObject(evnt)) return false;
            string[] tags = DecorationMarkerTags.GetTags(evnt);
            for (int i = 0; i < tags.Length; i++)
                if (tags[i].StartsWith(DecorationMarkerTags.ManagedMultiTilePrefix,
                        StringComparison.Ordinal)) return true;

            // Backward compatibility for Tn groups generated before v0.16.0.
            // Requiring both Tn and Tn_index avoids taking over unrelated floor objects.
            for (int i = 0; i < tags.Length; i++)
            {
                int groupNumber;
                if (!TryParseGroup(tags[i], out groupNumber)) continue;
                string prefix = "T" + groupNumber + "_";
                for (int j = 0; j < tags.Length; j++)
                {
                    int index;
                    if (tags[j].StartsWith(prefix, StringComparison.Ordinal) &&
                        int.TryParse(tags[j].Substring(prefix.Length), out index) && index >= 0)
                        return true;
                }
            }
            return false;
        }

        internal static void AttachAllInCurrentScenes()
        {
            scrDecoration[] decorations = Resources.FindObjectsOfTypeAll<scrDecoration>();
            for (int i = 0; i < decorations.Length; i++)
            {
                scrDecoration item = decorations[i];
                if (item == null || item.gameObject == null ||
                    !item.gameObject.scene.IsValid() || !IsManagedFloor(item.sourceLevelEvent))
                    continue;
                MultiTileDecorationRuntime runtime =
                    item.GetComponent<MultiTileDecorationRuntime>();
                if (runtime == null)
                    runtime = item.gameObject.AddComponent<MultiTileDecorationRuntime>();
                runtime.Configure(item);
            }
        }

        private static bool IsFloorObject(LevelEvent evnt)
        {
            if (evnt == null || evnt.eventType != LevelEventType.AddObject || evnt.data == null)
                return false;
            object raw;
            if (!evnt.data.TryGetValue("objectType", out raw) || raw == null) return false;
            if (raw is ObjectDecorationType)
                return (ObjectDecorationType)raw == ObjectDecorationType.Floor;
            return string.Equals(Convert.ToString(raw), "Floor",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseGroup(string tag, out int number)
        {
            number = -1;
            if (string.IsNullOrEmpty(tag) || tag.Length < 2 || tag[0] != 'T') return false;
            return int.TryParse(tag.Substring(1), out number) && number >= 0;
        }

        public void StopAndRestore()
        {
            // Position is the serialized AddObject value itself, so there is no temporary
            // state to restore when the component is removed.
        }
    }
}
