using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ADOFAI;
using HarmonyLib;
using Newtonsoft.Json;

namespace Kiner.ADOFAIEditorQoL.Core
{
    /// <summary>
    /// Isolates members whose binary signatures changed between ADOFAI 2.9.x and
    /// 3.3.x so the mod does not emit stale field or method references.
    /// </summary>
    internal static class GameVersionCompat
    {
        private const BindingFlags StaticMembers =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo SoloTypesField =
            AccessTools.Field(typeof(EditorConstants), "soloTypes");

        private static readonly FieldInfo SettingsTypesField =
            AccessTools.Field(typeof(EditorConstants), "settingsTypes");

        private static readonly MethodInfo EncodeEventMethod =
            AccessTools.Method(typeof(LevelEvent), "Encode", new[] { typeof(bool) });

        private static readonly AccessTools.FieldRef<LevelEvent, Dictionary<string, object>>
            EventDataRef = AccessTools.FieldRefAccess<LevelEvent, Dictionary<string, object>>(
                "data");

        private static readonly FieldInfo CameraInstanceField =
            AccessTools.Field(typeof(scrCamera), "instance");

        private static readonly System.Reflection.PropertyInfo CameraInstanceProperty =
            typeof(scrCamera).GetProperty("instance", StaticMembers);

        internal static bool IsSoloType(LevelEventType type)
        {
            return StaticCollectionContains(SoloTypesField, type);
        }

        internal static bool IsSettingsType(LevelEventType type)
        {
            return StaticCollectionContains(SettingsTypesField, type);
        }

        internal static IDictionary<string, object> GetEventData(this LevelEvent source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            return EventDataRef(source);
        }

        internal static string EncodeEventJson(LevelEvent source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (EncodeEventMethod == null)
            {
                throw new MissingMethodException(typeof(LevelEvent).FullName, "Encode(bool)");
            }

            object encoded = EncodeEventMethod.Invoke(source, new object[] { false });
            string legacyFragment = encoded as string;
            if (legacyFragment != null)
            {
                string trimmed = legacyFragment.Trim();
                return trimmed.StartsWith("{", StringComparison.Ordinal)
                    ? trimmed
                    : "{ " + legacyFragment + " }";
            }

            if (encoded == null)
            {
                throw new InvalidOperationException("LevelEvent.Encode returned null.");
            }

            // ADOFAI 3.3.x returns Dictionary<string, object> instead of a JSON
            // fragment. Its values have already been converted by the game encoder.
            return JsonConvert.SerializeObject(encoded);
        }

        internal static scrCamera GetCamera()
        {
            object value = null;
            if (CameraInstanceProperty != null)
            {
                value = CameraInstanceProperty.GetValue(null, null);
            }
            else if (CameraInstanceField != null)
            {
                value = CameraInstanceField.GetValue(null);
            }
            return value as scrCamera;
        }

        private static bool StaticCollectionContains(FieldInfo field, LevelEventType type)
        {
            if (field == null)
            {
                return false;
            }

            IEnumerable values = field.GetValue(null) as IEnumerable;
            if (values == null)
            {
                return false;
            }

            foreach (object value in values)
            {
                if (value is LevelEventType && (LevelEventType)value == type)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
