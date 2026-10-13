using System.IO;
using System.Linq;
using TCG.Client.Battle;
using TCG.Client.Flow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace TCG.EditorTools
{
    /// <summary>
    /// 코드로 하는 프로젝트 준비 작업. 메뉴(TCG/Setup) 또는 명령줄:
    /// Unity -batchmode -quit -projectPath ... -executeMethod TCG.EditorTools.ProjectSetup.SetupAll
    /// </summary>
    public static class ProjectSetup
    {
        public const string TitleScenePath = "Assets/00_Scenes/Title.unity";
        public const string BattleScenePath = "Assets/00_Scenes/Battle.unity";
        const string MaterialsFolder = "Assets/03_Datas/Resources/Materials";

        [MenuItem("TCG/Setup/All")]
        public static void SetupAll()
        {
            ImportTmpEssentials();
            CreateBaseMaterials();
            BuildBattleScene();
            BuildTitleScene();
        }

        /// <summary>
        /// TextMeshPro 기본 리소스(셰이더·설정)를 Assets/TextMesh Pro로 가져온다. 이미 있으면 건너뛴다.
        /// 주의: batchmode에서 -quit와 함께 실행하면 가져오기가 끝나기 전에 종료될 수 있다 (저장소에는 이미 포함되어 있음).
        /// </summary>
        [MenuItem("TCG/Setup/Import TMP Essentials")]
        public static void ImportTmpEssentials()
        {
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length > 0)
            {
                Debug.Log("[Setup] TMP Essentials already imported.");
                return;
            }

            var package = Directory.GetDirectories("Library/PackageCache", "com.unity.ugui@*")
                .Select(dir => Path.Combine(dir, "Package Resources", "TMP Essential Resources.unitypackage"))
                .FirstOrDefault(File.Exists);
            if (package == null)
            {
                Debug.LogError("[Setup] TMP Essential Resources.unitypackage not found.");
                return;
            }

            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh();
            Debug.Log("[Setup] Imported TMP Essentials.");
        }

        /// <summary>런타임에 색만 바꿔 쓰는 기본 머티리얼 (빌드에 URP 셰이더가 포함되도록 에셋으로 둔다).</summary>
        [MenuItem("TCG/Setup/Create Base Materials")]
        public static void CreateBaseMaterials()
        {
            Directory.CreateDirectory(MaterialsFolder);
            CreateMaterial("BaseLit", "Universal Render Pipeline/Lit");
            CreateMaterial("BaseUnlit", "Universal Render Pipeline/Unlit");
            AssetDatabase.SaveAssets();
        }

        static void CreateMaterial(string name, string shaderName)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                return;
            var material = new Material(Shader.Find(shaderName)) { name = name };
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, path);
            Debug.Log($"[Setup] Created {path}");
        }

        /// <summary>Battle 장면: 카메라 + 조명 + BattleBootstrap. 빌드 목록에서 Title 다음에 둔다.</summary>
        [MenuItem("TCG/Setup/Build Battle Scene")]
        public static void BuildBattleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            camera.fieldOfView = 36f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraGo.transform.position = new Vector3(0f, 15.5f, -11.2f);
            cameraGo.transform.LookAt(new Vector3(0f, 0f, -0.9f));
            cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraGo.AddComponent<AudioListener>();

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(55f, -25f, 0f);

            var bootstrapGo = new GameObject("Battle");
            var bootstrap = bootstrapGo.AddComponent<BattleBootstrap>();
            var so = new SerializedObject(bootstrap);
            so.FindProperty("_camera").objectReferenceValue = camera;
            so.ApplyModifiedPropertiesWithoutUndo();

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            EditorSceneManager.SaveScene(scene, BattleScenePath);
            RegisterScenes();
            Debug.Log($"[Setup] Built {BattleScenePath}");
        }

        /// <summary>Title 장면(시작 화면): 카메라 + TitleScreen. 빌드 목록 맨 앞(첫 장면)에 등록한다.</summary>
        [MenuItem("TCG/Setup/Build Title Scene")]
        public static void BuildTitleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraGo.AddComponent<AudioListener>();

            new GameObject("Title").AddComponent<TitleScreen>();

            EditorSceneManager.SaveScene(scene, TitleScenePath);
            RegisterScenes();
            Debug.Log($"[Setup] Built {TitleScenePath}");
        }

        /// <summary>빌드 목록: Title(첫 장면) → Battle → 나머지. 아직 없는 장면은 건너뛴다.</summary>
        static void RegisterScenes()
        {
            var first = new[] { TitleScenePath, BattleScenePath }.Where(File.Exists).ToList();
            var scenes = first.Select(path => new EditorBuildSettingsScene(path, true)).ToList();
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => !first.Contains(s.path)));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
        }
    }
}
