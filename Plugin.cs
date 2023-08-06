using System;
using BepInEx;
using BepInEx.Logging;
using Gloomwood;
using Gloomwood.AI;
using Gloomwood.Entity;
using Gloomwood.Players;
using HarmonyLib;
using UnityEngine;

namespace GloomwoodVoiceMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static readonly Plugin instance = new Plugin();

        public float testSound;
        public static float MicLoudness;
        private string _device;
        private AudioClip _clipRecord = new AudioClip();
        private int _sampleWindow = 128;
        private bool _isInitialized;

        private void Awake()
        {
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            GVMPatches.Initialize(); // Cache aIDetectParams
            Harmony.CreateAndPatchAll(typeof(GVMPatches));
        }

        public ManualLogSource getLogger() {
            return Logger;
        }

        public void InitMic() {
            if (_device == null) {
                _device = Microphone.devices[0];
                _clipRecord = Microphone.Start(_device, true, 999, 44100);
                Debug.Log(_clipRecord);
            }
        }

        public float LevelMax()
        {
            float levelMax = 0;
            float[] waveData = new float[_sampleWindow];
            int micPosition = Microphone.GetPosition(_device) - (_sampleWindow + 1);
            if (micPosition < 0) {
                return 0;
            }
            _clipRecord.GetData (waveData, micPosition);
            for (int i = 0; i < _sampleWindow; ++i) {
                float wavePeak = waveData [i] * waveData [i];
                if (levelMax < wavePeak) {
                    levelMax = wavePeak;
                }
            }
            return levelMax;
        }
    }

    public class GVMPatches {
        // Cache the AIDetectParams for a small performance boost.
        private static AIDetectParams aIDetectParams = new AIDetectParams();
        private static readonly int maxCooldown = 40;
        private static int cd = 0;

        public static void Initialize() {
            aIDetectParams.isPlayer = true;
            aIDetectParams.isAI = false;
            aIDetectParams.isLeaning = false;
            aIDetectParams.firstHand = true;
        }

        [HarmonyPatch(typeof(PlayerMovement), "Update")]
        [HarmonyPostfix]
        private static void PlayerMovement_Update_Postfix(PlayerMovement __instance) {
            if(cd < maxCooldown) {
                cd++;
            }

            AIDetectLevel detectLevel = AIDetectLevel.None;
            Plugin.instance.InitMic();
            float volume = Plugin.instance.LevelMax() * 6;
            Plugin.instance.getLogger().LogMessage("mic vol: " + volume);

            if(cd >= maxCooldown && volume < 1.0) {
                PlayerEntity plr = GameManager.Player;
                if(GameManager.HasPlayer) {
                    if(volume >= 0.03) {
                        detectLevel = AIDetectLevel.High;
                    } else if(volume >= 0.01) {
                        detectLevel = AIDetectLevel.Moderate;
                    } else {
                        detectLevel = AIDetectLevel.Low;
                    }
                    Plugin.instance.getLogger().LogMessage("Detection Level set to " + detectLevel + " from source volume " + volume);

                    foreach(AIEntity ai in GameManager.AIManager.GetActiveAI()) {
                        if(
                            ai.Sense != null 
                            && Vector3.Distance(plr.Position, ai.Position) > (16 * (int) detectLevel)
                            && ai.EntityType == EntityTypes.AI
                        ) {
                            try {
                                var detection = ai.Sense.GetDetection(plr.gameObject);
                                aIDetectParams.targetPosition = plr.position;
                                aIDetectParams.heardPosition = ai.Position;
                                aIDetectParams.hearLevel = detectLevel;
                                aIDetectParams.distanceSq = Vector3.Distance(ai.Position, plr.position);
                                aIDetectParams.target = GameManager.Player.gameObject;
                                aIDetectParams.source = GameManager.Player.gameObject;
                                aIDetectParams.player = GameManager.Player;

                                if(detection != null) {
                                    detection = ai.sense.ProcessDetection(detection, ref aIDetectParams);
                                    if(!ai.Sense.detectedObjList.Contains(plr.gameObject)) {
                                        ai.Sense.detectedObjList.Add(plr.gameObject);
                                    }
                                    ai.Sense.SetDetection(
                                        plr.gameObject, 
                                        detection
                                    );
                                }
                            } catch(Exception e) {
                                // Log any errors that occur.
                                Plugin.instance.getLogger().LogError(e.Message);
                            }
                        }
                    }
                }
                cd = 0;
            }
        }
    }
}
