using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    public sealed class TextMaskRuntime : MonoBehaviour, IRuntimeEffect
    {
        private scrTextDecoration decoration;
        private Text text;
        private string targetTag;
        private GameObject helper;
        private SpriteAlphaMask alphaMask;
        private Texture2D texture;
        private Sprite sprite;
        private Material renderMaterial;
        private string lastSignature;
        private Vector2 localCenter;
        private Vector2 localSize;
        private int nextRefreshFrame;

        internal void Configure(scrTextDecoration source, string tag)
        {
            decoration = source;
            text = source == null ? null : source.text;
            targetTag = tag ?? string.Empty;
            EnsureHelper();
            if (helper != null && source != null) helper.layer = source.gameObject.layer;
            RefreshMask(true);
        }

        private void Start()
        {
            RefreshMask(true);
        }

        private void LateUpdate()
        {
            if (!Main.Enabled)
            {
                if (helper != null) helper.SetActive(false);
                if (text != null) text.enabled = true;
                return;
            }
            if (decoration == null || text == null || string.IsNullOrEmpty(targetTag)) return;
            EnsureHelper();
            if (Time.frameCount >= nextRefreshFrame) RefreshMask(false);
            UpdateHelperTransform();

            // The text itself defines the mask and should not be drawn during gameplay.
            // It stays visible in the editor so the mask area remains editable.
            bool editorVisible = ADOBase.editor != null && ADOBase.editor.gameObject.activeInHierarchy;
            if (editorVisible)
            {
                if (!text.enabled) text.enabled = true;
            }
            else if (text.enabled)
            {
                text.enabled = false;
            }
        }

        private void EnsureHelper()
        {
            if (helper != null) return;
            helper = new GameObject("Editor QoL Text Mask " + targetTag);
            helper.hideFlags = HideFlags.HideAndDontSave;
            alphaMask = helper.AddComponent<SpriteAlphaMask>();
            alphaMask.pivotTrans = helper.transform;
            alphaMask.childTrans = null;
            alphaMask.targetTag = targetTag;
            SpriteAlphaMaskUtils.doRefreshMaskCache = true;
            ForceTargetCacheRefresh();
        }

        private void RefreshMask(bool force)
        {
            nextRefreshFrame = Time.frameCount + 10;
            if (text == null || text.font == null) return;
            string signature = (text.text ?? string.Empty) + "\n" + text.font.name + "\n" + text.fontSize + "\n" +
                               text.fontStyle + "\n" + text.lineSpacing + "\n" + text.rectTransform.rect.size;
            if (!force && signature == lastSignature) return;
            lastSignature = signature;

            Sprite generated;
            Vector2 center;
            Vector2 size;
            Texture2D generatedTexture;
            if (!TryRenderText(text, out generatedTexture, out generated, out center, out size))
            {
                if (helper != null) helper.SetActive(false);
                return;
            }

            DestroyGeneratedAssets();
            texture = generatedTexture;
            sprite = generated;
            localCenter = center;
            localSize = size;
            alphaMask.sprite = sprite;
            alphaMask.targetTag = targetTag;
            helper.SetActive(true);
            SpriteAlphaMaskUtils.doRefreshMaskCache = true;
            ForceTargetCacheRefresh();
            UpdateHelperTransform();
        }

        private void ForceTargetCacheRefresh()
        {
            if (decoration == null || decoration.manager == null || string.IsNullOrEmpty(targetTag)) return;
            List<scrDecoration> targets;
            if (!decoration.manager.taggedDecorations.TryGetValue(targetTag, out targets) || targets == null) return;
            foreach (scrDecoration target in targets)
            {
                scrVisualDecoration visual = target as scrVisualDecoration;
                if (visual != null) decoration.manager.ForceUpdateAlphaMaskCache(visual);
            }
        }

        private bool TryRenderText(Text source, out Texture2D outputTexture, out Sprite outputSprite,
            out Vector2 center, out Vector2 size)
        {
            outputTexture = null;
            outputSprite = null;
            center = Vector2.zero;
            size = Vector2.zero;
            if (string.IsNullOrEmpty(source.text) || source.font == null) return false;

            source.font.RequestCharactersInTexture(source.text, Math.Max(1, source.fontSize), source.fontStyle);
            TextGenerator generator = new TextGenerator(Math.Max(16, source.text.Length * 4));
            TextGenerationSettings settings = source.GetGenerationSettings(source.rectTransform.rect.size);
            if (!generator.Populate(source.text, settings)) return false;
            IList<UIVertex> vertices = generator.verts;
            int usable = Math.Max(0, vertices.Count - 4);
            if (usable < 4) return false;

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < usable; i++)
            {
                Vector3 position = vertices[i].position;
                minX = Math.Min(minX, position.x);
                minY = Math.Min(minY, position.y);
                maxX = Math.Max(maxX, position.x);
                maxY = Math.Max(maxY, position.y);
            }
            if (maxX <= minX || maxY <= minY) return false;

            const float padding = 3f;
            float rawWidth = maxX - minX + padding * 2f;
            float rawHeight = maxY - minY + padding * 2f;
            float downscale = Math.Min(1f, Math.Min(2048f / rawWidth, 2048f / rawHeight));
            int width = Mathf.Clamp(Mathf.CeilToInt(rawWidth * downscale), 2, 2048);
            int height = Mathf.Clamp(Mathf.CeilToInt(rawHeight * downscale), 2, 2048);

            Shader shader = Shader.Find("UI/Default") ?? Shader.Find("Unlit/Transparent");
            if (shader == null) return false;
            if (renderMaterial == null || renderMaterial.shader != shader)
            {
                if (renderMaterial != null) Destroy(renderMaterial);
                renderMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            renderMaterial.mainTexture = source.font.material.mainTexture;

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.clear);
                GL.PushMatrix();
                try
                {
                    GL.LoadPixelMatrix(minX - padding, maxX + padding, minY - padding, maxY + padding);
                    if (!renderMaterial.SetPass(0)) return false;
                    GL.Begin(GL.QUADS);
                    GL.Color(Color.white);
                    for (int i = 0; i + 3 < usable; i += 4)
                    {
                        for (int j = 0; j < 4; j++)
                        {
                            UIVertex vertex = vertices[i + j];
                            GL.TexCoord2(vertex.uv0.x, vertex.uv0.y);
                            GL.Vertex(vertex.position);
                        }
                    }
                    GL.End();
                }
                finally
                {
                    GL.PopMatrix();
                }

                outputTexture = new Texture2D(width, height, TextureFormat.ARGB32, false, false)
                {
                    name = "Editor QoL Text Mask Texture",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                outputTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                outputTexture.Apply(false, false);
                outputSprite = Sprite.Create(outputTexture, new Rect(0f, 0f, width, height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                outputSprite.name = "Editor QoL Text Mask Sprite";
                outputSprite.hideFlags = HideFlags.HideAndDontSave;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            size = new Vector2(rawWidth, rawHeight);
            return outputTexture != null && outputSprite != null;
        }

        private void UpdateHelperTransform()
        {
            if (helper == null || sprite == null || text == null) return;
            RectTransform rect = text.rectTransform;
            helper.transform.position = rect.TransformPoint(new Vector3(localCenter.x, localCenter.y, 0f));
            helper.transform.rotation = rect.rotation;
            float worldWidth = rect.TransformVector(new Vector3(localSize.x, 0f, 0f)).magnitude;
            float worldHeight = rect.TransformVector(new Vector3(0f, localSize.y, 0f)).magnitude;
            Vector2 spriteSize = sprite.bounds.size;
            helper.transform.localScale = new Vector3(
                spriteSize.x <= 0f ? 1f : worldWidth / spriteSize.x,
                spriteSize.y <= 0f ? 1f : worldHeight / spriteSize.y,
                1f);
            helper.SetActive(decoration == null || decoration.GetVisible());
        }

        private void DestroyGeneratedAssets()
        {
            if (sprite != null) Destroy(sprite);
            if (texture != null) Destroy(texture);
            sprite = null;
            texture = null;
        }

        public void StopAndRestore()
        {
            if (text != null) text.enabled = true;
            if (helper != null) helper.SetActive(false);
            ForceTargetCacheRefresh();
        }

        private void OnDestroy()
        {
            StopAndRestore();
            DestroyGeneratedAssets();
            if (renderMaterial != null) Destroy(renderMaterial);
            if (helper != null) Destroy(helper);
            SpriteAlphaMaskUtils.doRefreshMaskCache = true;
        }
    }
}
