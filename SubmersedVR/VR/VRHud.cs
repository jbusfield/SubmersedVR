using System;
using HarmonyLib;
using UnityEngine;

namespace SubmersedVR
{
    extern alias SteamVRActions;
    extern alias SteamVRRef;
    using SteamVRActions.Valve.VR;
    using System.Collections.Generic;
    using System.Reflection.Emit;

    // Tweaks regarding the HUD of the game
    static class VRHud
    {
        private static Transform screenCanvas;
        private static Transform overlayCanvas;
        private static Transform hud;

        private static Canvas staticHudCanvas = null;
        // private static OffsetCalibrationTool calibrationTool;

        public static void HideOverlays()
        {
            uGUI.main.overlays.gameObject.SetActive(false);
            uGUI.main.hud.gameObject.SetActive(false);
        }
        // TODO: Hud Distance needs dedicated canvas, since the Pips seem to assume the 1 meter canvas distance.
#if false
        public static float hudDistance = 1.0f;
        public static float HudDistance {
            get {
                return hudDistance;
            }
            set {
                hudDistance = value;
                if (staticHudCanvas == null || screenCanvas == null) {
                    return;
                }
                hud.transform.localPosition = Vector3.forward * (hudDistance - 1.0f);
            }
        }
        public static void OnHudDistanceChanged(float value) {
            HudDistance = value;
        }
#endif

        public static void SetupHandReticle(bool onLaserPointer, Camera uiCamera, Transform rightControllerUI)
        {
            if (onLaserPointer)
            {
                SetupHandReticleLaserPointer(uiCamera, rightControllerUI);
            }
            else
            {
                SetupHandReticleOnHand(uiCamera, rightControllerUI);
            }
        }

        public static void SetupHandReticleOnHand(Camera uiCamera, Transform rightControllerUI)
        {
            // Steal Reticle and attach to the right hand
            var handReticle = HandReticle.main.gameObject.WithParent(rightControllerUI.transform);
            handReticle.GetOrAddComponent<Canvas>().worldCamera = uiCamera;
            handReticle.transform.localEulerAngles = new Vector3(90, 0, 0);
            handReticle.transform.localPosition = new Vector3(0, 0, 0.05f);
            handReticle.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
        }

        public static void SetupHandReticleLaserPointer(Camera uiCamera, Transform rightControllerUI)
        {
            var handReticle = HandReticle.main.gameObject.WithParent(VRCameraRig.instance.laserPointerUI.pointerDot.transform);
            handReticle.transform.LookAt(uiCamera.transform.position);
            handReticle.transform.localRotation = Quaternion.Euler(40, 0, 0);
            handReticle.transform.localPosition = new Vector3(0, -5, VRCameraRig.instance.laserPointerUI.pointerDot.transform.localPosition.z);//new Vector3(0, 0, 0.05f);
            handReticle.transform.localScale = VRCameraRig.instance.laserPointerUI.pointerDot.transform.localScale * 2;//new Vector3(0.001f, 0.001f, 0.001f);
        }

        public static void OnHandReticleSettingChanged(bool onLaserPointer)
        {
            var rig = VRCameraRig.instance;
            if (!rig)
            {
                return;
            }
            SetupHandReticle(onLaserPointer, rig.uiCamera, rig.rightControllerUI.transform);
        }

        public static Canvas CreateWorldCanvas(this GameObject go)
        {
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.layer = LayerID.UI;
            return canvas;
        }

