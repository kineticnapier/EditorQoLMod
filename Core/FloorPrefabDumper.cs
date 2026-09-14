using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class FloorPrefabDumper
    {
        internal static string Dump()
        {
            scrLevelMaker levelMaker = scrLevelMaker.instance;
            if (levelMaker == null)
            {
                return "scrLevelMaker.instance == null";
            }

            GameObject prefab = levelMaker.meshFloor;
            if (prefab == null)
            {
                return "scrLevelMaker.meshFloor == null";
            }

            StringBuilder sb = new StringBuilder(32768);
            sb.AppendLine("=== ADOFAI meshFloor runtime dump ===");
            sb.AppendLine("EditorQoL: " + ModVersion.Current);
            sb.AppendLine("Unity: " + Application.unityVersion);
            sb.AppendLine("Application.isPlaying: " + Application.isPlaying);
            sb.AppendLine();

            DumpGameObject("PREFAB meshFloor", prefab, sb);

            sb.AppendLine();
            sb.AppendLine("============================================================");
            sb.AppendLine();

            if (levelMaker.listFloors != null && levelMaker.listFloors.Count > 0 && levelMaker.listFloors[0] != null)
            {
                DumpGameObject("LIVE listFloors[0]", levelMaker.listFloors[0].gameObject, sb);
            }
            else
            {
                sb.AppendLine("=== LIVE listFloors[0] ===");
                sb.AppendLine("No generated floor is available.");
            }

            string path = Path.Combine(Main.ModPath, "meshFloor_dump.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

            if (Main.Logger != null)
            {
                Main.Logger.Log("meshFloor runtime dump written: " + path);
            }

            return "meshFloor dump保存: " + path;
        }

        private static void DumpGameObject(string title, GameObject root, StringBuilder sb)
        {
            sb.AppendLine("=== " + title + " ===");
            sb.AppendLine("Name: " + root.name);
            sb.AppendLine("InstanceID: " + root.GetInstanceID());
            sb.AppendLine("activeSelf: " + root.activeSelf);
            sb.AppendLine("activeInHierarchy: " + root.activeInHierarchy);
            sb.AppendLine("layer: " + root.layer);
            sb.AppendLine("tag: " + SafeTag(root));
            sb.AppendLine();

            sb.AppendLine("--- Hierarchy / Components ---");
            DumpTransform(root.transform, root.transform, sb, 0);

            sb.AppendLine();
            sb.AppendLine("--- Component object references ---");
            DumpComponentReferences(root.transform, root.transform, sb);
        }

        private static void DumpTransform(Transform current, Transform root, StringBuilder sb, int depth)
        {
            string indent = new string(' ', depth * 2);
            string path = GetRelativePath(current, root);
            GameObject go = current.gameObject;

            sb.Append(indent);
            sb.Append('[').Append(path).Append(']');
            sb.Append(" id=").Append(go.GetInstanceID());
            sb.Append(" activeSelf=").Append(go.activeSelf);
            sb.Append(" layer=").Append(go.layer);
            sb.Append(" localPos=").Append(FormatVector3(current.localPosition));
            sb.Append(" localRot=").Append(FormatVector3(current.localEulerAngles));
            sb.Append(" localScale=").Append(FormatVector3(current.localScale));
            sb.AppendLine();

            Component[] components = current.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                sb.Append(indent).Append("  - ");
                if (component == null)
                {
                    sb.AppendLine("<Missing Component>");
                }
                else
                {
                    sb.Append(component.GetType().FullName);
                    sb.Append(" id=").Append(component.GetInstanceID());
                    if (component is Behaviour behaviour)
                    {
                        sb.Append(" enabled=").Append(behaviour.enabled);
                    }
                    sb.AppendLine();
                }
            }

            for (int i = 0; i < current.childCount; i++)
            {
                DumpTransform(current.GetChild(i), root, sb, depth + 1);
            }
        }

        private static void DumpComponentReferences(Transform current, Transform root, StringBuilder sb)
        {
            Component[] components = current.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;

                sb.AppendLine("[" + GetRelativePath(current, root) + "] " + component.GetType().FullName);
                DumpObjectFields(component, root, sb);
            }

            for (int i = 0; i < current.childCount; i++)
            {
                DumpComponentReferences(current.GetChild(i), root, sb);
            }
        }

        private static void DumpObjectFields(Component component, Transform root, StringBuilder sb)
        {
            Type type = component.GetType();
            bool wroteAny = false;

            while (type != null && type != typeof(object))
            {
                FieldInfo[] fields;
                try
                {
                    fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("  <fields error on " + type.FullName + ": " + ex.GetType().Name + ">");
                    break;
                }

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic) continue;

                    Type fieldType = field.FieldType;
                    bool directObject = typeof(UnityEngine.Object).IsAssignableFrom(fieldType);
                    bool objectArray = fieldType.IsArray &&
                                       typeof(UnityEngine.Object).IsAssignableFrom(fieldType.GetElementType());
                    bool enumerable = !directObject && !objectArray &&
                                      fieldType != typeof(string) &&
                                      typeof(IEnumerable).IsAssignableFrom(fieldType);

                    if (!directObject && !objectArray && !enumerable) continue;

                    object value;
                    try
                    {
                        value = field.GetValue(component);
                    }
                    catch (Exception ex)
                    {
                        sb.Append("  ").Append(type.Name).Append('.').Append(field.Name)
                            .Append(" = <ERROR: ").Append(ex.GetType().Name).AppendLine(">");
                        wroteAny = true;
                        continue;
                    }

                    if (directObject)
                    {
                        sb.Append("  ").Append(type.Name).Append('.').Append(field.Name)
                            .Append(" : ").Append(fieldType.FullName).Append(" = ")
                            .AppendLine(DescribeUnityObject(value as UnityEngine.Object, root));
                        wroteAny = true;
                    }
                    else if (objectArray)
                    {
                        Array array = value as Array;
                        sb.Append("  ").Append(type.Name).Append('.').Append(field.Name)
                            .Append(" : ").Append(fieldType.FullName).Append(" = ");
                        if (array == null)
                        {
                            sb.AppendLine("null");
                        }
                        else
                        {
                            sb.AppendLine("length=" + array.Length);
                            for (int j = 0; j < array.Length; j++)
                            {
                                sb.Append("    [").Append(j).Append("] ")
                                    .AppendLine(DescribeUnityObject(array.GetValue(j) as UnityEngine.Object, root));
                            }
                        }
                        wroteAny = true;
                    }
                    else if (value is IEnumerable sequence)
                    {
                        int index = 0;
                        bool containsUnityObject = false;
                        StringBuilder items = new StringBuilder();
                        try
                        {
                            foreach (object item in sequence)
                            {
                                if (!(item is UnityEngine.Object unityObject))
                                {
                                    index++;
                                    if (index >= 64) break;
                                    continue;
                                }

                                containsUnityObject = true;
                                items.Append("    [").Append(index).Append("] ")
                                    .AppendLine(DescribeUnityObject(unityObject, root));
                                index++;
                                if (index >= 64)
                                {
                                    items.AppendLine("    ... truncated at 64 entries");
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            containsUnityObject = false;
                        }

                        if (containsUnityObject)
                        {
                            sb.Append("  ").Append(type.Name).Append('.').Append(field.Name)
                                .Append(" : ").Append(fieldType.FullName).AppendLine();
                            sb.Append(items);
                            wroteAny = true;
                        }
                    }
                }

                type = type.BaseType;
            }

            if (!wroteAny)
            {
                sb.AppendLine("  (no UnityEngine.Object references)");
            }
        }

        private static string DescribeUnityObject(UnityEngine.Object value, Transform root)
        {
            if (value == null) return "null";

            Component component = value as Component;
            if (component != null)
            {
                return GetRelativePath(component.transform, root) +
                       " [" + component.GetType().FullName + ", id=" + component.GetInstanceID() + "]";
            }

            GameObject go = value as GameObject;
            if (go != null)
            {
                return GetRelativePath(go.transform, root) + " [GameObject, id=" + go.GetInstanceID() + "]";
            }

            return value.name + " [" + value.GetType().FullName + ", id=" + value.GetInstanceID() + "]";
        }

        private static string GetRelativePath(Transform current, Transform root)
        {
            if (current == null) return "<null>";
            if (current == root) return root.name;

            string path = Segment(current);
            Transform parent = current.parent;
            while (parent != null && parent != root)
            {
                path = Segment(parent) + "/" + path;
                parent = parent.parent;
            }

            if (parent == root)
            {
                path = root.name + "/" + path;
            }
            else
            {
                path = "<external>/" + path;
            }

            return path;
        }

        private static string Segment(Transform transform)
        {
            return transform.name + "#" + transform.GetSiblingIndex();
        }

        private static string FormatVector3(Vector3 value)
        {
            return "(" + value.x.ToString("0.###") + "," +
                         value.y.ToString("0.###") + "," +
                         value.z.ToString("0.###") + ")";
        }

        private static string SafeTag(GameObject gameObject)
        {
            try
            {
                return gameObject.tag;
            }
            catch
            {
                return "<unavailable>";
            }
        }
    }
}
