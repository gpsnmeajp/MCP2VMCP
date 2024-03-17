﻿/*
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

[RequireComponent(typeof(uOSC.uOscClient))]
public class SampleBonesSendBundle : MonoBehaviour
{
    uOSC.uOscClient uClient = null;

    public GameObject Model = null;
    private GameObject OldModel = null;
    public bool vrm0styleExpression = true;
    public string vrmfilepath;
    public bool RuntimeLoadGUI = true;

    string path = @"C:\default.vrm";
    string vrmType = "未読込";

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

        Application.targetFrameRate = 60; //60fps
    }

    void resetBE() {
        if (blendShapeProxy) { 
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

            //Virtual Tracker send from bone position
            var trackerBundle = new Bundle(Timestamp.Now);
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.Head, "Head");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.Spine, "Spine");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.LeftHand, "LeftHand");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.RightHand, "RightHand");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.LeftFoot, "LeftFoot");
            SendBoneTransformForTracker(ref trackerBundle, HumanBodyBones.RightFoot, "RightFoot");
            uClient.Send(trackerBundle);

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
        if (RuntimeLoadGUI) {
            var ButtonStyle = new GUIStyle(GUI.skin.button);
            ButtonStyle.fontSize = 24;
            var TextFieldStyle = new GUIStyle(GUI.skin.textField);
            TextFieldStyle.fontSize = 24;
            var LabelStyle = new GUIStyle(GUI.skin.label);
            LabelStyle.fontSize = 24;

            if (Model == null)
            {
                path = GUILayout.TextField(path, TextFieldStyle);
                if (GUILayout.Button("開く(Load)", ButtonStyle))
                {
                    LoadVRM(path);
                }
            }
            GUILayout.Label("VMCP Port:" + uClient.port, LabelStyle);
            GUILayout.Label("mocopi Port:" + "12351", LabelStyle);
            GUILayout.Label("VRM Type:" + vrmType, LabelStyle);
            GUILayout.Label("VRM0 Style Expression:" + vrm0styleExpression.ToString(), LabelStyle);

            if (GUILayout.Button("vrm0styleExpression", ButtonStyle))
            {
                vrm0styleExpression = !vrm0styleExpression;
            }


            if (GUILayout.Button("Reset", ButtonStyle))
            {
                resetBE();
            }
            if (GUILayout.Button("Happy(Joy)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.Joy, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Happy, 1);
            }
            if (GUILayout.Button("Angry(Angry)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.Angry, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Angry, 1);
            }
            if (GUILayout.Button("Sad(Sorrow)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.Sorrow, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Sad, 1);
            }
            if (GUILayout.Button("Relaxed(Fun)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.Fun, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Relaxed, 1);
            }
            if (GUILayout.Button("Aa(A)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.A, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Aa, 1);
            }
            if (GUILayout.Button("Ih(I)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.I, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Ih, 1);
            }
            if (GUILayout.Button("Ou(U)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.U, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Ou, 1);
            }
            if (GUILayout.Button("Ee(E)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.E, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Ee, 1);
            }
            if (GUILayout.Button("Oh(O)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.O, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Oh, 1);
            }
            if (GUILayout.Button("Blinkleft(Blink_L)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.Blink_L, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.BlinkLeft, 1);
            }
            if (GUILayout.Button("Blinkright(Blink_R)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.Blink_R, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.BlinkRight, 1);
            }
            if (GUILayout.Button("Blink(Blink)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.Blink, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1);
            }
            if (GUILayout.Button("Neutral(Neutral)", ButtonStyle))
            {
                resetBE();
                blendShapeProxy?.SetValue(BlendShapePreset.Neutral, 1);
                vrm10Root?.Runtime.Expression.SetWeight(ExpressionKey.Neutral, 1);
            }
        }
    }

    void SendBoneTransformForTracker(ref Bundle bundle, HumanBodyBones bone, string DeviceSerial)
    {
        var DeviceTransform = animator.GetBoneTransform(bone);
        if (DeviceTransform != null) {
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
        catch (NotVrm0Exception) { 
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
        catch (Exception) {
            vrmType = "読み込み不能";
        }
    }
}