        public static void Setup(Camera uiCamera, Transform rightControllerUI)
        {
            Mod.logger.LogDebug($"Setting up HUD for {uiCamera.name}");

            screenCanvas = uGUI.main.screenCanvas.gameObject.transform;
            overlayCanvas = uGUI.main.overlays.gameObject.transform.parent;
            hud = uGUI.main.hud.transform;

            if (staticHudCanvas == null)
            {
                var uiRig = VRCameraRig.instance.uiRig.transform;
                var go = new GameObject("StaticHUDCanvas").WithParent(uiRig);
                staticHudCanvas = go.CreateWorldCanvas();
                var rt = go.GetComponent<RectTransform>();
                go.transform.localScale = screenCanvas.localScale;
                rt.sizeDelta = screenCanvas.GetComponent<RectTransform>().sizeDelta;
                rt.anchoredPosition = screenCanvas.GetComponent<RectTransform>().anchoredPosition;
                go.transform.localPosition = Vector3.forward + new Vector3(0.0f, 0.1f, 0.0f);
                go.transform.localRotation = Quaternion.identity;
            }
            staticHudCanvas.worldCamera = uiCamera;

            screenCanvas.SetParent(uiCamera.transform, true);
            overlayCanvas.SetParent(uiCamera.transform, true);

            //Makes the UI more comfortable to view
            screenCanvas.transform.localScale = new Vector3(0.00072f, 0.00072f, 0.00072f);
            overlayCanvas.transform.localScale = new Vector3(0.00032f, 0.00032f, 0.00032f);
            staticHudCanvas.transform.localScale = new Vector3(0.00085f, 0.00085f, 0.00085f);

            SetupHandReticle(Settings.PutHandReticleOnLaserPointer, uiCamera, rightControllerUI);
            Settings.PutHandReticleOnLaserPointerChanged -= OnHandReticleSettingChanged;
            Settings.PutHandReticleOnLaserPointerChanged += OnHandReticleSettingChanged;

            WristHud.Setup();

            screenCanvas.GetComponent<uGUI_CanvasScaler>()?.SetDirty();
            screenCanvas.GetComponentsInChildren<uGUI_CanvasScaler>().ForEach(cs => cs.SetDirty());
            MiscSettings.cameraBobbing = VROptions.enableCinematics; 
        }

        public static void OnEnterVehicle()
        {
            var player = Player.main;
            if (player != null)
            {
                hud.SetParent(staticHudCanvas.transform, false);
            }
        }

        public static void OnExitVehicle()
        {
            hud.SetParent(screenCanvas, false);
        }
    }

    static class WristHud
    {
        private static TransformOffset wristOffset = new TransformOffset(new Vector3(-0.079f, 0.148f, -0.158f), new Vector3(350.494f, 88.400f, 244.161f));
        private static GameObject wristTarget;
        private static Canvas canvas;
        private static CanvasGroup canvasGroup;

        // Cached Values
        private static Transform hudContent;
        private static Transform uiCamera;
        private static Transform cachedIndexTip;
        private static FMODAsset turnOnSound;
        private static FMODAsset turnOffSound;

        // State
        public static bool isHudOn = true;
        private static bool touchingWrist = false;
        private static bool prevTouchingWrist = false;

        public static FMODAsset CreateFMODAsset(string eventPath)
        {
            FMODAsset asset = ScriptableObject.CreateInstance<FMODAsset>();
            asset.path = eventPath;
            return asset;
        }

        // Create Wrist World Canvas
        public static void Setup()
        {
            var rig = VRCameraRig.instance;
            uiCamera = rig.uiCamera.transform;
            hudContent = uGUI.main.hud.transform.GetChild(0);

            if (wristTarget == null)
            {
                wristTarget = new GameObject("WristTarget").WithParent(rig.leftControllerUI).ResetTransform();
                var wristCanvasGo = new GameObject("WristCanvas").WithParent(wristTarget).ResetTransform();
                canvas = wristCanvasGo.CreateWorldCanvas();
                canvasGroup = wristCanvasGo.AddComponent<CanvasGroup>();
                wristCanvasGo.transform.localScale = new Vector3(0.0004f, 0.0004f, 0.0004f);
                wristOffset.Apply(wristTarget.transform);
            }

            Settings.PutBarsOnWristChanged -= OnPutBarsOnHandChanged;
            Settings.PutBarsOnWristChanged += OnPutBarsOnHandChanged;
            Toggle(Settings.PutBarsOnWrist);

            turnOnSound = CreateFMODAsset("event:/tools/flashlight/turn_on");
            turnOffSound = CreateFMODAsset("event:/tools/flashlight/turn_off");
        }

