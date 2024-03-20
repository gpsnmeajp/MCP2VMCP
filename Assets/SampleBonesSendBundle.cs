/*
 * SampleBonesSendBundle (VRM0and1)
 * gpsnmeajp
 * https://sh-akira.github.io/VirtualMotionCaptureProtocol/
 *
 * These codes are licensed under CC0.
 * http://creativecommons.org/publicdomain/zero/1.0/deed.ja
 */
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;
using VRM;
using uOSC;
using UniGLTF;
using System.Linq;
using UnityEngine.XR;
using UnityEngine.UIElements;
using SimpleFileBrowser;
using static SimpleFileBrowser.FileBrowser;
using System.Net;

[RequireComponent(typeof(uOSC.uOscClient))]
public class SampleBonesSendBundle : MonoBehaviour
{
    uOSC.uOscClient uClient = null;

    public GameObject Model = null;
    private GameObject OldModel = null;
    public bool vrm0styleExpression = true;
    public string vrmfilepath;
    public bool RuntimeLoadGUI = true;
    string loadExceptionString = "";

    string path = @"C:\default.vrm";
    string vrmType = "未読込";
    bool tenkeyEnable = true;
    int panelMode = 0;
    int select = 0;

    bool expression = true;
    bool body = true;

    string address = "127.0.0.1";
    string port = "39539";

    string log = "";
    Vector2 logScrollPosition = Vector2.zero;

    public Transform mainCamera;

    Animator animator = null;
    Vrm10Instance vrm10Root = null;
    VRMBlendShapeProxy blendShapeProxy = null;

    SynchronizationContext synchronizationContext;

    public enum VirtualDevice
    {
        HMD = 0,
        Controller = 1,
        Tracker = 2,
    }

