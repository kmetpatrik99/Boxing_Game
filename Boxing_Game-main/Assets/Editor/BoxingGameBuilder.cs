using System.Linq;
using BoxingGame;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

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

        // ---- save + build settings
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Boxing",
            "Done! Scene saved to:\n" + ScenePath + "\n\nPress Play, choose a difficulty and hit Enter.", "OK");
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

    // ------------------------------------------------------------------ helpers

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

    static bool HasAncestorWithPrefix(Transform t, string prefix)
    {
        while (t != null)
        {
            if (t.name.StartsWith(prefix)) return true;
            t = t.parent;
        }
        return false;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }
}