        public static Transform GetIndexFingerTip()
        {
            if (cachedIndexTip != null)
            {
                return cachedIndexTip;
            }
            var animator = Player.main?.playerAnimator;
            if (animator is Animator anim)
            {
                // TODO: Test this
                var tip = anim.transform.Find("export_skeleton/head_rig/neck/chest/clav_R/clav_R_aim/shoulder_R/hand_R/hand_R_point_base/hand_R_point_mid/hand_R_point_tip_rig");
                if (tip != null)
                {
                    cachedIndexTip = tip;
                    return tip;
                }
            }
            return null;
        }

        public static void OnPutBarsOnHandChanged(bool isOn)
        {
            Toggle(isOn);
        }


        public static void OnUpdate()
        { 
            if (!uGUI.isMainLevel)
            {
                return;
            }
            var camPos = uiCamera.transform.position;
            var worldRigPos = VRCameraRig.instance.rigParentTarget.position;
            var wristPos = wristTarget.transform.position;

            Vector3 wristDir = wristTarget.transform.TransformDirection(Vector3.forward);
            Vector3 toCam = (wristPos - camPos).normalized;

            float wristCamDot = Vector3.Dot(wristDir, toCam);
            bool isFacingCamera = wristCamDot > 0.1f;
            // DebugPanel.Show($"dot = {dot} <= {wristDir}, {toCam}");
            canvasGroup.alpha = Mathf.Max(wristCamDot, 0.0f);

            if (isFacingCamera && GetIndexFingerTip() is Transform indexTip)
            {
                var uiIndexPos = indexTip.position - worldRigPos;
                var wristDistance = Vector3.Distance(uiIndexPos, wristPos);
                // DebugPanel.Show($"wristDistance = {wristDistance} <= uiPos{uiIndexPos}, {wristPos}");
                const float threshold = 0.1f;
                touchingWrist = wristDistance < threshold;
                if (touchingWrist && !prevTouchingWrist)
                {
                    isHudOn = !isHudOn;
                    Utils.PlayFMODAsset(isHudOn ? turnOnSound : turnOffSound);
                }
                prevTouchingWrist = wristDistance < threshold;
            }
        }

