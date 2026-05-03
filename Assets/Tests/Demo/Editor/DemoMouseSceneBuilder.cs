#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace io.github.hatayama.UnityCliLoop
{
    public static class DemoMouseSceneBuilder
    {
        private const string SCENE_PATH = "Assets/Scenes/SimulateMouseInputDemoScene.unity";

        [MenuItem("UnityCliLoop/Build Mouse Input Demo Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateDirectionalLight();
            CreateGround();
            GameObject player = CreatePlayer();
            CreateTargets(player.transform.position);

            bool saved = EditorSceneManager.SaveScene(scene, SCENE_PATH);
            if (!saved)
            {
                Debug.Assert(false, $"[DemoMouseSceneBuilder] Failed to save scene to {SCENE_PATH}");
                return;
            }
            Debug.Log($"[DemoMouseSceneBuilder] Scene saved to {SCENE_PATH}");
        }

        private static void CreateDirectionalLight()
        {
            GameObject lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.84f);
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(5f, 1f, 5f);

            Renderer renderer = ground.GetComponent<Renderer>();
            renderer.material.color = new Color(0.3f, 0.5f, 0.3f);
        }

        private static GameObject CreatePlayer()
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "UnityChan";
            player.transform.position = new Vector3(0f, 1f, 0f);

            Renderer renderer = player.GetComponent<Renderer>();
            renderer.material.color = new Color(0.2f, 0.6f, 1f);

            player.AddComponent<DemoWeaponSelector>();
            player.AddComponent<DemoMouseShooter>();
            player.AddComponent<DemoMouseLook>();

            CreateFollowCamera(player.transform);

            return player;
        }

        // DemoMouseLook expects CinemachineVirtualCamera + Transposer + Composer
        private static void CreateFollowCamera(Transform target)
        {
            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 60f;
            cameraGo.AddComponent<CinemachineBrain>();

            GameObject vcamGo = new GameObject("CM vcam - UnityChan Follow");
            CinemachineVirtualCamera vcam = vcamGo.AddComponent<CinemachineVirtualCamera>();
            vcam.Follow = target;
            vcam.LookAt = target;
            vcam.m_Lens.FieldOfView = 60f;

            CinemachineTransposer transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;
            transposer.m_FollowOffset = new Vector3(0f, 2f, -4f);
            transposer.m_XDamping = 0f;
            transposer.m_YDamping = 0f;
            transposer.m_ZDamping = 0f;

            CinemachineComposer composer = vcam.AddCinemachineComponent<CinemachineComposer>();
            composer.m_TrackedObjectOffset = new Vector3(0f, 0.8f, 0f);
            composer.m_HorizontalDamping = 0f;
            composer.m_VerticalDamping = 0f;
        }

        private static void CreateTargets(Vector3 playerPosition)
        {
            Vector3[] targetPositions = {
                playerPosition + new Vector3(0f, 0.5f, 10f),
                playerPosition + new Vector3(-3f, 0.5f, 12f),
                playerPosition + new Vector3(3f, 0.5f, 8f),
                playerPosition + new Vector3(-5f, 1.5f, 15f),
                playerPosition + new Vector3(5f, 1.0f, 14f),
            };

            for (int i = 0; i < targetPositions.Length; i++)
            {
                GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube);
                target.name = $"Target_{i}";
                target.transform.position = targetPositions[i];
                target.transform.localScale = new Vector3(1f, 1f, 1f);

                Renderer renderer = target.GetComponent<Renderer>();
                renderer.material.color = Color.red;

                // Rigidbody so bullets can knock them
                Rigidbody rb = target.AddComponent<Rigidbody>();
                rb.mass = 2f;
            }
        }
    }
}
#endif
