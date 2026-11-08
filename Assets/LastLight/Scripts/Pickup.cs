using UnityEngine;

namespace LastLight
{
    public enum PickupKind { Coin, Booster, Shield, Flashlight }

    /// <summary>
    /// A floating pickup. Powerups sit beside the road and are collected by staring at them for 1.5 seconds.
    /// Coins sit on the road at jeep height and are collected by driving through them.
    /// Both show while lit or on the disk, and stay until collected or until they pass behind the disk.
    /// </summary>
    public class Pickup : MonoBehaviour
    {
        public PickupKind kind;
        public Chunk chunk;
        public bool alive = true;
        public float dwell;

        float spinPhase;
        Vector3 basePos;
        GameObject visual;
        static Mesh coinMesh, boosterMesh, shieldMesh, flashlightMesh;

        public const float StareSeconds = 1.5f;
        public float DwellSeconds => StareSeconds;
        public float Progress => Mathf.Clamp01(dwell / DwellSeconds);
        public bool Visible => visual != null && visual.activeSelf;

        /// <summary>Position in diorama-local units.</summary>
        public Vector3 DioramaLocal => chunk.root.localPosition + basePos;

        public static Pickup Create(PickupKind kind, Chunk chunk, Material mat, Vector3 chunkLocal, float phase)
        {
            BuildMeshes();
            Mesh mesh = kind switch
            {
                PickupKind.Booster => boosterMesh,
                PickupKind.Shield => shieldMesh,
                PickupKind.Flashlight => flashlightMesh,
                _ => coinMesh
            };
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(chunk.root, false);
            go.transform.localPosition = chunkLocal;
            var p = go.AddComponent<Pickup>();
            p.kind = kind;
            p.chunk = chunk;
            p.basePos = chunkLocal;
            p.spinPhase = phase;
            var r = MeshBuilder.Spawn("Icon", go.transform, mesh, mat);
            float scale = kind == PickupKind.Coin ? 0.8f : 1.5f;
            r.transform.localScale = Vector3.one * scale;
            p.visual = r.gameObject;
            p.visual.SetActive(false);
            return p;
        }

        public void SetVisible(bool visible)
        {
            if (visual != null && visual.activeSelf != visible) visual.SetActive(visible);
        }

        public void Animate(float time)
        {
            if (!Visible) return;
            transform.localPosition = basePos + Vector3.up * Mathf.Sin(time * 2f + spinPhase) * 0.2f;
            // Powerups rock gently so the icon stays readable; coins spin all the way round.
            float yaw = kind == PickupKind.Coin ? (time + spinPhase) * 140f : Mathf.Sin(time * 1.4f + spinPhase) * 22f;
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void Consume()
        {
            alive = false;
            gameObject.SetActive(false);
        }

        static void BuildMeshes()
        {
            if (coinMesh != null) return;
            var mb = new MeshBuilder();

            // Coin: a gold disc with a raised centre.
            var outer = new Vector2[16];
            var inner = new Vector2[16];
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                outer[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.85f;
                inner[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f;
            }
            mb.Prism(outer, 0.16f, Palette.CoinGold);
            mb.Prism(inner, 0.26f, Palette.CoinGold * 1.3f);
            coinMesh = mb.ToMesh("LL_CoinIcon"); mb.Clear();

            // Booster: two stacked chevrons pointing up the road.
            for (int i = 0; i < 2; i++)
            {
                float y = -0.55f + i * 0.72f;
                Vector2[] leftArm = { P(-0.85f, y - 0.1f), P(-0.85f, y + 0.25f), P(0f, y + 0.85f), P(0f, y + 0.5f) };
                Vector2[] rightArm = { P(0f, y + 0.5f), P(0f, y + 0.85f), P(0.85f, y + 0.25f), P(0.85f, y - 0.1f) };
                Color c = i == 0 ? Palette.BoostOrange : Palette.BoostOrange * 1.3f;
                mb.Prism(leftArm, 0.2f, c);
                mb.Prism(rightArm, 0.2f, c);
            }
            boosterMesh = mb.ToMesh("LL_BoosterIcon"); mb.Clear();

            // Shield: a classic heater shape with a lighter inner plate.
            Vector2[] shield = { P(-0.8f, 0.85f), P(0.8f, 0.85f), P(0.8f, -0.05f), P(0f, -1f), P(-0.8f, -0.05f) };
            Vector2[] plate = { P(-0.5f, 0.58f), P(0.5f, 0.58f), P(0.5f, -0.02f), P(0f, -0.62f), P(-0.5f, -0.02f) };
            mb.Prism(shield, 0.18f, Palette.ShieldBlue);
            mb.Prism(plate, 0.28f, Palette.ShieldBlue * 1.5f);
            shieldMesh = mb.ToMesh("LL_ShieldIcon"); mb.Clear();

            // Flashlight: body, flared head and a pale beam, pointing up.
            Vector2[] body = { P(-0.24f, -1f), P(-0.24f, 0.1f), P(0.24f, 0.1f), P(0.24f, -1f) };
            Vector2[] head = { P(-0.24f, 0.1f), P(-0.48f, 0.5f), P(0.48f, 0.5f), P(0.24f, 0.1f) };
            Vector2[] beam = { P(-0.4f, 0.55f), P(-0.85f, 1.15f), P(0.85f, 1.15f), P(0.4f, 0.55f) };
            mb.Prism(body, 0.3f, Palette.LightBody);
            mb.Prism(head, 0.36f, Palette.LightBody * 1.3f);
            mb.Prism(beam, 0.08f, Palette.LightBeam);
            flashlightMesh = mb.ToMesh("LL_FlashlightIcon"); mb.Clear();
        }

        static Vector2 P(float x, float y) => new Vector2(x, y);
    }
}
