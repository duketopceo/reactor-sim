using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ReactorAssetPlayCheck
{
    private const string Key = "ReactorAssetPlayCheck.Stage";
    private const string Start = "ReactorAssetPlayCheck.Start";
    private const string Code = "ReactorAssetPlayCheck.Code";
    private static double warmedAt;
    private static int enteredFrame = -1, captureIndex;
    private static Vector3 savedPosition;
    private static Quaternion savedRotation;
    private static float savedFov;
    private static int savedMask;

    static ReactorAssetPlayCheck() { if (SessionState.GetInt(Key, 0) != 0) Arm(); }

    public static void Run()
    {
        ControlRoomSceneBuilder.Build(true);
        SessionState.SetInt(Key, 1);
        SessionState.SetInt(Code, 1);
        SessionState.SetFloat(Start, (float)EditorApplication.timeSinceStartup);
        Arm();
        PlayModeGuard.Prepare();
        EditorApplication.isPlaying = true;
    }

    private static void Arm() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }

    private static void Poll()
    {
        int stage = SessionState.GetInt(Key, 0);
        if (stage == 0) return;
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Start, 0) > 120)
        {
            Debug.LogError("[ReactorAssetPlayCheck] FAIL timeout");
            Finish(1);
            return;
        }
        if (stage == 2)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(SessionState.GetInt(Code, 1));
            return;
        }
        if (!EditorApplication.isPlaying) return;
        if (warmedAt == 0) { warmedAt = EditorApplication.timeSinceStartup; return; }
        if (EditorApplication.timeSinceStartup - warmedAt < 4) return;
        int code = 0;
        try
        {
            var camera = Camera.main;
            var inspection = camera.GetComponent<ReactorInspectionView>();
            var controller = camera.GetComponent<OperatorController>();
            var bridge = UnityEngine.Object.FindAnyObjectByType<PlantBridge>();
            Require(bridge != null && bridge.Sim != null, "live simulation initialized");
            if (enteredFrame < 0)
            {
                savedPosition = camera.transform.position;
                savedRotation = camera.transform.rotation;
                savedFov = camera.fieldOfView;
                savedMask = camera.cullingMask;
                Require(inspection.Enter() && ReactorInspectionView.Active, "inspection enters");
                Require(!controller.enabled && ReactorInspectionView.ConsumesEscape, "inspection owns input and Escape");
                captureIndex = 0;
                enteredFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount <= enteredFrame) return;
            var hall = HallVisibility.FindHall();
            Require(hall.activeSelf, "inspection activates hall");
            var snapshot = bridge.GetSnapshot();
            var pivot = ReactorAssetCheck.Find(hall, "ROD_BANK_A_PIV");
            Vector3 expected = Vector3.Lerp(pivot.parent.Find("RS_ROD_BankA_Withdrawn_PIV").position,
                pivot.parent.Find("RS_ROD_BankA_Inserted_PIV").position, (float)snapshot.BankInsertion[0]);
            Require(Vector3.Distance(expected, pivot.position) < 0.02f, "live binder follows actual snapshot");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../out/reactor-assets"));
            Directory.CreateDirectory(directory);
            Capture(camera, Path.Combine(directory, "inspection-" + captureIndex + ".png"));
            if (++captureIndex < 4)
            {
                inspection.SetView(captureIndex);
                enteredFrame = Time.frameCount;
                return;
            }
            inspection.Leave();
            Require(!ReactorInspectionView.Active && controller.enabled, "inspection restores operator");
            Require(Vector3.Distance(camera.transform.position, savedPosition) < 1e-5f &&
                    Quaternion.Angle(camera.transform.rotation, savedRotation) < 0.01f &&
                    camera.fieldOfView == savedFov && camera.cullingMask == savedMask, "inspection restores camera exactly");
            Debug.Log("[ReactorAssetPlayCheck] PASS actual play-mode binder and inspection");
        }
        catch (Exception ex) { code = 1; Debug.LogError("[ReactorAssetPlayCheck] FAIL " + ex); }
        SessionState.SetInt(Code, code);
        SessionState.SetInt(Key, 2);
        EditorApplication.isPlaying = false;
    }

    private static void Capture(Camera camera, string path)
    {
        var render = new RenderTexture(1280, 960, 24) { antiAliasing = 4 };
        var priorTarget = camera.targetTexture;
        var priorActive = RenderTexture.active;
        var texture = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = render;
            camera.Render();
            RenderTexture.active = render;
            texture.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = priorTarget;
            RenderTexture.active = priorActive;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(render);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Debug.Log("[ReactorAssetPlayCheck] OK " + message);
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Poll;
        SessionState.EraseInt(Key);
        SessionState.EraseFloat(Start);
        SessionState.EraseInt(Code);
        warmedAt = 0;
        enteredFrame = -1;
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
