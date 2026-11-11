using System.Collections.Generic;
using System.Linq;
using BoxingGame;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One-click builder:  menu  Boxing > Build Game Scene
///  - builds an Animator Controller from the Kairo animation clips
///  - copies the arena demo scene to Assets/Scenes/BoxingGame.unity
///  - drops in the player, the AI opponent, camera and game manager
/// </summary>
public static class BoxingGameBuilder
{
    const string ArenaScene = "Assets/MarpaStudio/Scene/DemoScene.unity";
    const string KairoRoot = "Assets/Rallba/Kairo The Boxer - Hyper Casual Cartoon Boxer";
    const string CharacterPrefab = KairoRoot + "/Prefabs/Kairo The Boxer.prefab";
    const string CharacterFbx = KairoRoot + "/Meshes/Kairo The Boxer.fbx";
    const string AnimFolder = KairoRoot + "/Animations";

    const string OutFolder = "Assets/Boxing";
    const string ControllerPath = OutFolder + "/BoxerAnimator.controller";
    const string ScenePath = "Assets/Scenes/BoxingGame.unity";

    const float TargetHeight = 1.85f;

    [MenuItem("Boxing/Build Game Scene")]
    // Funkcija Build: podrobnosti so opisane v komentarjih in navodilih projekta.
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ArenaScene) == null)
        {
            EditorUtility.DisplayDialog("Boxing", "Arena scene not found at:\n" + ArenaScene, "OK");
            return;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefab);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Boxing", "Character prefab not found at:\n" + CharacterPrefab, "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder("Assets", "Boxing");
        EnsureFolder("Assets/Boxing", "Materials");
        EnsureFolder("Assets", "Scenes");

        var controller = BuildController();

        var scene = EditorSceneManager.OpenScene(ArenaScene, OpenSceneMode.Single);

        // remove the demo camera(s); we add our own
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            Object.DestroyImmediate(cam.gameObject);

        // ---- remember the classic arena's environment, then group everything it contains under "Map_Classic"
        var classic = new MapDefinition
        {
            name = "Classic Arena",
            subtitle = "Bright boxing ring",
            applyEnvironment = true,
            skyboxClear = true,
            skybox = RenderSettings.skybox,
            ambientMode = RenderSettings.ambientMode,
            ambientColor = RenderSettings.ambientLight,
            fog = RenderSettings.fog,
            fogMode = RenderSettings.fogMode,
            fogColor = RenderSettings.fogColor,
            fogDensity = RenderSettings.fogDensity,
            fogStart = RenderSettings.fogStartDistance,
            fogEnd = RenderSettings.fogEndDistance,
            cameraBackground = new Color(0.19f, 0.30f, 0.47f),
        };
        var classicRoot = new GameObject("Map_Classic");
        foreach (var g in scene.GetRootGameObjects())
            if (g != classicRoot) g.transform.SetParent(classicRoot.transform, true);
        classic.root = classicRoot;

        // ---- ring measurements
        float groundY = 0f;
        Vector2 center = Vector2.zero;
        Vector2 half = new Vector2(2.4f, 2.4f);

        Bounds fb;
        if (TryGetBounds("RingFloor", out fb))
        {
            groundY = fb.max.y;
            center = new Vector2(fb.center.x, fb.center.z);
        }
        else Debug.LogWarning("[Boxing] Could not find 'RingFloor' in the arena scene; using ground height 0.");

        Bounds cb;
        if (TryGetBounds("Corner", out cb))
        {
            center = new Vector2(cb.center.x, cb.center.z);
            half = new Vector2(Mathf.Clamp(cb.extents.x - 0.6f, 1.5f, 3.5f), Mathf.Clamp(cb.extents.z - 0.6f, 1.5f, 3.5f));
        }
        Debug.Log("[Boxing] Ring: ground Y = " + groundY + ", centre = " + center + ", half size = " + half);

        Vector3 c = new Vector3(center.x, groundY, center.y);
        Vector3 playerPos = c + new Vector3(0f, 0f, -1.2f);
        Vector3 oppPos = c + new Vector3(0f, 0f, 1.2f);

        classic.center = c;
        classic.halfSize = half;
        classic.radius = 0f;
        classic.groundY = groundY;

        // ---- second map: octagon cage
        var cage = BuildCageMap();

        // ---- fighters
        var player = CreateFighter(prefab, "Player", "YOU", playerPos, Quaternion.LookRotation(Vector3.forward), controller, true);
        var opponent = CreateFighter(prefab, "Opponent", "CPU", oppPos, Quaternion.LookRotation(Vector3.back), controller, false);
        TintOpponent(opponent.gameObject);
        player.opponent = opponent;
        opponent.opponent = player;

        // ---- camera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var camera = camGo.AddComponent<Camera>();
        camera.fieldOfView = 55f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 300f;
        camGo.AddComponent<AudioListener>();
        var bc = camGo.AddComponent<BoxingCamera>();
        bc.player = player.transform;
        bc.opponent = opponent.transform;
        bc.groundY = groundY;
        camGo.transform.position = playerPos + new Vector3(0.9f, 1.9f, -3f);
        camGo.transform.LookAt(c + Vector3.up * 1.15f);

        // ---- game manager
        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gm.player = player;
        gm.opponent = opponent;
        gm.ai = opponent.GetComponent<BoxerAI>();
        gm.ringCenter = c;
        gm.ringHalfSize = half;
        gm.groundY = groundY;
        gm.maps = new[] { classic, cage };
        gm.mapIndex = 0;
        gm.difficultyIndex = 1;
        cage.root.SetActive(false);   // GameManager switches maps at runtime

        // ---- save + build settings
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Boxing",
            "Done! Scene saved to:\n" + ScenePath + "\n\nPress Play, choose a difficulty and an arena, then hit Enter.", "OK");
    }

    // ------------------------------------------------------------------ animator controller

    static AnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;

        var guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimFolder });
        var paths = guids.Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p).ToArray();

        AnimatorState idle = null;
        foreach (var path in paths)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null || clip.name.Contains("T-Pose")) continue;

            string stateName = clip.name.StartsWith("Kairo_") ? clip.name.Substring(6) : clip.name;
            var state = sm.AddState(stateName);
            state.motion = clip;
            if (stateName == "Idle") idle = state;

            bool loop = stateName.StartsWith("Idle") || stateName.StartsWith("Walk") || stateName.StartsWith("Run") || stateName == "Stunned";
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.loopTime != loop)
            {
                settings.loopTime = loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
            }
        }
        if (idle != null) sm.defaultState = idle;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[Boxing] Animator controller built with " + sm.states.Length + " states.");
        return controller;
    }

    // ------------------------------------------------------------------ fighters

    static Fighter CreateFighter(GameObject prefab, string objName, string displayName, Vector3 pos, Quaternion rot,
                                 RuntimeAnimatorController controller, bool isPlayer)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = objName;
        go.transform.SetPositionAndRotation(pos, rot);

        var anim = go.GetComponentInChildren<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        if (anim.avatar == null)
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterFbx).OfType<Avatar>().FirstOrDefault();
            if (avatar != null) anim.avatar = avatar;
            else Debug.LogWarning("[Boxing] No Avatar found on the character FBX.");
        }
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;

        // normalise the character's height so punch ranges feel right
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float h = b.size.y;
            Debug.Log("[Boxing] " + objName + " model height = " + h.ToString("F2") + ", feet offset = " + (b.min.y - pos.y).ToString("F2"));
            if (h > 0.1f && Mathf.Abs(h - TargetHeight) > 0.15f * TargetHeight)
                go.transform.localScale *= TargetHeight / h;
        }

        var f = go.AddComponent<Fighter>();
        f.displayName = displayName;
        if (isPlayer) go.AddComponent<PlayerController>();
        else go.AddComponent<BoxerAI>();
        return f;
    }

    // Funkcija TintOpponent: podrobnosti so opisane v komentarjih in navodilih projekta.
    static void TintOpponent(GameObject go)
    {
        var blue = new Color(0.15f, 0.35f, 0.95f, 1f);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                string n = m.name.ToLowerInvariant();
                if (!(n.Contains("glove") || n.Contains("underwear"))) continue;

                string path = OutFolder + "/Materials/Opponent_" + m.name + ".mat";
                var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (copy == null)
                {
                    copy = new Material(m) { name = "Opponent_" + m.name };
                    if (copy.HasProperty("_BaseMap")) copy.SetTexture("_BaseMap", null);
                    if (copy.HasProperty("_MainTex")) copy.SetTexture("_MainTex", null);
                    if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", blue);
                    if (copy.HasProperty("_Color")) copy.SetColor("_Color", blue);
                    AssetDatabase.CreateAsset(copy, path);
                }
                mats[i] = copy;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    // ------------------------------------------------------------------ octagon cage map

    // Funkcija BuildCageMap: podrobnosti so opisane v komentarjih in navodilih projekta.
    static MapDefinition BuildCageMap()
    {
        var root = new GameObject("Map_Cage");
        Transform rt = root.transform;

        var steel   = LitMat("Cage_Steel",   new Color(0.13f, 0.13f, 0.15f), 0.65f, 0.8f, null);
        var canvas  = LitMat("Cage_Canvas",  new Color(0.10f, 0.13f, 0.24f), 0.25f, 0.0f, null);
        var apronM  = LitMat("Cage_Apron",   new Color(0.06f, 0.06f, 0.08f), 0.20f, 0.0f, null);
        var groundM = LitMat("Cage_Ground",  new Color(0.04f, 0.04f, 0.05f), 0.10f, 0.0f, null);
        var logoRed = LitMat("Cage_Logo",    new Color(0.80f, 0.10f, 0.10f), 0.30f, 0.0f, new Color(1f, 0.10f, 0.10f) * 1.1f);
        var padRed  = LitMat("Cage_Padding", new Color(0.65f, 0.08f, 0.08f), 0.30f, 0.0f, null);

        const float R = 4.55f;                                   // distance to the octagon's corners
        float apothem = R * Mathf.Cos(22.5f * Mathf.Deg2Rad);    // distance to the middle of each wall
        const float H = 2.3f;                                    // fence height

        // dark floor all around
        Prim(PrimitiveType.Cube, "Ground", rt, new Vector3(0f, -1.1f, 0f), new Vector3(90f, 0.2f, 90f), groundM, null);

        // raised platform (apron) + fighting canvas
        Mesh apronMesh = OctagonPrism("CageApronMesh", R + 0.75f, 0.9f);
        Mesh floorMesh = OctagonPrism("CageFloorMesh", R, 0.9f);
        MeshObj("Apron", rt, new Vector3(0f, -0.03f, 0f), apronMesh, apronM);
        MeshObj("Canvas", rt, Vector3.zero, floorMesh, canvas);

        // centre logo: red ring
        Prim(PrimitiveType.Cylinder, "LogoOuter", rt, new Vector3(0f, 0.010f, 0f), new Vector3(3.4f, 0.004f, 3.4f), logoRed, null);
        Prim(PrimitiveType.Cylinder, "LogoInner", rt, new Vector3(0f, 0.016f, 0f), new Vector3(3.0f, 0.004f, 3.0f), canvas, null);

        // fence
        var corners = new Vector3[8];
        for (int k = 0; k < 8; k++)
        {
            float ang = (22.5f + 45f * k) * Mathf.Deg2Rad;
            corners[k] = new Vector3(Mathf.Cos(ang) * R, 0f, Mathf.Sin(ang) * R);
        }
        for (int k = 0; k < 8; k++)
        {
            Vector3 a = corners[k], b = corners[(k + 1) % 8];
            Vector3 e = b - a;
            float len = e.magnitude;
            Vector3 dir = e / len;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 mid = (a + b) * 0.5f;

            Prim(PrimitiveType.Cube, "RailBottom", rt, mid + Vector3.up * 0.25f, new Vector3(0.05f, 0.05f, len), steel, rot);
            Prim(PrimitiveType.Cube, "RailMid",    rt, mid + Vector3.up * 1.15f, new Vector3(0.05f, 0.05f, len), steel, rot);
            Prim(PrimitiveType.Cube, "RailTop",    rt, mid + Vector3.up * H,     new Vector3(0.10f, 0.10f, len), padRed, rot);

            int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.32f));
            for (int j = 1; j < n; j++)
            {
                Vector3 pos = a + dir * (len * j / n) + Vector3.up * (H * 0.5f);
                Prim(PrimitiveType.Cube, "Bar", rt, pos, new Vector3(0.03f, H, 0.03f), steel, null);
            }
        }
        for (int k = 0; k < 8; k++)
            Prim(PrimitiveType.Cube, "Post", rt, corners[k] + Vector3.up * (H * 0.5f + 0.05f), new Vector3(0.18f, H + 0.1f, 0.18f), padRed, null);

        // lighting: soft fill + four spotlights from above
        var lights = new GameObject("Lights");
        lights.transform.SetParent(rt, false);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(lights.transform, false);
        fill.transform.rotation = Quaternion.Euler(55f, 30f, 0f);
        var fl = fill.AddComponent<Light>();
        fl.type = LightType.Directional;
        fl.color = new Color(0.65f, 0.72f, 1f);
        fl.intensity = 0.35f;
        fl.shadows = LightShadows.None;

        Vector3[] spots = { new Vector3(3f, 7.5f, 3f), new Vector3(-3f, 7.5f, 3f), new Vector3(3f, 7.5f, -3f), new Vector3(-3f, 7.5f, -3f) };
        for (int i = 0; i < spots.Length; i++)
        {
            var g = new GameObject("Spot " + (i + 1));
            g.transform.SetParent(lights.transform, false);
            g.transform.position = spots[i];
            g.transform.LookAt(new Vector3(0f, 0.8f, 0f));
            var l = g.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = new Color(1f, 0.95f, 0.85f);
            l.intensity = 6f;
            l.range = 18f;
            l.spotAngle = 75f;
            l.shadows = i == 0 ? LightShadows.Soft : LightShadows.None;
        }

        return new MapDefinition
        {
            name = "Octagon Cage",
            subtitle = "Underground cage fight",
            root = root,
            center = Vector3.zero,
            groundY = 0f,
            radius = apothem - 0.55f,
            halfSize = new Vector2(3.5f, 3.5f),
            applyEnvironment = true,
            skyboxClear = false,
            skybox = null,
            ambientMode = AmbientMode.Flat,
            ambientColor = new Color(0.30f, 0.31f, 0.38f),
            fog = true,
            fogMode = FogMode.ExponentialSquared,
            fogColor = new Color(0.03f, 0.03f, 0.05f),
            fogDensity = 0.03f,
            cameraBackground = new Color(0.02f, 0.02f, 0.04f),
        };
    }

    // Funkcija LitMat: 
    static Material LitMat(string name, Color color, float smoothness, float metallic, Color? emission)
    {
        string path = OutFolder + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission.Value);
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    // Funkcija Prim: 
    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        var col = g.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        if (rot.HasValue) g.transform.localRotation = rot.Value;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g;
    }

    // Funkcija MeshObj:
    static GameObject MeshObj(string name, Transform parent, Vector3 pos, Mesh mesh, Material mat)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return g;
    }

    /// <summary>Flat-topped 8-sided prism: top face at y = 0, sides going down by 'height'. Saved as an asset.</summary>
    static Mesh OctagonPrism(string name, float radius, float height)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        const int n = 8;
        float off = 22.5f * Mathf.Deg2Rad;

        // top fan
        int c = verts.Count;
        verts.Add(Vector3.zero); norms.Add(Vector3.up); uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < n; i++)
        {
            float ang = off + i * Mathf.PI * 2f / n;
            verts.Add(new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius));
            norms.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
        }
        for (int i = 0; i < n; i++)
        {
            tris.Add(c);
            tris.Add(c + 1 + (i + 1) % n);
            tris.Add(c + 1 + i);
        }

        // sides
        for (int i = 0; i < n; i++)
        {
            float a0 = off + i * Mathf.PI * 2f / n;
            float a1 = off + (i + 1) * Mathf.PI * 2f / n;
            Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
            Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
            Vector3 nrm = ((p0 + p1) * 0.5f).normalized;
            int s0 = verts.Count;
            verts.Add(p0);                          // top-left
            verts.Add(p1);                          // top-right
            verts.Add(p1 + Vector3.down * height);  // bottom-right
            verts.Add(p0 + Vector3.down * height);  // bottom-left
            for (int k = 0; k < 4; k++) norms.Add(nrm);
            uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0f, 0f));
            tris.Add(s0); tris.Add(s0 + 1); tris.Add(s0 + 2);
            tris.Add(s0); tris.Add(s0 + 2); tris.Add(s0 + 3);
        }

        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        string path = OutFolder + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // ------------------------------------------------------------------ helpers

    // Funkcija TryGetBounds: 
    static bool TryGetBounds(string namePrefix, out Bounds bounds)
    {
        bounds = new Bounds();
        bool any = false;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!HasAncestorWithPrefix(r.transform, namePrefix)) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    // Funkcija HasAncestorWithPrefix: 
    static bool HasAncestorWithPrefix(Transform t, string prefix)
    {
        while (t != null)
        {
            if (t.name.StartsWith(prefix)) return true;
            t = t.parent;
        }
        return false;
    }

    // Funkcija EnsureFolder: podrobnosti so opisane v komentarjih in navodilih projekta.
    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }
}