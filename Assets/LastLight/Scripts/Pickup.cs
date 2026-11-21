using System.Collections.Generic;
using UnityEngine;

namespace LastLight
{
    public enum PickupKind { Coin, Booster, Shield, Flashlight, Turret, Shovel, Repair }

    /// <summary>
    /// A floating pickup. Powerups sit beside the road and are collected by staring at them: 1 second at the start
    /// of a run, a little quicker every 150 m, down to half a second.
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
        UISprite badge;

        /// <summary>Sprite material for the flat badge icons; set by the director at boot.</summary>
        public static Material IconMaterial;
        /// <summary>The player's head in disk units: badges turn to face it.</summary>
        public static Vector3 FaceTowards = new Vector3(0f, 15f, -14f);
        /// <summary>World width of a powerup badge, in game units.</summary>
        public const float BadgeSize = 2.8f;
        static readonly Dictionary<PickupKind, Texture2D> iconCache = new Dictionary<PickupKind, Texture2D>();

        /// <summary>The comic-style badge texture for a powerup, or null where there isn't one (coins and the booster keep 3D icons).</summary>
        public static Texture2D IconTexture(PickupKind kind)
        {
            string name = kind switch
            {
                PickupKind.Shovel => "pu_shovel",
                PickupKind.Turret => "pu_turret",
                PickupKind.Shield => "pu_shield",
                PickupKind.Flashlight => "pu_flashlight",
                PickupKind.Repair => "pu_repair",
                _ => null
            };
            if (name == null) return null;
            if (!iconCache.TryGetValue(kind, out var tex) || tex == null)
            {
                tex = Resources.Load<Texture2D>(UISprite.ResourceFolder + name);
                iconCache[kind] = tex;
            }
            return tex;
        }
        static Mesh coinMesh, boosterMesh, shieldMesh, flashlightMesh, turretMesh, shovelMesh, repairMesh;

        /// <summary>Stare time at the start of a run.</summary>
        public const float StareSeconds = 1f;
        /// <summary>The stare time never drops below this.</summary>
        public const float MinStareSeconds = 0.5f;
        /// <summary>Every this many metres the stare gets a little quicker.</summary>
        public const float StareStepMetres = 150f;
        public const float StareStepSeconds = 0.05f;
        /// <summary>The stare time now: set by the director as the run goes on.</summary>
        public static float CurrentStareSeconds = StareSeconds;

        /// <summary>1 s at the start, 0.05 s quicker every 150 m, never under 0.5 s (reached at 1.5 km).</summary>
        public static float StareSecondsAt(float distance)
            => Mathf.Max(MinStareSeconds, StareSeconds - Mathf.Floor(Mathf.Max(distance, 0f) / StareStepMetres) * StareStepSeconds);

        public float DwellSeconds => CurrentStareSeconds;
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
                PickupKind.Turret => turretMesh,
                PickupKind.Shovel => shovelMesh,
                PickupKind.Repair => repairMesh,
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
            Texture2D icon = IconTexture(kind);
            if (icon != null && IconMaterial != null)
            {
                // A flat comic badge that turns to face the player.
                p.badge = new UISprite("Badge", go.transform, icon, Vector2.one * BadgeSize, IconMaterial, 6);
                p.visual = p.badge.transform.gameObject;
            }
            else
            {
                var r = MeshBuilder.Spawn("Icon", go.transform, mesh, mat);
                float scale = kind == PickupKind.Coin ? 0.8f : 1.5f;
                r.transform.localScale = Vector3.one * scale;
                p.visual = r.gameObject;
            }
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
            if (badge != null)
            {
                // Badges face the player and sway a little.
                Vector3 toward = DioramaLocal - FaceTowards;
                transform.localRotation = Quaternion.LookRotation(toward, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(time * 1.4f + spinPhase) * 6f);
                return;
            }
            // Powerups rock gently so the icon stays readable; coins spin all the way round.
            float yaw = kind == PickupKind.Coin ? (time + spinPhase) * 140f : Mathf.Sin(time * 1.4f + spinPhase) * 22f;
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void Consume()
        {
            alive = false;
            gameObject.SetActive(false);
        }