    void Start()
    {
        uClient = GetComponent<uOSC.uOscClient>();
        synchronizationContext = SynchronizationContext.Current;

        Application.logMessageReceivedThreaded += (text, stack, logType) =>
        {
            string typeString = "";
            switch (logType) {
                case LogType.Assert: typeString = "[Assert]"; break;
                case LogType.Error: typeString = "[Error]"; break;
                case LogType.Warning: typeString = "[Warning]"; break;
                case LogType.Log: typeString = "[Log]"; break;
                case LogType.Exception: typeString = "[Exception]"; break;
                default: typeString = "[Unkown]"; break;
            }

            log = DateTime.Now + "\n" + typeString + "\n" + text + "\n\n" + log;
            log = log.Substring(0, 65535);
        };

        Application.targetFrameRate = 60; //60fps

        FileBrowser.SetFilters(false, new FileBrowser.Filter("VRM", ".vrm"));
        FileBrowser.ShowLoadDialog((string[] paths) => {
            path = paths[0];
            LoadVRM(path);
        }, () => {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }, PickMode.Files, false, @"C:\", "default.vrm", "VRMの読み込み", "開く(Load)");
    }

    void resetBE()
    {
        if (blendShapeProxy)
        {
            var x = new Dictionary<BlendShapeKey, float>(blendShapeProxy.GetValues());
            foreach (var key in x.Keys)
            {
                blendShapeProxy.SetValue(key, 0);
            }

        }

        if (vrm10Root)
        {
            var y = new Dictionary<ExpressionKey, float>(vrm10Root.Runtime.Expression.GetWeights());
            foreach (var key in y.Keys)
            {
                vrm10Root.Runtime.Expression.SetWeight(key, 0);
            }
        }
    }
    void Update()
    {
        //Only model updated
        if (Model != null && OldModel != Model)
        {
            animator = Model.GetComponent<Animator>();
            vrm10Root = Model.GetComponent<Vrm10Instance>();
            blendShapeProxy = Model.GetComponent<VRMBlendShapeProxy>();
            OldModel = Model;
        }

        if (Model != null && animator != null && uClient != null)
        {
            if (body)
            {
                //Root
                var RootTransform = Model.transform;
                if (RootTransform != null)
                {
                    uClient.Send("/VMC/Ext/Root/Pos",
                        "root",
                        RootTransform.position.x, RootTransform.position.y, RootTransform.position.z,
                        RootTransform.rotation.x, RootTransform.rotation.y, RootTransform.rotation.z, RootTransform.rotation.w);
                }

                //Bones
                var boneBundle = new Bundle(Timestamp.Now);
                foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (bone != HumanBodyBones.LastBone)
                    {
                        var Transform = animator.GetBoneTransform(bone);
                        if (Transform != null)
                        {
                            boneBundle.Add(new Message("/VMC/Ext/Bone/Pos",
                                bone.ToString(),
                                Transform.localPosition.x, Transform.localPosition.y, Transform.localPosition.z,
                                Transform.localRotation.x, Transform.localRotation.y, Transform.localRotation.z, Transform.localRotation.w));
                        }
                    }
                }
                uClient.Send(boneBundle);
            }

            //Virtual Tracker send from bone position
            var trackerBundle = new Bundle(Timestamp.Now);
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.Head, "Head");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.Spine, "Spine");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.LeftHand, "LeftHand");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.RightHand, "RightHand");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.LeftFoot, "LeftFoot");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.RightFoot, "RightFoot");
            uClient.Send(trackerBundle);

            if (expression)
            {

                //Expression
                if (vrm10Root != null)
                {
                    var blendShapeBundle = new Bundle(Timestamp.Now);

                    foreach (var b in vrm10Root.Runtime.Expression.GetWeights())
                    {
                        string expressionName = b.Key.ToString();

                        if (vrm0styleExpression)
                        {
                            //VRM1 Preset -> VRM0 Preset 
                            switch (expressionName.ToLower())
                            {
                                case "happy": expressionName = "Joy"; break;
                                case "angry": expressionName = "Angry"; break;
                                case "sad": expressionName = "Sorrow"; break;
                                case "relaxed": expressionName = "Fun"; break;
                                case "aa": expressionName = "A"; break;
                                case "ih": expressionName = "I"; break;
                                case "ou": expressionName = "U"; break;
                                case "ee": expressionName = "E"; break;
                                case "oh": expressionName = "O"; break;
                                case "blinkleft": expressionName = "Blink_L"; break;
                                case "blinkright": expressionName = "Blink_R"; break;
                                default: break;
                            }
                        }

                        uClient.Send("/VMC/Ext/Blend/Val",
                            expressionName,
                            (float)b.Value
                            );
                    }
                    blendShapeBundle.Add(new Message("/VMC/Ext/Blend/Apply"));
                    uClient.Send(blendShapeBundle);
                }

                //BlendShape
                if (blendShapeProxy != null)
                {
                    var blendShapeBundle = new Bundle(Timestamp.Now);

                    foreach (var b in blendShapeProxy.GetValues())
                    {
                        blendShapeBundle.Add(new Message("/VMC/Ext/Blend/Val",
                            b.Key.ToString(),
                            (float)b.Value
                            ));
                    }
                    blendShapeBundle.Add(new Message("/VMC/Ext/Blend/Apply"));
                    uClient.Send(blendShapeBundle);
                }
            }

            //Available
            uClient.Send("/VMC/Ext/OK", 1);
        }
        else
        {
            uClient.Send("/VMC/Ext/OK", 0);
        }
        uClient.Send("/VMC/Ext/T", Time.time);

        //Load request
        uClient.Send("/VMC/Ext/VRM", vrmfilepath, "");

    }

    private void OnGUI()
    {
        const int main_window_id = 0;
        const int status_window_id = 1;
        const int log_window_id = 2;
        if (RuntimeLoadGUI)
        {
            var ButtonStyle = new GUIStyle(GUI.skin.button);
            ButtonStyle.fontSize = 16;
            var TextFieldStyle = new GUIStyle(GUI.skin.textField);
            TextFieldStyle.fontSize = 16;
            var LabelStyle = new GUIStyle(GUI.skin.label);
            LabelStyle.fontSize = 16;

            if (loadExceptionString != "")
            {
                GUI.Window(main_window_id, new Rect(0, 160, 340, 430), (id) =>
                {
                    GUILayout.Label(loadExceptionString, LabelStyle);
                }, "Control");
            }

            if (Model != null)
            {
                GUI.Window(status_window_id, new Rect(0, 0, 340, 155), (id) =>
                {
                    GUILayout.Label("VMCP Port: " + uClient.address + ":"+ uClient.port, LabelStyle);
                    GUILayout.Label("mocopi Port: 12351", LabelStyle);
                    GUILayout.Label("VRM Type: " + vrmType, LabelStyle);

                    GUILayout.Space(23);

                    if (GUILayout.Button("パネル切替(Next Panel)", ButtonStyle))
                    {
                        panelMode++;
                        if (panelMode > 2)
                        {
                            panelMode = 0;
                        }
                    }

                }, "Status");


                if (panelMode == 2)
                {
                    // 非表示
                }
                if (panelMode == 1)
                {
                    GUI.Window(main_window_id, new Rect(0, 160, 340, 430), (id) =>
                    {
                        if (GUILayout.Button("VRM0互換: " + (vrm0styleExpression ? "有効(Enabled)" : "無効(Disabled)"), ButtonStyle))
                        {
                            vrm0styleExpression = !vrm0styleExpression;
                        }
                        if (GUILayout.Button("Expression: " + (expression ? "有効(Enabled)" : "無効(Disabled)"), ButtonStyle))
                        {
                            expression = !expression;
                        }
                        if (GUILayout.Button("Body: " + (body ? "有効(Enabled)" : "無効(Disabled)"), ButtonStyle))
                        {
                            body = !body;
                        }

                        GUILayout.Space(23);

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Address: ", LabelStyle);
                        address = GUILayout.TextField(address, TextFieldStyle);
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Port: ", LabelStyle);
                        port = GUILayout.TextField(port, TextFieldStyle);
                        GUILayout.EndHorizontal();

                        if (GUILayout.Button("接続切替(Change connection)", ButtonStyle))
                        {
                            uClient.address = address;
                            uClient.port = int.Parse(port);
                        }
                    }, "Control 1");
                }
                if (panelMode == 0)
                {
                    GUI.Window(main_window_id, new Rect(0, 160, 340, 430), (id) =>
                    {
                        var SquareButtonStyle = new GUIStyle(GUI.skin.button);
                        SquareButtonStyle.fontSize = 16;
                        SquareButtonStyle.fixedWidth = 60;
                        SquareButtonStyle.fixedHeight = 60;
                        SquareButtonStyle.wordWrap = true;

                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("7\nRela xed", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad7)))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.Fun, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Relaxed, 1);
                        }
                        if (GUILayout.Button("8\nAngry\n", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad8)))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.Angry, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Angry, 1);
                        }

                        if (GUILayout.Button("9\nSad\n", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad9)))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.Sorrow, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Sad, 1);
                        }

                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("4\nHappy\n", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad4)))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.Joy, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Happy, 1);
                        }

                        if (GUILayout.Button("5\nReset\n", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad5)))
                        {
                            resetBE();
                        }
                        if (GUILayout.Button("6\n▲\n", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad6)))
                        {
                            resetBE();
                            if (select == 0)
                            {
                                blendShapeProxy?.SetValue(BlendShapePreset.A, 1);
                                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Aa, 1);
                            }
                            if (select == 1)
                            {
                                blendShapeProxy?.SetValue(BlendShapePreset.I, 1);
                                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Ih, 1);
                            }
                            if (select == 2)
                            {
                                blendShapeProxy?.SetValue(BlendShapePreset.U, 1);
                                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Ou, 1);
                            }
                            if (select == 3)
                            {
                                blendShapeProxy?.SetValue(BlendShapePreset.E, 1);
                                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Ee, 1);
                            }
                            if (select == 4)
                            {
                                blendShapeProxy?.SetValue(BlendShapePreset.O, 1);
                                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Oh, 1);
                            }

                        }

                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();

                        if (GUILayout.Button("1\nBlink\nRight", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad1)))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.Blink_R, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.BlinkRight, 1);
                        }
                        if (GUILayout.Button("2\nBlink\n", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad2)))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.Blink, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1);
                        }
                        if (GUILayout.Button("3\nBlink\nLeft", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad3)))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.Blink_L, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.BlinkLeft, 1);
                        }


                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();

                        if (GUILayout.Button("0\nNeu tral", SquareButtonStyle) || (tenkeyEnable && Input.GetKey(KeyCode.Keypad0)))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.Neutral, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Neutral, 1);
                        }

                        GUILayout.EndHorizontal();

                        GUILayout.Space(15);

                        GUILayout.BeginHorizontal();

                        if (GUILayout.Button("Aa", SquareButtonStyle))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.A, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Aa, 1);
                        }
                        if (GUILayout.Button("Ih", SquareButtonStyle))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.I, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Ih, 1);
                        }
                        if (GUILayout.Button("Ou", SquareButtonStyle))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.U, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Ou, 1);
                        }
                        if (GUILayout.Button("Ee", SquareButtonStyle))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.E, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Ee, 1);
                        }
                        if (GUILayout.Button("Oh", SquareButtonStyle))
                        {
                            resetBE();
                            blendShapeProxy?.SetValue(BlendShapePreset.O, 1);
                            vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Oh, 1);
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        SquareButtonStyle.fixedHeight = 20;

                        if (GUILayout.Button(select == 0 ? "▲" : "", SquareButtonStyle))
                        {
                            select = 0;
                        }
                        if (GUILayout.Button(select == 1 ? "▲" : "", SquareButtonStyle))
                        {
                            select = 1;
                        }
                        if (GUILayout.Button(select == 2 ? "▲" : "", SquareButtonStyle))
                        {
                            select = 2;
                        }
                        if (GUILayout.Button(select == 3 ? "▲" : "", SquareButtonStyle))
                        {
                            select = 3;
                        }
                        if (GUILayout.Button(select == 4 ? "▲" : "", SquareButtonStyle))
                        {
                            select = 4;
                        }
                        GUILayout.EndHorizontal();


                        GUILayout.Space(20);
                        if (GUILayout.Button("テンキー操作(Num Pad): " + (tenkeyEnable ? "有効(ON)" : "無効(OFF)"), ButtonStyle))
                        {
                            tenkeyEnable = !tenkeyEnable;
                        }

                    }, "Control 0");
                }
            }

            if (log != "")
            {
                GUILayout.Window(log_window_id, new Rect(Screen.width - 340, 0, 340, 430), (id) =>
                {
                    logScrollPosition = GUILayout.BeginScrollView(logScrollPosition);
                    GUILayout.Label(log, LabelStyle);
                    GUILayout.EndScrollView();
                }, "Log");
            }
        }
    }

    void SendBoneTransformForTracker(ref Bundle bundle, HumanBodyBones bone, string DeviceSerial)
    {
        var DeviceTransform = animator.GetBoneTransform(bone);
        if (DeviceTransform != null)
        {
            bundle.Add(new Message("/VMC/Ext/Tra/Pos",
        (string)DeviceSerial,
        (float)DeviceTransform.position.x,
        (float)DeviceTransform.position.y,
        (float)DeviceTransform.position.z,
        (float)DeviceTransform.rotation.x,
        (float)DeviceTransform.rotation.y,
        (float)DeviceTransform.rotation.z,
        (float)DeviceTransform.rotation.w));
        }
    }

    //Load VRM file on runtime
    public void LoadVRM(string path)
    {
        try
        {
            if (Model != null)
            {
                mainCamera.parent = null;
                Destroy(Model);
                Model = null;
            }

            if (File.Exists(path))
            {
                vrmfilepath = path;
                byte[] VRMdataRaw = File.ReadAllBytes(path);
                LoadVRMFromData(VRMdataRaw);
            }
            else
            {
                Debug.LogError("File not found: " + path);
                vrmType = "ファイルが無い";
                loadExceptionString = "File not found";

            }
        }
        catch (Exception e) {
            loadExceptionString = e.Message + "\n\n" + e.StackTrace;
        }
    }

    //Load VRM data on runtime
    //You can receive VRM over the network or file or other.
    public void LoadVRMFromData(byte[] VRMdataRaw)
    {
        GlbLowLevelParser glbLowLevelParser = new GlbLowLevelParser(null, VRMdataRaw);
        GltfData gltfData = glbLowLevelParser.Parse();
        try
        {
            VRMData vrm = new VRMData(gltfData);
            VRMImporterContext vrmImporter = new VRMImporterContext(vrm);

            synchronizationContext.Post(async (_) =>
            {
                RuntimeGltfInstance gltfInstance = await vrmImporter.LoadAsync(new VRMShaders.ImmediateCaller());
                gltfData.Dispose();
                vrmImporter.Dispose();
                vrmType = "VRM0";
                loadExceptionString = "";

                Model = gltfInstance.Root;
                Model.transform.parent = this.transform;
                animator = Model.GetComponent<Animator>();
                mainCamera.parent = animator.GetBoneTransform(HumanBodyBones.Hips);

                gltfInstance.EnableUpdateWhenOffscreen();
                gltfInstance.ShowMeshes();

                var mocopiAvatar = Model.AddComponent<Mocopi.Receiver.MocopiAvatar>();
                GetComponent<Mocopi.Receiver.MocopiSimpleReceiver>().enabled = false;
                await Task.Delay(100);
                GetComponent<Mocopi.Receiver.MocopiSimpleReceiver>().AvatarSettings.Clear();
                GetComponent<Mocopi.Receiver.MocopiSimpleReceiver>().AddAvatar(mocopiAvatar, 12351);
                await Task.Delay(100);
                GetComponent<Mocopi.Receiver.MocopiSimpleReceiver>().enabled = true;
            }, null);

            return; //VRM0 Loaded
        }
        catch (NotVrm0Exception e)
        {
            loadExceptionString = e.Message + "\n\n" + e.StackTrace;
            //continue loading
        }

        try
        {
            Vrm10Data vrm10 = Vrm10Data.Parse(gltfData);
            GltfData migratedGltfData = null;
            Vrm10Importer vrm10Importer = new Vrm10Importer(vrm10);

            synchronizationContext.Post(async (_) =>
            {
                RuntimeGltfInstance gltfInstance = await vrm10Importer.LoadAsync(new VRMShaders.ImmediateCaller());
                gltfData.Dispose();
                vrm10Importer.Dispose();

                if (migratedGltfData != null)
                {
                    migratedGltfData.Dispose();
                }

                Model = gltfInstance.Root;
                Model.transform.parent = this.transform;
                animator = Model.GetComponent<Animator>();
                mainCamera.parent = animator.GetBoneTransform(HumanBodyBones.Hips);

                vrmType = "VRM1";
                loadExceptionString = "";

                gltfInstance.EnableUpdateWhenOffscreen();
                gltfInstance.ShowMeshes();

                var mocopiAvatar = Model.AddComponent<Mocopi.Receiver.MocopiAvatar>();
                GetComponent<Mocopi.Receiver.MocopiSimpleReceiver>().enabled = false;
                await Task.Delay(100);
                GetComponent<Mocopi.Receiver.MocopiSimpleReceiver>().AvatarSettings.Clear();
                GetComponent<Mocopi.Receiver.MocopiSimpleReceiver>().AddAvatar(mocopiAvatar, 12351);
                await Task.Delay(100);
                GetComponent<Mocopi.Receiver.MocopiSimpleReceiver>().enabled = true;
            }, null);
        }
        catch (Exception e)
        {
            loadExceptionString = e.Message + "\n\n" + e.StackTrace;
            vrmType = "読み込み不能";
        }
    }
}