        public static void Toggle(bool isOn)
        {
            if (canvas == null)
            {
                Setup();
            }

            var barsPanel = uGUI.main.barsPanel;
            if (isOn)
            {
                // Move to wrist
                Mod.logger.LogDebug("Turning WristHud on");
                barsPanel.WithParent(canvas.transform).ResetTransform();
                barsPanel.GetComponent<RectTransform>().pivot = new Vector2(0, 0);
                ManagedUpdate.Subscribe(ManagedUpdate.Queue.LateUpdateLast, new ManagedUpdate.OnUpdate(OnUpdate));
            }
            else
            {
                // Move back
                Mod.logger.LogDebug("Turning WristHud off");
                barsPanel.transform.SetParent(hudContent.transform, false);
                barsPanel.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
                barsPanel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0.0f, 0.0f);
                ManagedUpdate.Unsubscribe(ManagedUpdate.Queue.LateUpdateLast, new ManagedUpdate.OnUpdate(OnUpdate));
                isHudOn = true;
            }

        }
    }

    #region Patches

    //Handler for Exosuit entering only
    [HarmonyPatch(typeof(Vehicle), nameof(Vehicle.OnPilotModeBegin))]
    public static class SetHudStaticInVehicles
    {
        public static void Postfix(Vehicle __instance)
        {
            Mod.logger.LogInfo("Vehicle.OnPilotModeBegin");
            // TODO: How to check for SeaTruck?
            if (__instance is Exosuit)
            {
                VRHud.OnEnterVehicle();
            }
        }
    }

    //Handler for Exosuit exiting only
    [HarmonyPatch(typeof(Vehicle), nameof(Vehicle.OnPilotModeEnd))]
    public static class ResetHudStaticInVehicles
    {
        public static void Postfix(Vehicle __instance)
        {
            // TODO: How to check for SeaTruck?
            Mod.logger.LogInfo("Vehicle.OnPilotModeEnd {__instance is Exosuit}");
            if (__instance is Exosuit)
            {
                VRHud.OnExitVehicle();
                //This moves player off of the top of the suit
                Player.main.transform.position = Player.main.transform.position + SNCameraRoot.main.transform.forward * -2.5f;
                Player.main.transform.localPosition += new Vector3(0.0f, 0.5f, 0.0f);
            }
        }
    }

    //handler for Seatruck only entering the docking bay
    [HarmonyPatch(typeof(Dockable), nameof(Dockable.OnDockingComplete))]
    public static class ResetHudStaticWhenDocked
    {
        public static void Postfix(Dockable __instance)
        {
            Mod.logger.LogInfo("Dockable.OnDockingComplete");
            if(__instance.truckMotor)
            {
                VRHud.OnExitVehicle();      
            }
       }
    }

    //handler for Seatruck only exiting the docking bay
    [HarmonyPatch(typeof(Dockable), nameof(Dockable.OnUndockingComplete))]
    public static class ResetHudStaticWhenUndocked
    {
        public static void Postfix(Dockable __instance)
        {
            Mod.logger.LogInfo("Dockable.OnUndockingComplete");
            if(__instance.truckMotor)
            {
                VRHud.OnEnterVehicle();   
            }   
        }
    }

    //handler for Seatruck only entering
    [HarmonyPatch(typeof(SeaTruckMotor), nameof(SeaTruckMotor.StartPiloting))]
    static class SetHudStaticInSeaTrucker
    {
        public static void Postfix()
        {
            Mod.logger.LogInfo("SeaTruckMotor.StartPiloting");
            VRHud.OnEnterVehicle();
        }
    }

    //handler for Seatruck only exiting
    [HarmonyPatch(typeof(SeaTruckMotor), nameof(SeaTruckMotor.StopPiloting))]
    static class ResetHudStaticInSeaTrucker
    {
        public static void Postfix()
        {
            Mod.logger.LogInfo("SeaTruckMotor.StopPiloting");
            VRHud.OnExitVehicle();
        }
    }


    [HarmonyPatch(typeof(uGUI_PlayerSleep), nameof(uGUI_PlayerSleep.Start))]
    public static class ScaleSleep
    {
        public static void Postfix(uGUI_PlayerSleep __instance)
        {
             __instance.blackOverlay.transform.localScale = new Vector3(10f, 10f, 10f);
        }
    }

    [HarmonyPatch(typeof(uGUI_PlayerDeath), nameof(uGUI_PlayerDeath.Start))]
    public static class ScaleDeath
    {
        public static void Postfix(uGUI_PlayerDeath __instance)
        {
             __instance.blackOverlay.transform.localScale = new Vector3(10f, 10f, 10f);
        }
    }

    [HarmonyPatch(typeof(uGUI_Overlays), nameof(uGUI_Overlays.Awake))]
    public static class ScaleOverlays
    {
        public static void Postfix(uGUI_Overlays __instance)
        {
            __instance.gameObject.transform.localScale = new Vector3(10f, 10f, 10f);
        }
    }

    [HarmonyPatch(typeof(EndCreditsManager), nameof(EndCreditsManager.Update))]
    public static class ScaleEndCredits
    {
        public static void Postfix(EndCreditsManager __instance)
        {
             __instance.gameObject.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
        }
    }

    [HarmonyPatch(typeof(uGUI_ExpansionIntro), nameof(uGUI_ExpansionIntro.Start))]
    public static class ScaleuGUI_ExpansionIntro
    {
        public static void Postfix(uGUI_ExpansionIntro __instance)
        {
             __instance.gameObject.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
        }
    }


    [HarmonyPatch(typeof(uGUI_Pings), nameof(uGUI_Pings.IsVisibleNow))]
    public static class HidePingsWhenHudOff
    {
        public static void Postfix(ref bool __result)
        {
            __result &= WristHud.isHudOn;
        }
    }

    [HarmonyPatch(typeof(uGUI_DepthCompass), nameof(uGUI_DepthCompass.GetDepthInfo))]
    public static class HideDepthCompassWhenHudOff
    {
        public static void Postfix(ref uGUI_DepthCompass.DepthMode __result)
        {
            if (!WristHud.isHudOn)
            {
                __result = uGUI_DepthCompass.DepthMode.None;
            }
        }
    }

    [HarmonyPatch(typeof(uGUI_PinnedRecipes), nameof(uGUI_PinnedRecipes.GetMode))]
    public static class HidePinnedRecipesWhenHudOff
    {
        public static void Postfix(ref uGUI_PinnedRecipes.Mode __result)
        {
            if (!WristHud.isHudOn)
            {
                __result = uGUI_PinnedRecipes.Mode.Off;
            }
        }
    }

    [HarmonyPatch(typeof(uGUI_PowerIndicator), nameof(uGUI_PowerIndicator.IsPowerEnabled))]
    public static class HidePowerWhenHudOff
    {
        public static void Postfix(ref bool __result)
        {
            __result &= WristHud.isHudOn;
        }
    }

    [HarmonyPatch(typeof(uGUI_SeamothHUD), nameof(uGUI_SeamothHUD.Update))]
    public static class HideSeamothHudWhenHudOff
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var m = new CodeMatcher(instructions);
            m.MatchForward(true, new CodeMatch[] {
                new CodeMatch(OpCodes.Stloc_3),
            }).Advance(1).Insert(new CodeInstruction[] {
                new CodeInstruction(OpCodes.Ldloc_3),
                CodeInstruction.LoadField(typeof(WristHud), nameof(WristHud.isHudOn)),
                new CodeInstruction(OpCodes.And),
                new CodeInstruction(OpCodes.Stloc_3),
            });
            return m.InstructionEnumeration();
        }
    }

    [HarmonyPatch(typeof(uGUI_ExosuitHUD), nameof(uGUI_ExosuitHUD.Update))]
    public static class HideExosuitHudWhenHudOff
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var m = new CodeMatcher(instructions);
            m.MatchForward(true, new CodeMatch[] {
                new CodeMatch(OpCodes.Stloc_3),
            }).Advance(1).Insert(new CodeInstruction[] {
                new CodeInstruction(OpCodes.Ldloc_3),
                CodeInstruction.LoadField(typeof(WristHud), nameof(WristHud.isHudOn)),
                new CodeInstruction(OpCodes.And),
                new CodeInstruction(OpCodes.Stloc_3),
            });
            return m.InstructionEnumeration();
        }
    }

    //Make the pause menu a more comfortable scale
    [HarmonyPatch(typeof(IngameMenu), nameof(IngameMenu.Update))]
    class IngameMenu_Scale_Fixer
    {
        public static void Postfix(IngameMenu __instance)
        {
            __instance.transform.localScale = new Vector3(0.0013f , 0.0013f, 0.0013f);
            //__instance.transform.localPosition = SNCameraRoot.main.transform.forward * 1.5f;
       }
    }
    
    //Make the builder menu a more comfortable scale
    [HarmonyPatch(typeof(uGUI_BuilderMenu), nameof(uGUI_BuilderMenu.Update))]
    class uGUI_BuilderMenu_Scale_Fixer
    {
        public static void Postfix(uGUI_BuilderMenu __instance)
        {
            __instance.transform.localScale = new Vector3(0.0013f , 0.0013f, 0.0013f);
        }
    }
    
    //These next two functions eliminate the "squashed" HUD UI that comes from enabling XRSettings
    //by temporarily turning the setting off during execution
    [HarmonyPatch(typeof(uGUI_CanvasScaler), nameof(uGUI_CanvasScaler.UpdateFrustum))]
    static class uGUI_CanvasScalerFrustum_Fixer
    {
        public static bool Prefix()
        {
            XRSettingsEnabled.isEnabled = false;
            return true;
        }
        public static void Postfix()
        {
            XRSettingsEnabled.isEnabled = true;
        }
    }

    [HarmonyPatch(typeof(uGUI_SafeAreaScaler), nameof(uGUI_SafeAreaScaler.Update))]
    static class uGUI_SafeAreaScaler_Fixer
    {
        public static bool Prefix()
        {
            XRSettingsEnabled.isEnabled = false;
            return true;
        }
        public static void Postfix()
        {
            XRSettingsEnabled.isEnabled = true;
        }
    }


    #endregion

}
