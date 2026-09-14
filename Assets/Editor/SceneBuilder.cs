using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RedLightQwop.Editor
{
    /// <summary>
    /// Builds the whole Game scene from scratch: course, ragdoll prefab, doll, camera, HUD, and
    /// the GameManager wiring. Re-run it any time to regenerate the scene.
    /// Menu: Red Light Qwop > Build Game Scene.
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Game.unity";
        public const string PrefabPath = "Assets/Prefabs/Ragdoll.prefab";
        public const float CourseLength = 20f;
        const float k_CourseWidth = 10f;

        // x, z offsets from the player's start position.
        static readonly Vector2[] k_NpcSpawns =
        {
            new Vector2(-1.4f, 0.9f), new Vector2(1.4f, 0.9f),
            new Vector2(-2.6f, -0.4f), new Vector2(2.6f, -0.4f),
            new Vector2(-3.8f, 0.6f), new Vector2(3.8f, 0.6f),
            new Vector2(-1.2f, -1.4f), new Vector2(1.2f, -1.4f),
            new Vector2(-3.2f, 1.8f), new Vector2(3.2f, 1.8f),
        };

        [MenuItem("Red Light Qwop/Build Game Scene")]
        public static void BuildFromMenu() => Build();

        /// <summary>
        /// Schedules the build on the next Editor tick and returns immediately. Use this from
        /// remote eval, whose main-thread slot is short. Watch the console for "[SceneBuilder] Built".
        /// </summary>
        public static string BuildAsync()
        {
            EditorApplication.delayCall += () =>
            {
                try { Build(); }
                catch (System.Exception e) { Debug.LogException(e); }
            };
            return "scheduled";
        }

        public static string Build()
        {
            EnsureFolders();

            // Open the new scene additively and drop the others without saving, so no
            // "save changes?" dialog can ever block the Editor's main thread.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                var other = SceneManager.GetSceneAt(i);
                if (other != scene) EditorSceneManager.CloseScene(other, true);
            }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var groundMat = Mat(lit, "Ground", new Color(0.52f, 0.62f, 0.42f));
            var lineMat = Mat(lit, "Line", Color.white);
            var bodyMat = Mat(lit, "Body", new Color(0.25f, 0.55f, 0.85f));
            var accentMat = Mat(lit, "BodyAccent", new Color(0.95f, 0.55f, 0.15f));
            var dollSkinMat = Mat(lit, "DollSkin", new Color(0.95f, 0.85f, 0.7f));
            var dollDressMat = Mat(lit, "DollDress", new Color(0.95f, 0.45f, 0.1f));
            var faceMat = Mat(lit, "DollFace", new Color(0.1f, 0.1f, 0.1f));
            var npcBodyMat = Mat(lit, "NpcBody", new Color(0.35f, 0.7f, 0.55f));
            var npcAccentMat = Mat(lit, "NpcAccent", new Color(0.85f, 0.8f, 0.35f));
            var indicatorMat = Mat(lit, "Indicator", new Color(0.2f, 0.9f, 0.3f));
            indicatorMat.EnableKeyword("_EMISSION");
            indicatorMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            indicatorMat.SetColor("_EmissionColor", new Color(0.2f, 0.9f, 0.3f) * 2f);

            var footPhys = PhysMat("Foot", 1.0f, 1.0f);
            var groundPhys = PhysMat("Ground", 0.9f, 0.9f);

            // --- Environment ---------------------------------------------------------------
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.65f);
            RenderSettings.fog = false;

            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = Primitive("Ground", PrimitiveType.Cube, new Vector3(0f, -0.5f, CourseLength * 0.5f),
                new Vector3(k_CourseWidth + 4f, 1f, CourseLength + 24f), groundMat);
            ground.GetComponent<Collider>().sharedMaterial = groundPhys;

            Primitive("StartLine", PrimitiveType.Cube, new Vector3(0f, 0.005f, 0f), new Vector3(k_CourseWidth, 0.01f, 0.15f), lineMat);
            for (float z = 5f; z < CourseLength; z += 5f)
            {
                Primitive($"Marker_{z:0}m", PrimitiveType.Cube, new Vector3(0f, 0.005f, z), new Vector3(k_CourseWidth, 0.01f, 0.06f), lineMat);
            }
            Primitive("FinishLine", PrimitiveType.Cube, new Vector3(0f, 0.005f, CourseLength), new Vector3(k_CourseWidth, 0.01f, 0.25f), lineMat);
            Primitive("FinishPostLeft", PrimitiveType.Cube, new Vector3(-k_CourseWidth * 0.5f, 1f, CourseLength), new Vector3(0.15f, 2f, 0.15f), lineMat);
            Primitive("FinishPostRight", PrimitiveType.Cube, new Vector3(k_CourseWidth * 0.5f, 1f, CourseLength), new Vector3(0.15f, 2f, 0.15f), lineMat);

            var finishTrigger = new GameObject("FinishTrigger");
            finishTrigger.transform.position = new Vector3(0f, 1.5f, CourseLength + 0.3f);
            var triggerCol = finishTrigger.AddComponent<BoxCollider>();
            triggerCol.isTrigger = true;
            triggerCol.size = new Vector3(k_CourseWidth, 3f, 0.6f);
            var finish = finishTrigger.AddComponent<FinishLine>();

            // --- Player --------------------------------------------------------------------
            var ragdollSource = RagdollFactory.Build(bodyMat, accentMat, footPhys);
            var prefab = PrefabUtility.SaveAsPrefabAsset(ragdollSource, PrefabPath);
            Object.DestroyImmediate(ragdollSource);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 0.02f, 0f);
            var ragdoll = player.GetComponent<Ragdoll>();
            var controller = player.GetComponent<LimbController>();

            int playerLayer = EnsureLayer(GameManager.PlayerLayerName, 8);
            int npcLayer = EnsureLayer(GameManager.NpcLayerName, 9);
            ragdoll.SetLayer(playerLayer);
            Physics.IgnoreLayerCollision(npcLayer, npcLayer, true);
            Physics.IgnoreLayerCollision(npcLayer, playerLayer, true);

            // --- NPC runners, spread around the player ------------------------------------
            var npcRoot = new GameObject("Runners");
            for (int i = 0; i < k_NpcSpawns.Length; i++)
            {
                var npcGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                npcGo.name = $"Runner_{i + 1}";
                npcGo.transform.SetParent(npcRoot.transform, true);
                npcGo.transform.position = new Vector3(k_NpcSpawns[i].x, 0.02f, k_NpcSpawns[i].y);
                var npcRagdoll = npcGo.GetComponent<Ragdoll>();
                npcRagdoll.SetLayer(npcLayer);
                foreach (var r in npcGo.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.sharedMaterial == bodyMat) r.sharedMaterial = npcBodyMat;
                    else if (r.sharedMaterial == accentMat) r.sharedMaterial = npcAccentMat;
                }
                var brain = npcGo.AddComponent<NpcBrain>();
                brain.Ragdoll = npcRagdoll;
                brain.Controller = npcGo.GetComponent<LimbController>();
                brain.Controller.UseKeyboard = false;
                brain.StartDelay = 0.2f + 0.1f * (i % 5);
                brain.StepTime = 0.19f + 0.01f * (i % 4);
            }

            // --- Doll ----------------------------------------------------------------------
            var dollRoot = new GameObject("TrafficDoll");
            dollRoot.transform.position = new Vector3(0f, 0f, CourseLength + 4f);
            var doll = dollRoot.AddComponent<TrafficDoll>();

            var dollBody = new GameObject("Body");
            dollBody.transform.SetParent(dollRoot.transform, false);
            Primitive("Dress", PrimitiveType.Cylinder, new Vector3(0f, 1f, 0f), new Vector3(0.7f, 1f, 0.7f), dollDressMat, dollBody.transform, false);
            Primitive("Head", PrimitiveType.Sphere, new Vector3(0f, 2.45f, 0f), Vector3.one * 0.8f, dollSkinMat, dollBody.transform, false);
            Primitive("Face", PrimitiveType.Cube, new Vector3(0f, 2.5f, 0.36f), new Vector3(0.35f, 0.12f, 0.1f), faceMat, dollBody.transform, false);
            Primitive("Hair", PrimitiveType.Cube, new Vector3(0f, 2.7f, -0.1f), new Vector3(0.7f, 0.35f, 0.6f), faceMat, dollBody.transform, false);
            doll.Body = dollBody.transform;

            var indicator = Primitive("Indicator", PrimitiveType.Sphere, new Vector3(0f, 3.6f, 0f), Vector3.one * 0.6f, indicatorMat, dollRoot.transform, false);
            doll.Indicator = indicator.GetComponent<Renderer>();
            var indicatorLight = new GameObject("IndicatorLight").AddComponent<Light>();
            indicatorLight.transform.SetParent(indicator.transform, false);
            indicatorLight.type = LightType.Point;
            indicatorLight.range = 12f;
            indicatorLight.intensity = 4f;
            doll.IndicatorLight = indicatorLight;

            // --- Camera --------------------------------------------------------------------
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
            cam.fieldOfView = 55f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<CinemachineBrain>();
            camGo.transform.position = new Vector3(0f, 2.5f, -5f);

            var vcamGo = new GameObject("FollowCamera");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = ragdoll.Pelvis.transform;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(2.5f, 1.8f, -4.5f);
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = new Vector3(0.6f, 0.6f, 0.6f);
            var composer = vcamGo.AddComponent<CinemachineRotationComposer>();
            composer.Damping = new Vector2(0.4f, 0.4f);

            // --- HUD -----------------------------------------------------------------------
            var canvasGo = new GameObject("HUD", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var hud = canvasGo.AddComponent<GameHud>();

            hud.PhaseText = MakeText(canvasGo.transform, "PhaseText", new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(900f, 90f), 64, TextAnchor.MiddleCenter, FontStyle.Bold);
            hud.InfoText = MakeText(canvasGo.transform, "InfoText", new Vector2(0.5f, 1f), new Vector2(0f, -115f), new Vector2(900f, 50f), 30, TextAnchor.MiddleCenter, FontStyle.Normal);
            hud.CenterText = MakeText(canvasGo.transform, "CenterText", new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1200f, 220f), 72, TextAnchor.MiddleCenter, FontStyle.Bold);
            hud.HintText = MakeText(canvasGo.transform, "HintText", new Vector2(0.5f, 0f), new Vector2(0f, 35f), new Vector2(1200f, 40f), 26, TextAnchor.MiddleCenter, FontStyle.Normal);
            hud.HintText.color = new Color(1f, 1f, 1f, 0.8f);

            // --- Game manager --------------------------------------------------------------
            var gameGo = new GameObject("GameManager");
            var game = gameGo.AddComponent<GameManager>();
            game.Player = ragdoll;
            game.Controller = controller;
            game.Doll = doll;
            game.Hud = hud;
            game.FinishLine = finishTrigger.transform;
            finish.Game = game;

            // --- Save ----------------------------------------------------------------------
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SceneBuilder] Built {ScenePath} and {PrefabPath}.");
            return ScenePath;
        }

        // ---------------------------------------------------------------------------------------

        /// <summary>Returns the index of a named user layer, creating it in TagManager if needed.</summary>
        static int EnsureLayer(string name, int preferredIndex)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = Mathf.Max(8, preferredIndex); i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = name;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }
            throw new System.InvalidOperationException($"No free user layer for '{name}'.");
        }

        static void EnsureFolders()
        {
            foreach (var folder in new[] { "Assets/Scenes", "Assets/Prefabs", "Assets/Materials" })
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
                }
            }
        }

        static Material Mat(Shader shader, string name, Color color)
        {
            string path = $"Assets/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static PhysicsMaterial PhysMat(string name, float dynamicFriction, float staticFriction)
        {
            string path = $"Assets/Materials/{name}Physics.asset";
            var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (pm == null)
            {
                pm = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(pm, path);
            }
            pm.dynamicFriction = dynamicFriction;
            pm.staticFriction = staticFriction;
            pm.frictionCombine = PhysicsMaterialCombine.Maximum;
            pm.bounciness = 0f;
            EditorUtility.SetDirty(pm);
            return pm;
        }

        static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Transform parent = null, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor alignment, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = anchoredPosition;
            rt.sizeDelta = size;

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = Color.white;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        static void AddSceneToBuild(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
