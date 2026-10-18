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
        private static Material s_depthMask;

        // Orden de dibujado (detrás de los transparentes del juego para no tapar agua ni partículas):
        // 1. aristas rojas: solo las tapan la roca y el terreno, nunca las caras de otras cajas;
        // 2. caras invisibles de todas las cajas, que escriben profundidad;
        // 3. aristas verdes: las tapan la roca, el terreno y las caras de las cajas.
        private const int CollidingLineQueue = 3989;
        private const int DepthMaskQueue = 3990;
        private const int FreeLineQueue = 3991;

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
                // Solo se muestra un depósito a la vez: al picar otro, el anterior desaparece.
                ClearAll();
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

        /// <summary>El mod se descarga (recarga con ScriptEngine): borra todo lo creado.</summary>
        public static void Shutdown()
        {
            ClearAll();
            OnAppearanceChanged();
            if (s_cubeMesh != null)
            {
                Object.Destroy(s_cubeMesh);
                s_cubeMesh = null;
            }
            if (s_root != null)
            {
                Object.Destroy(s_root);
                s_root = null;
            }
        }

        /// <summary>Colores, grosor u opacidad cambiados en la config: se rehacen materiales y mallas.</summary>
        public static void OnAppearanceChanged()
        {
            DestroyMaterial(ref s_lineColliding);
            DestroyMaterial(ref s_lineFree);
            DestroyMaterial(ref s_depthMask);
            foreach (RockVisual visual in s_rocks.Values)
            {
                visual.DestroyAreas();
            }
            s_nextRefresh = 0f;
        }

        // ---------------------------------------------------------------- Recuento de nodos (texto al apuntar)

        private static MineRock5 s_countRock;
        private static float s_countValidUntil;
        private static int s_countTotal;
        private static int s_countColliding;
        private static bool[] s_countAlive;
        private static Bounds[] s_countBounds;
        private static bool[] s_countRed;

        /// <summary>
        /// Trozos vivos del depósito y cuántos están en rojo. El texto se pide cada frame mientras se apunta,
        /// así que el resultado se guarda el mismo intervalo que el refresco de las cajas.
        /// </summary>
        public static void GetNodeCounts(MineRock5 rock, out int total, out int colliding)
        {
            float now = Time.time;
            if (rock == s_countRock && now < s_countValidUntil)
            {
                total = s_countTotal;
                colliding = s_countColliding;
                return;
            }

            int count = rock.m_hitAreas.Count;
            if (s_countAlive == null || s_countAlive.Length != count)
            {
                s_countAlive = new bool[count];
                s_countBounds = new Bounds[count];
                s_countRed = new bool[count];
            }
            CollectAreas(rock, s_countAlive, s_countBounds);
            ComputeStates(rock, s_countAlive, s_countBounds, s_countRed);

            total = 0;
            colliding = 0;
            for (int i = 0; i < count; i++)
            {
                if (s_countAlive[i])
                {
                    total++;
                    if (s_countRed[i])
                    {
                        colliding++;
                    }
                }
            }

            s_countRock = rock;
            s_countValidUntil = now + RefreshInterval;
            s_countTotal = total;
            s_countColliding = colliding;
        }

        // ---------------------------------------------------------------- Colisión

        /// <summary>Qué trozos siguen vivos y la caja de cada uno (collider.bounds, como en MineRock5.SetupColliders).</summary>
        internal static void CollectAreas(MineRock5 rock, bool[] alive, Bounds[] bounds)
        {
            List<MineRock5.HitArea> hitAreas = rock.m_hitAreas;
            for (int i = 0; i < alive.Length; i++)
            {
                MineRock5.HitArea area = hitAreas[i];
                alive[i] = area.m_health > 0f && area.m_collider != null && area.m_collider.gameObject.activeInHierarchy;
                if (alive[i])
                {
                    bounds[i] = area.m_collider.bounds;
                }
            }
        }

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
                    if (!red[i] && IsBelowGround(bounds[i]))
                    {
                        red[i] = true;
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

        /// <summary>
        /// El terreno es una superficie, no un volumen: una caja enterrada del todo no cruza su collider y el OverlapBox
        /// no la detecta. Si la base de la caja queda por debajo del suelo (altura actual del terreno, con lo cavado
        /// o elevado) en el centro o en alguna esquina, está tocando tierra.
        /// </summary>
        private static bool IsBelowGround(Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3 center = bounds.center;
            return BelowGroundAt(center.x, center.z, min.y)
                || BelowGroundAt(min.x, min.z, min.y)
                || BelowGroundAt(min.x, max.z, min.y)
                || BelowGroundAt(max.x, min.z, min.y)
                || BelowGroundAt(max.x, max.z, min.y);
        }

        private static bool BelowGroundAt(float x, float z, float y)
        {
            return Heightmap.GetHeight(new Vector3(x, y, z), out float ground) && y < ground;
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
                // Con HideHiddenEdges las aristas respetan la profundidad (roca, terreno y caras invisibles de las cajas);
                // sin él se dibujan a través de todo.
                CompareFunction zTest = OreHitboxVisualizerPlugin.HideHiddenEdges.Value ? CompareFunction.LessEqual : CompareFunction.Always;
                int queue = colliding ? CollidingLineQueue : FreeLineQueue;
                mat = CreateMaterial(color, queue, zTest, false, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, CullMode.Off);
            }
            return mat;
        }

        /// <summary>
        /// Caras de la caja que no pintan nada (Blend Zero One) pero escriben profundidad, para que tapen
        /// las aristas que quedan detrás de otras cajas. Cull Back: si la cámara está dentro de una caja, esa no tapa nada.
        /// </summary>
        internal static Material GetDepthMaskMaterial()
        {
            if (s_depthMask == null)
            {
                s_depthMask = CreateMaterial(Color.white, DepthMaskQueue, CompareFunction.LessEqual, true, BlendMode.Zero, BlendMode.One, CullMode.Back);
            }
            return s_depthMask;
        }

        private static Material CreateMaterial(Color color, int renderQueue, CompareFunction zTest, bool zWrite,
            BlendMode src, BlendMode dst, CullMode cull)
        {
            // Shader interno de Unity, siempre incluido en las builds (el mismo que usan las líneas GL).
            Material mat = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave,
                color = color,
                renderQueue = renderQueue
            };
            mat.SetInt("_SrcBlend", (int)src);
            mat.SetInt("_DstBlend", (int)dst);
            mat.SetInt("_Cull", (int)cull);
            mat.SetInt("_ZWrite", zWrite ? 1 : 0);
            mat.SetInt("_ZTest", (int)zTest);
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

            HitboxManager.CollectAreas(m_rock, m_alive, m_bounds);
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

    /// <summary>Aristas de la caja de un trozo, más sus caras invisibles que tapan lo que queda detrás.</summary>
    internal class AreaVisual
    {
        public readonly Bounds Bounds;

        private readonly GameObject m_edges;
        private readonly Mesh m_edgeMesh;
        private readonly MeshRenderer m_edgeRenderer;
        private readonly GameObject m_mask;
        private bool? m_colliding;

        public AreaVisual(Transform parent, Bounds bounds)
        {
            Bounds = bounds;
            float thickness = Mathf.Max(0.001f, OreHitboxVisualizerPlugin.LineThickness.Value);

            m_edgeMesh = HitboxManager.BuildEdgeMesh(bounds, thickness);
            m_edges = new GameObject("Edges");
            m_edges.transform.SetParent(parent, false);
            m_edges.AddComponent<MeshFilter>().sharedMesh = m_edgeMesh;
            m_edgeRenderer = SetupRenderer(m_edges.AddComponent<MeshRenderer>());

            if (OreHitboxVisualizerPlugin.HideHiddenEdges.Value)
            {
                // Caras invisibles que solo tapan las aristas de detrás. Un poco más pequeñas que la caja
                // para que no se coman la mitad de sus propias aristas.
                m_mask = new GameObject("DepthMask");
                m_mask.transform.SetParent(parent, false);
                m_mask.transform.position = bounds.center;
                m_mask.transform.localScale = Vector3.Max(bounds.size - Vector3.one * thickness, Vector3.one * 0.001f);
                m_mask.AddComponent<MeshFilter>().sharedMesh = HitboxManager.CubeMesh;
                SetupRenderer(m_mask.AddComponent<MeshRenderer>()).sharedMaterial = HitboxManager.GetDepthMaskMaterial();
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
        }

        public void Destroy()
        {
            Object.Destroy(m_edges);
            Object.Destroy(m_edgeMesh);
            if (m_mask != null)
            {
                Object.Destroy(m_mask);
            }
        }
    }
}