        /// <summary>The 3D icon for a pickup kind, shared with the storage compartment and its garage shop.</summary>
        public static Mesh IconMesh(PickupKind kind)
        {
            BuildMeshes();
            return kind switch
            {
                PickupKind.Booster => boosterMesh,
                PickupKind.Shield => shieldMesh,
                PickupKind.Flashlight => flashlightMesh,
                PickupKind.Turret => turretMesh,
                PickupKind.Shovel => shovelMesh,
                PickupKind.Repair => repairMesh,
                _ => coinMesh
            };
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

            // Turret: a gun head on a base with twin barrels pointing up and right.
            Vector2[] tBase = { P(-0.8f, -1f), P(-0.8f, -0.7f), P(0.8f, -0.7f), P(0.8f, -1f) };
            Vector2[] tHead = { P(-0.55f, -0.7f), P(-0.55f, 0.05f), P(-0.25f, 0.3f), P(0.4f, 0.3f), P(0.55f, 0.05f), P(0.55f, -0.7f) };
            Vector2[] barrel1 = { P(0.2f, 0.05f), P(0.85f, 0.85f), P(0.98f, 0.74f), P(0.33f, -0.06f) };
            Vector2[] barrel2 = { P(0.0f, 0.2f), P(0.62f, 1.0f), P(0.75f, 0.9f), P(0.13f, 0.1f) };
            mb.Prism(tBase, 0.3f, Palette.Metal * 1.4f);
            mb.Prism(tHead, 0.26f, Palette.TurretRed);
            mb.Prism(barrel1, 0.34f, Palette.LightBody);
            mb.Prism(barrel2, 0.34f, Palette.LightBody * 0.85f);
            turretMesh = mb.ToMesh("LL_TurretIcon"); mb.Clear();

            // Shovel: a wide plough blade with a yellow stripe and two arms.
            Vector2[] blade = { P(-1f, -0.15f), P(-0.9f, 0.55f), P(0.9f, 0.55f), P(1f, -0.15f), P(0.8f, -0.45f), P(-0.8f, -0.45f) };
            Vector2[] stripe = { P(-0.86f, 0.3f), P(-0.83f, 0.45f), P(0.83f, 0.45f), P(0.86f, 0.3f) };
            Vector2[] armL = { P(-0.5f, -0.45f), P(-0.5f, -1f), P(-0.3f, -1f), P(-0.3f, -0.45f) };
            Vector2[] armR = { P(0.3f, -0.45f), P(0.3f, -1f), P(0.5f, -1f), P(0.5f, -0.45f) };
            mb.Prism(blade, 0.22f, Palette.ShovelSteel * 1.3f);
            mb.Prism(stripe, 0.3f, Palette.ShovelYellow * 1.3f);
            mb.Prism(armL, 0.26f, Palette.Metal * 1.3f);
            mb.Prism(armR, 0.26f, Palette.Metal * 1.3f);
            shovelMesh = mb.ToMesh("LL_ShovelIcon"); mb.Clear();

            // Repair: a green gear, eight teeth round a ring with a dark hub.
            var ring = new Vector2[20];
            for (int i = 0; i < ring.Length; i++)
            {
                float a = i * Mathf.PI * 2f / ring.Length;
                ring[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.68f;
            }
            mb.Prism(ring, 0.26f, Palette.RepairGreen);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                Vector3 dir = Quaternion.Euler(0f, 0f, a) * Vector3.up;
                mb.Box(dir * 0.8f, new Vector3(0.3f, 0.34f, 0.26f), Quaternion.Euler(0f, 0f, a), Palette.RepairGreen * 1.15f);
            }
            var hub = new Vector2[12];
            for (int i = 0; i < hub.Length; i++)
            {
                float a = i * Mathf.PI * 2f / hub.Length;
                hub[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.3f;
            }
            mb.Prism(hub, 0.34f, Palette.Metal * 0.8f);
            repairMesh = mb.ToMesh("LL_RepairIcon"); mb.Clear();
        }

        static Vector2 P(float x, float y) => new Vector2(x, y);
    }
}
