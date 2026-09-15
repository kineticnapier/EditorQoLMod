using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    internal sealed class AssetProbeResult
    {
        internal string TargetName;
        internal string ReportPath;
        internal int GameObjectCount;
        internal int ComponentCount;
        internal string RootSummary;
    }

    internal static class AssetProbe
    {
        private const int MaxNodes = 500;
        private const int MaxDepth = 10;

        internal static AssetProbeResult ProbeLoadedObject(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName))
                throw new ArgumentException("対象名を入力してください。");

            GameObject target = FindLoadedObject(targetName.Trim());
            if (target == null)
                throw new InvalidOperationException("ロード済みGameObjectが見つかりません: " + targetName);

            return Probe(target, "name-" + targetName.Trim());
        }

        internal static AssetProbeResult ProbeFirstEditorFloor()
        {
            scrLevelMaker maker = scrLevelMaker.instance;
            if (maker == null || maker.listFloors == null || maker.listFloors.Count == 0 || maker.listFloors[0] == null)
                throw new InvalidOperationException("エディタ床がまだ生成されていません。");

            return Probe(maker.listFloors[0].gameObject, "editor-floor-0");
        }

        private static GameObject FindLoadedObject(string targetName)
        {
            GameObject active = GameObject.Find(targetName);
            if (active != null) return active;

            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            GameObject partial = null;
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null) continue;
                if (string.Equals(go.name, targetName, StringComparison.OrdinalIgnoreCase))
                    return go;
                if (partial == null && go.name.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                    partial = go;
            }
            return partial;
        }

        private static AssetProbeResult Probe(GameObject root, string label)
        {
            StringBuilder report = new StringBuilder(32768);
            int gameObjects = 0;
            int components = 0;

            report.AppendLine("ADOFAI Editor QoL Asset Probe");
            report.AppendLine("Mod version: " + ModVersion.Current);
            report.AppendLine("Unity: " + Application.unityVersion);
            report.AppendLine("Root: " + root.name);
            report.AppendLine("Scene: " + root.scene.name);
            report.AppendLine("HideFlags: " + root.hideFlags);
            report.AppendLine();

            DumpGameObject(root, report, 0, ref gameObjects, ref components);

            string directory = Path.Combine(Main.ModPath, "AssetProbe");
            Directory.CreateDirectory(directory);
            string fileName = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Sanitize(label) + ".txt";
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));

            if (Main.Logger != null)
                Main.Logger.Log("Asset Probe: " + root.name + " -> " + path);

            return new AssetProbeResult
            {
                TargetName = root.name,
                ReportPath = path,
                GameObjectCount = gameObjects,
                ComponentCount = components,
                RootSummary = BuildRootSummary(root)
            };
        }

        private static void DumpGameObject(GameObject go, StringBuilder report, int depth,
            ref int gameObjects, ref int components)
        {
            if (go == null || gameObjects >= MaxNodes || depth > MaxDepth) return;
            gameObjects++;

            string indent = new string(' ', depth * 2);
            report.Append(indent).Append("GameObject: ").Append(go.name)
                .Append(" | activeSelf=").Append(go.activeSelf)
                .Append(" | activeInHierarchy=").Append(go.activeInHierarchy)
                .Append(" | layer=").Append(go.layer)
                .Append(" | tag=").Append(SafeTag(go))
                .AppendLine();

            Component[] local = go.GetComponents<Component>();
            for (int i = 0; i < local.Length; i++)
            {
                Component component = local[i];
                components++;
                if (component == null)
                {
                    report.Append(indent).AppendLine("  Component: <missing script>");
                    continue;
                }
                DumpComponent(component, report, indent + "  ");
            }

            Transform transform = go.transform;
            for (int i = 0; i < transform.childCount && gameObjects < MaxNodes; i++)
                DumpGameObject(transform.GetChild(i).gameObject, report, depth + 1, ref gameObjects, ref components);
        }

        private static void DumpComponent(Component component, StringBuilder report, string indent)
        {
            Type type = component.GetType();
            report.Append(indent).Append("Component: ").Append(type.FullName).AppendLine();

            Renderer renderer = component as Renderer;
            if (renderer != null)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null) continue;
                    report.Append(indent).Append("  Material[").Append(i).Append("]: ")
                        .Append(material.name)
                        .Append(" | shader=").Append(material.shader != null ? material.shader.name : "<null>")
                        .Append(" | mainTexture=").Append(material.mainTexture != null ? DescribeObject(material.mainTexture) : "<null>")
                        .AppendLine();
                }
            }

            MeshFilter meshFilter = component as MeshFilter;
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Mesh mesh = meshFilter.sharedMesh;
                report.Append(indent).Append("  Mesh: ").Append(mesh.name)
                    .Append(" | vertices=").Append(mesh.vertexCount)
                    .Append(" | subMeshes=").Append(mesh.subMeshCount)
                    .Append(" | bounds=").Append(mesh.bounds)
                    .AppendLine();
            }

            SpriteRenderer spriteRenderer = component as SpriteRenderer;
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                Sprite sprite = spriteRenderer.sprite;
                report.Append(indent).Append("  Sprite: ").Append(sprite.name)
                    .Append(" | texture=").Append(sprite.texture != null ? DescribeObject(sprite.texture) : "<null>")
                    .Append(" | rect=").Append(sprite.rect)
                    .Append(" | pixelsPerUnit=").Append(sprite.pixelsPerUnit)
                    .AppendLine();
            }

            DumpUnityObjectFields(component, type, report, indent + "  ");
        }

        private static void DumpUnityObjectFields(Component component, Type type, StringBuilder report, string indent)
        {
            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch
            {
                return;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                try
                {
                    UnityEngine.Object value = field.GetValue(component) as UnityEngine.Object;
                    if (value != null)
                        report.Append(indent).Append("ref ").Append(field.Name).Append(" = ")
                            .Append(DescribeObject(value)).AppendLine();
                }
                catch (Exception ex)
                {
                    report.Append(indent).Append("ref ").Append(field.Name)
                        .Append(" = <error: ").Append(ex.GetType().Name).AppendLine(">");
                }
            }
        }

        private static string BuildRootSummary(GameObject root)
        {
            Component[] components = root.GetComponents<Component>();
            List<string> names = new List<string>(components.Length);
            for (int i = 0; i < components.Length; i++)
                names.Add(components[i] != null ? components[i].GetType().Name : "<missing>");
            return root.name + " [" + string.Join(", ", names.ToArray()) + "]";
        }

        private static string DescribeObject(UnityEngine.Object value)
        {
            return value.GetType().Name + " '" + value.name + "' (id=" + value.GetInstanceID() + ")";
        }

        private static string SafeTag(GameObject go)
        {
            try { return go.tag; }
            catch { return "<unavailable>"; }
        }

        private static string Sanitize(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
                result.Append(Array.IndexOf(invalid, value[i]) >= 0 ? '_' : value[i]);
            return result.ToString();
        }
    }
}
