using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OreHitboxVisualizer
{
    /// <summary>
    /// Guarda los depósitos (MineRock5) que el jugador local ha golpeado y dibuja la caja de soporte de cada trozo.
    /// La caja es la misma que usa el juego en MineRock5.UpdateSupport: collider.bounds del trozo, alineada a los ejes.
    /// </summary>
    internal static class HitboxManager
    {
        private const float RefreshInterval = 0.1f;

        private static readonly Dictionary<MineRock5, RockVisual> s_rocks = new Dictionary<MineRock5, RockVisual>();
        private static readonly List<MineRock5> s_toRemove = new List<MineRock5>();
        private static readonly Collider[] s_overlap = new Collider[128];

        private static int s_mask;
        private static int s_terrainLayer = -1;
        private static float s_nextRefresh;
        private static GameObject s_root;
        private static Mesh s_cubeMesh;
        private static Material s_lineColliding;
        private static Material s_lineFree;
        private static Material s_fillColliding;
        private static Material s_fillFree;

        public static void OnRockHit(MineRock5 rock, HitData hit)
        {
            if (rock == null || rock.m_hitAreas == null || hit == null)
            {
                return;
            }
            Player player = Player.m_localPlayer;
            if (player == null || hit.GetAttacker() != player)
            {
                return;
            }
            if (OreHitboxVisualizerPlugin.PickaxeOnly.Value && hit.m_damage.m_pickaxe <= 0f && hit.m_skill != Skills.SkillType.Pickaxes)
            {
                return;
            }

            if (!s_rocks.TryGetValue(rock, out RockVisual visual))
            {
                visual = new RockVisual(rock, GetRoot().transform);
                s_rocks[rock] = visual;
                s_nextRefresh = 0f;
            }
            visual.LastHitTime = Time.time;
        }

        public static void Tick()
        {
            if (s_rocks.Count == 0)
            {
                return;
            }
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                ClearAll();
                return;
            }

            float now = Time.time;
            float hideDistance = OreHitboxVisualizerPlugin.HideDistance.Value;
            float hideAfter = OreHitboxVisualizerPlugin.HideAfterSeconds.Value;
            Vector3 playerPos = player.transform.position;

            s_toRemove.Clear();
            foreach (KeyValuePair<MineRock5, RockVisual> pair in s_rocks)
            {
                MineRock5 rock = pair.Key;
                if (rock == null || rock.m_allDestroyed
                    || Vector3.Distance(playerPos, rock.transform.position) > hideDistance
                    || (hideAfter > 0f && now - pair.Value.LastHitTime > hideAfter))
                {
                    s_toRemove.Add(rock);
                }
            }
            foreach (MineRock5 rock in s_toRemove)
            {
                s_rocks[rock].Destroy();
                s_rocks.Remove(rock);
            }

            bool show = OreHitboxVisualizerPlugin.Enabled.Value;
            GameObject root = GetRoot();
            if (root.activeSelf != show)
            {
                root.SetActive(show);
                s_nextRefresh = 0f;
            }
            if (!show || now < s_nextRefresh)
            {
                return;
            }
            s_nextRefresh = now + RefreshInterval;

            foreach (RockVisual visual in s_rocks.Values)
            {
                visual.Refresh();
            }
        }

        public static void ClearAll()
        {
            foreach (RockVisual visual in s_rocks.Values)
            {
                visual.Destroy();
            }
            s_rocks.Clear();
        }

        /// <summary>Colores, grosor u opacidad cambiados en la config: se rehacen materiales y mallas.</summary>
        public static void OnAppearanceChanged()
        {
            DestroyMaterial(ref s_lineColliding);
            DestroyMaterial(ref s_lineFree);
            DestroyMaterial(ref s_fillColliding);
            DestroyMaterial(ref s_fillFree);
            foreach (RockVisual visual in s_rocks.Values)
            {
                visual.DestroyAreas();
            }
            s_nextRefresh = 0f;
        }

        // ---------------------------------------------------------------- Colisión

        /// <summary>Calcula qué trozos vivos están "en rojo" según el modo configurado.</summary>
        internal static void ComputeStates(MineRock5 rock, bool[] alive, Bounds[] bounds, bool[] red)
        {
            if (s_mask == 0)
            {
                // Mismas capas que MineRock5.m_rayMask.
                s_mask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
                s_terrainLayer = LayerMask.NameToLayer("terrain");
            }

            int count = alive.Length;
            for (int i = 0; i < count; i++)
            {
                red[i] = false;
            }

            if (OreHitboxVisualizerPlugin.Mode.Value == HitboxMode.Collision)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!alive[i])
                    {
                        continue;
                    }
                    Collider own = rock.m_hitAreas[i].m_collider;
                    int n = Physics.OverlapBoxNonAlloc(bounds[i].center, bounds[i].extents, s_overlap, Quaternion.identity, s_mask);
                    for (int j = 0; j < n; j++)
                    {
                        // Los trozos del mismo depósito no cuentan como colisión.
                        if (IsValidContact(s_overlap[j], own) && rock.GetAreaIndex(s_overlap[j]) < 0)
                        {
                            red[i] = true;
                            break;
                        }
                    }
                }
                return;
            }

            // Modo Support: réplica de MineRock5.UpdateSupport (3 pasadas, los trozos sostenidos sostienen a sus vecinos).
            for (int iteration = 0; iteration < 3; iteration++)
            {
                for (int i = 0; i < count; i++)
                {
                    if (!alive[i] || red[i])
                    {
                        continue;
                    }
                    Collider own = rock.m_hitAreas[i].m_collider;
                    int n = Physics.OverlapBoxNonAlloc(bounds[i].center, bounds[i].extents, s_overlap, Quaternion.identity, s_mask);
                    for (int j = 0; j < n; j++)
                    {
                        Collider other = s_overlap[j];
                        if (IsValidContact(other, own) && GivesSupport(rock, other, red))
                        {
                            red[i] = true;
                            break;
                        }
                    }
                }
            }
        }

        private static bool IsValidContact(Collider other, Collider own)
        {
            return other != null && other != own && other.attachedRigidbody == null && !other.isTrigger;
        }

        /// <summary>Equivalente a MineRock5.GetSupport.</summary>
        private static bool GivesSupport(MineRock5 rock, Collider other, bool[] supported)
        {
            if (other.gameObject.layer == s_terrainLayer)
            {
                return true;
            }
            IDestructible destructible = other.gameObject.GetComponentInParent<IDestructible>();
            if (destructible == null)
            {
                return true;
            }
            if (ReferenceEquals(destructible, rock))
            {
                int index = rock.GetAreaIndex(other);
                if (index >= 0)
                {
                    return supported[index];
                }
            }
            return other.transform.position.y < rock.transform.position.y;
        }

        // ---------------------------------------------------------------- Recursos de dibujo

        private static GameObject GetRoot()
        {
            if (s_root == null)
            {
                s_root = new GameObject("OreHitboxVisualizer");
                Object.DontDestroyOnLoad(s_root);
            }
            return s_root;
        }

        internal static Mesh CubeMesh
        {
            get
            {
                if (s_cubeMesh == null)
                {
                    s_cubeMesh = BuildUnitCube();
                }
                return s_cubeMesh;
            }
        }

        internal static Material GetLineMaterial(bool colliding)
        {
            ref Material mat = ref (colliding ? ref s_lineColliding : ref s_lineFree);
            if (mat == null)
            {
                Color color = colliding ? OreHitboxVisualizerPlugin.CollidingColor.Value : OreHitboxVisualizerPlugin.FreeColor.Value;
                mat = CreateMaterial(color, 3201);
            }
            return mat;
        }

        internal static Material GetFillMaterial(bool colliding)
        {
            ref Material mat = ref (colliding ? ref s_fillColliding : ref s_fillFree);
            if (mat == null)
            {
                Color color = colliding ? OreHitboxVisualizerPlugin.CollidingColor.Value : OreHitboxVisualizerPlugin.FreeColor.Value;
                color.a = Mathf.Clamp01(OreHitboxVisualizerPlugin.FillOpacity.Value);
                mat = CreateMaterial(color, 3200);
            }
            return mat;
        }

        private static Material CreateMaterial(Color color, int renderQueue)
        {
            // Shader interno de Unity, siempre incluido en las builds (el mismo que usan las líneas GL).
            Material mat = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave,
                color = color,
                renderQueue = OreHitboxVisualizerPlugin.ShowThroughWalls.Value ? renderQueue + 1000 : renderQueue
            };
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_ZTest", (int)(OreHitboxVisualizerPlugin.ShowThroughWalls.Value ? CompareFunction.Always : CompareFunction.LessEqual));
            return mat;
        }

        private static void DestroyMaterial(ref Material mat)
        {
            if (mat != null)
            {
                Object.Destroy(mat);
            }
            mat = null;
        }

        /// <summary>Cubo de lado 1 centrado en el origen, con colores de vértice blancos (el shader multiplica por ellos).</summary>
        private static Mesh BuildUnitCube()
        {
            Vector3[] vertices =
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
            };
            int[] triangles =
            {
                0, 2, 1, 0, 3, 2, // -z
                4, 5, 6, 4, 6, 7, // +z
                0, 1, 5, 0, 5, 4, // -y
                3, 6, 2, 3, 7, 6, // +y
                0, 4, 7, 0, 7, 3, // -x
                1, 2, 6, 1, 6, 5  // +x
            };
            Color[] colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Color.white;
            }
            Mesh mesh = new Mesh { name = "OreHitbox_Cube", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Las 12 aristas de la caja como prismas finos, en coordenadas de mundo.</summary>
        internal static Mesh BuildEdgeMesh(Bounds bounds, float thickness)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3 center = bounds.center;
            Vector3 size = bounds.size;
            CombineInstance[] edges = new CombineInstance[12];
            int k = 0;
            float[] xs = { min.x, max.x };
            float[] ys = { min.y, max.y };
            float[] zs = { min.z, max.z };

            foreach (float a in ys)
            {
                foreach (float b in zs)
                {
                    edges[k++] = Edge(new Vector3(center.x, a, b), new Vector3(size.x + thickness, thickness, thickness));
                }
            }
            foreach (float a in xs)
            {
                foreach (float b in zs)
                {
                    edges[k++] = Edge(new Vector3(a, center.y, b), new Vector3(thickness, size.y + thickness, thickness));
                }
            }
            foreach (float a in xs)
            {
                foreach (float b in ys)
                {
                    edges[k++] = Edge(new Vector3(a, b, center.z), new Vector3(thickness, thickness, size.z + thickness));
                }
            }

            Mesh mesh = new Mesh { name = "OreHitbox_Edges", hideFlags = HideFlags.HideAndDontSave };
            mesh.CombineMeshes(edges, true, true);
            return mesh;
        }

        private static CombineInstance Edge(Vector3 center, Vector3 size)
        {
            return new CombineInstance
            {
                mesh = CubeMesh,
                transform = Matrix4x4.TRS(center, Quaternion.identity, size)
            };
        }
    }

    /// <summary>Dibujo de las hitboxes de un depósito concreto.</summary>
    internal class RockVisual
    {
        public float LastHitTime;

        private readonly MineRock5 m_rock;
        private readonly GameObject m_root;
        private AreaVisual[] m_areas;
        private bool[] m_alive;
        private Bounds[] m_bounds;
        private bool[] m_red;

        public RockVisual(MineRock5 rock, Transform parent)
        {
            m_rock = rock;
            m_root = new GameObject("OreHitbox_" + rock.name);
            m_root.transform.SetParent(parent, false);
        }

        public void Refresh()
        {
            List<MineRock5.HitArea> hitAreas = m_rock.m_hitAreas;
            int count = hitAreas.Count;
            if (m_areas == null || m_areas.Length != count)
            {
                DestroyAreas();
                m_areas = new AreaVisual[count];
                m_alive = new bool[count];
                m_bounds = new Bounds[count];
                m_red = new bool[count];
            }

            for (int i = 0; i < count; i++)
            {
                MineRock5.HitArea area = hitAreas[i];
                m_alive[i] = area.m_health > 0f && area.m_collider != null && area.m_collider.gameObject.activeInHierarchy;
                if (m_alive[i])
                {
                    m_bounds[i] = area.m_collider.bounds;
                }
            }

            HitboxManager.ComputeStates(m_rock, m_alive, m_bounds, m_red);

            for (int i = 0; i < count; i++)
            {
                if (!m_alive[i])
                {
                    if (m_areas[i] != null)
                    {
                        m_areas[i].Destroy();
                        m_areas[i] = null;
                    }
                    continue;
                }
                if (m_areas[i] == null || m_areas[i].Bounds != m_bounds[i])
                {
                    m_areas[i]?.Destroy();
                    m_areas[i] = new AreaVisual(m_root.transform, m_bounds[i]);
                }
                m_areas[i].SetColliding(m_red[i]);
            }
        }

        public void DestroyAreas()
        {
            if (m_areas == null)
            {
                return;
            }
            for (int i = 0; i < m_areas.Length; i++)
            {
                m_areas[i]?.Destroy();
                m_areas[i] = null;
            }
        }

        public void Destroy()
        {
            DestroyAreas();
            if (m_root != null)
            {
                Object.Destroy(m_root);
            }
        }
    }

    /// <summary>Aristas + relleno semitransparente de la caja de un trozo.</summary>
    internal class AreaVisual
    {
        public readonly Bounds Bounds;

        private readonly GameObject m_edges;
        private readonly Mesh m_edgeMesh;
        private readonly MeshRenderer m_edgeRenderer;
        private readonly GameObject m_fill;
        private readonly MeshRenderer m_fillRenderer;
        private bool? m_colliding;

        public AreaVisual(Transform parent, Bounds bounds)
        {
            Bounds = bounds;

            m_edgeMesh = HitboxManager.BuildEdgeMesh(bounds, Mathf.Max(0.001f, OreHitboxVisualizerPlugin.LineThickness.Value));
            m_edges = new GameObject("Edges");
            m_edges.transform.SetParent(parent, false);
            m_edges.AddComponent<MeshFilter>().sharedMesh = m_edgeMesh;
            m_edgeRenderer = SetupRenderer(m_edges.AddComponent<MeshRenderer>());

            if (OreHitboxVisualizerPlugin.ShowFill.Value)
            {
                m_fill = new GameObject("Fill");
                m_fill.transform.SetParent(parent, false);
                m_fill.transform.position = bounds.center;
                m_fill.transform.localScale = bounds.size;
                m_fill.AddComponent<MeshFilter>().sharedMesh = HitboxManager.CubeMesh;
                m_fillRenderer = SetupRenderer(m_fill.AddComponent<MeshRenderer>());
            }
        }

        private static MeshRenderer SetupRenderer(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        public void SetColliding(bool colliding)
        {
            if (m_colliding == colliding)
            {
                return;
            }
            m_colliding = colliding;
            m_edgeRenderer.sharedMaterial = HitboxManager.GetLineMaterial(colliding);
            if (m_fillRenderer != null)
            {
                m_fillRenderer.sharedMaterial = HitboxManager.GetFillMaterial(colliding);
            }
        }

        public void Destroy()
        {
            Object.Destroy(m_edges);
            Object.Destroy(m_edgeMesh);
            if (m_fill != null)
            {
                Object.Destroy(m_fill);
            }
        }
    }
}
