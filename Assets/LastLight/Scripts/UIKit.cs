using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    /// <summary>Text for the floating panels, in the Anton comic face used by the button art.</summary>
    public static class UIText
    {
        public const int FontSize = 64;
        public const int Queue = 3030;      // after the sprite panels (Transparent+10), so text draws on top
        static Font font;
        static Material material, onTopMaterial;

        public static Font Font
        {
            get
            {
                if (font != null) return font;
                font = Resources.Load<Font>(UISprite.ResourceFolder + "Anton");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Material Material
        {
            get
            {
                if (material != null) return material;
                material = new Material(Font.material) { renderQueue = Queue, name = "LL_UIText" };
                Font.textureRebuilt += f => { if (f == font && material != null) material.mainTexture = f.material.mainTexture; };
                return material;
            }
        }

        /// <summary>
        /// Text that skips the depth test, for HUD labels that must never hide behind terrain.
        /// Uses the Last Light text shader, kept in builds by the LL_UIText material in Resources.
        /// </summary>
        public static Material OnTopMaterial
        {
            get
            {
                if (onTopMaterial != null) return onTopMaterial;
                var source = Resources.Load<Material>(UISprite.ResourceFolder + "LL_UIText");
                Shader shader = source != null ? source.shader : Shader.Find("LastLight/Text");
                if (shader == null) return Material;
                onTopMaterial = new Material(shader) { name = "LL_UIText_OnTop", renderQueue = Queue + 10 };
                onTopMaterial.mainTexture = Font.material.mainTexture;
                onTopMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                Font.textureRebuilt += f => { if (f == font && onTopMaterial != null) onTopMaterial.mainTexture = f.material.mainTexture; };
                return onTopMaterial;
            }
        }

        /// <summary>A text mesh whose capital letters are roughly <paramref name="height"/> panel units tall.</summary>
        public static TextMesh Make(Transform parent, string name, string text, float height, TextAnchor anchor, Color color, int order = 20, bool onTop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMesh>();
            t.font = Font;
            t.fontSize = FontSize;
            t.characterSize = height / (FontSize * 0.1f) * 1.35f;
            t.anchor = anchor;
            t.alignment = anchor == TextAnchor.MiddleCenter || anchor == TextAnchor.UpperCenter || anchor == TextAnchor.LowerCenter
                ? TextAlignment.Center
                : anchor == TextAnchor.MiddleRight || anchor == TextAnchor.UpperRight || anchor == TextAnchor.LowerRight ? TextAlignment.Right : TextAlignment.Left;
            t.color = color;
            t.text = text;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = onTop ? OnTopMaterial : Material;
            r.sortingOrder = order;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }
    }

    /// <summary>Panel text with an ink drop shadow, matching the comic lettering on the buttons.</summary>
    public sealed class Label
    {
        public readonly Transform transform;
        readonly TextMesh main, shadow;
        Color color;
        float alpha = 1f;

        public Label(Transform parent, string name, string text, float height, TextAnchor anchor, Color color, Vector2 position, int order = 20, bool onTop = false)
        {
            transform = new GameObject(name).transform;
            transform.SetParent(parent, false);
            transform.localPosition = new Vector3(position.x, position.y, -0.06f - order * 0.002f);
            shadow = UIText.Make(transform, "Shadow", text, height, anchor, Palette.UIInk, order, onTop);
            shadow.transform.localPosition = new Vector3(height * 0.07f, -height * 0.07f, 0.01f);
            main = UIText.Make(transform, "Text", text, height, anchor, color, order + 1, onTop);
            this.color = color;
        }

        public string Text
        {
            get => main.text;
            set { if (main.text != value) { main.text = value; shadow.text = value; } }
        }

        public Color Color { get => color; set { color = value; Apply(); } }
        public float Alpha { get => alpha; set { alpha = value; Apply(); } }

        public Vector2 Position
        {
            get => transform.localPosition;
            set => transform.localPosition = new Vector3(value.x, value.y, transform.localPosition.z);
        }

        public bool Visible
        {
            get => transform.gameObject.activeSelf;
            set { if (transform.gameObject.activeSelf != value) transform.gameObject.SetActive(value); }
        }

        void Apply()
        {
            Color c = color; c.a *= alpha;
            main.color = c;
            Color s = Palette.UIInk; s.a = alpha;
            shadow.color = s;
        }
    }

    /// <summary>A button you look at to focus and pinch to press. Art comes in a normal and a focused texture.</summary>
    public sealed class GazeButton
    {
        public readonly UISprite sprite;
        public readonly Transform root;
        public Vector2 Size;
        public bool Enabled = true;
        public bool Focusable = true;
        Texture2D normal, focused;
        float scale = 1f;

        public GazeButton(Transform parent, string name, string texture, Vector2 center, Vector2 size, Material mat, int order = 3)
        {
            root = new GameObject("Button " + name).transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(center.x, center.y, 0f);
            normal = UISprite.Load(texture);
            focused = UISprite.Load(texture + "_focus");
            Size = size;
            sprite = new UISprite(name, root, normal, size, mat, order);
        }

        public Vector2 Center
        {
            get => root.localPosition;
            set => root.localPosition = new Vector3(value.x, value.y, root.localPosition.z);
        }

        public bool Visible
        {
            get => root.gameObject.activeSelf;
            set { if (root.gameObject.activeSelf != value) root.gameObject.SetActive(value); }
        }

        /// <summary>Swaps the button art for another normal/focused texture pair.</summary>
        public void SetArt(string texture)
        {
            normal = UISprite.Load(texture);
            focused = UISprite.Load(texture + "_focus");
            sprite.Texture = normal;
        }

        public bool Contains(Vector2 p, float pad)
        {
            Vector2 d = p - Center;
            return Mathf.Abs(d.x) <= Size.x * 0.5f + pad && Mathf.Abs(d.y) <= Size.y * 0.5f + pad;
        }

        public void Refresh(bool isFocused, float dt, float alpha = 1f)
        {
            bool f = isFocused && Enabled;
            sprite.Texture = f ? focused : normal;
            float target = f ? 1.06f : 1f;
            scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-14f * Mathf.Max(dt, 0f)));
            sprite.Size = Size * scale;
            sprite.Color = Enabled ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            sprite.Alpha = alpha;
        }
    }

    /// <summary>
    /// Gaze focus shared by every panel: a padded hit test, a larger margin once focused so eye jitter
    /// doesn't drop it, and a short grace period for blinks and glances.
    /// </summary>
    public sealed class GazeFocus
    {
        const float FocusPadding = 0.3f;
        const float KeepFocusPadding = 0.7f;
        const float FocusGrace = 0.35f;

        public int Focused { get; private set; } = -1;
        float lost;

        public void Clear() { Focused = -1; lost = 0f; }

        /// <summary>Updates focus from a gaze point on the panel. Hidden buttons are skipped.</summary>
        public int Update(float dt, Vector2? point, IList<GazeButton> buttons)
        {
            int hit = -1;
            if (point.HasValue)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    if (buttons[i] == null || !buttons[i].Visible || !buttons[i].Focusable) continue;
                    if (buttons[i].Contains(point.Value, Focused == i ? KeepFocusPadding : FocusPadding)) { hit = i; break; }
                }
            }
            if (hit >= 0) { Focused = hit; lost = 0f; }
            else if (Focused >= 0)
            {
                lost += dt;
                if (lost > FocusGrace || buttons[Focused] == null || !buttons[Focused].Visible) Focused = -1;
            }
            return Focused;
        }

        /// <summary>Intersects a world ray with a panel plane (the panel faces -Z); the point is in panel units.</summary>
        public static bool Hit(Transform root, Ray ray, out Vector2 point)
        {
            point = default;
            Vector3 o = root.InverseTransformPoint(ray.origin);
            Vector3 d = root.InverseTransformDirection(ray.direction);
            if (d.z <= 1e-5f) return false;
            float t = -o.z / d.z;
            if (t <= 0f) return false;
            Vector3 p = o + d * t;
            point = new Vector2(p.x, p.y);
            return true;
        }
    }
}
