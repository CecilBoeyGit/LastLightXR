using UnityEngine;

namespace LastLight
{
    /// <summary>
    /// A flat textured quad on a menu panel. Units are diorama units (4 cm each at the default scale).
    /// The quad faces -Z, toward a viewer standing in front of its panel, and is drawn double-sided.
    /// </summary>
    public sealed class UISprite
    {
        public const string ResourceFolder = "LastLightUI/";
        static Mesh quad;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int TexId = Shader.PropertyToID("_MainTex");

        public readonly Transform transform;
        readonly MeshRenderer renderer;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        Texture texture;
        Color color = Color.white;
        float alpha = 1f;
        Vector2 size;

        public UISprite(string name, Transform parent, Texture tex, Vector2 size, Material material, int order)
        {
            var go = new GameObject(name);
            transform = go.transform;
            transform.SetParent(parent, false);
            // Later layers sit a hair closer to the viewer, as well as sorting later.
            transform.localPosition = new Vector3(0f, 0f, -0.02f * order);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            texture = tex;
            Size = size;
            Apply();
        }

        public Vector2 Size
        {
            get => size;
            set { size = value; transform.localScale = new Vector3(value.x, value.y, 1f); }
        }

        /// <summary>Position on the panel plane, keeping the layer's depth offset.</summary>
        public Vector2 Position
        {
            get => transform.localPosition;
            set => transform.localPosition = new Vector3(value.x, value.y, transform.localPosition.z);
        }

        public Color Color { get => color; set { color = value; Apply(); } }

        /// <summary>Extra fade multiplied onto the colour's alpha, for panel-wide fades.</summary>
        public float Alpha { get => alpha; set { alpha = value; Apply(); } }

        public Texture Texture { get => texture; set { texture = value; Apply(); } }

        public bool Visible
        {
            get => transform.gameObject.activeSelf;
            set => transform.gameObject.SetActive(value);
        }

        void Apply()
        {
            Color c = color;
            c.a *= alpha;
            block.SetColor(ColorId, c);
            if (texture != null) block.SetTexture(TexId, texture);
            renderer.SetPropertyBlock(block);
        }

        public static Texture2D Load(string name)
        {
            var tex = Resources.Load<Texture2D>(ResourceFolder + name);
            if (tex == null) Debug.LogWarning("[LastLight] Missing UI texture: " + name);
            return tex;
        }

        /// <summary>The alpha-blended sprite material, and an additive copy for light shapes.</summary>
        public static void LoadMaterials(out Material alphaBlend, out Material additive)
        {
            var source = Resources.Load<Material>(ResourceFolder + "LL_UISprite");
            alphaBlend = source != null ? new Material(source) : new Material(Shader.Find("LastLight/Sprite"));
            additive = new Material(alphaBlend);
            additive.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            additive.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            additive.renderQueue = alphaBlend.renderQueue + 1;
        }

        static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "LL_UIQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                quad.RecalculateBounds();
                return quad;
            }
        }
    }
}
