using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Steamworks;

namespace SpectatorPlus
{
    [BepInPlugin("com.spectatorplus.mod", "SpectatorPlus", "1.0.0")]
    [BepInProcess("MageArena.exe")]
    public class SpectatorPlus : BaseUnityPlugin
    {
        internal static ManualLogSource ModLogger;
        private static Harmony harmony;
        
        // Spectator state
        private static bool isSpectator = false;
        private static bool isFreecamMode = false;
        private static PlayerMovement spectateTarget = null;
        private static int currentPlayerIndex = 0;
        private static float spectateDistance = 5f; // Default zoom distance
        private static float minSpectateDistance = 2f; // Minimum zoom
        private static float maxSpectateDistance = 15f; // Maximum zoom
        private static float spectateHeight = 3f; // Base height offset

        // Spectator configuration
        private static bool invertMouseX = false; // Config: Invert horizontal mouse
        private static bool invertMouseY = false; // Config: Invert vertical mouse
        private static float cameraSmoothing = 5f; // Config: Camera smoothing speed
        private static Vector3 targetCameraPosition;
        private static Quaternion targetCameraRotation;
        
        // Freecam variables
        private static Vector3 freecamPosition;
        private static float freecamYaw = 0f;
        private static float freecamPitch = 0f;
        private static float freecamSpeed = 10f;
        private static float freecamSensitivity = 2f;
        
        // Original camera transform for restoration
        private static Vector3 originalCameraPosition;
        private static Quaternion originalCameraRotation;
        private static Transform originalCameraParent;

        // BepInEx Configuration
        private static ConfigEntry<bool> configInvertMouseX;
        private static ConfigEntry<bool> configInvertMouseY;
        private static ConfigEntry<float> configCameraSmoothing;
        private static ConfigEntry<float> configMouseSensitivity;
        private static ConfigEntry<float> configMinZoomDistance;
        private static ConfigEntry<float> configMaxZoomDistance;
        private static ConfigEntry<float> configDefaultZoomDistance;
        private static ConfigEntry<float> configZoomSpeed;
        private static ConfigEntry<float> configSpectateHeight;
        private static ConfigEntry<float> configFreecamSpeed;
        private static ConfigEntry<KeyCode> configFreecamToggleKey;
        private static ConfigEntry<KeyCode> configPlayerCycleKey;

        private void Awake()
        {
            ModLogger = BepInEx.Logging.Logger.CreateLogSource("SpectatorPlus");
            ModLogger.LogInfo("SpectatorPlus mod loaded!");

            // Initialize BepInEx configuration
            InitializeConfig();
            
            harmony = new Harmony("com.spectatorplus.mod");
            
            // Apply Harmony patches
            harmony.PatchAll(typeof(SpectatorPlus).Assembly);
            
            ModLogger.LogInfo("Harmony patches applied!");
        }

        private void InitializeConfig()
        {
            // Create configuration entries with descriptions and default values
            configInvertMouseX = Config.Bind("Camera", "InvertMouseX", false, 
                "Invert horizontal mouse movement");
            
            configInvertMouseY = Config.Bind("Camera", "InvertMouseY", false, 
                "Invert vertical mouse movement");
            
            configCameraSmoothing = Config.Bind("Camera", "CameraSmoothing", 8f, 
                "Camera smoothing speed for spectating (higher = smoother)");
            
            configMouseSensitivity = Config.Bind("Camera", "MouseSensitivity", 2f, 
                "Mouse sensitivity");
            
            configMinZoomDistance = Config.Bind("Spectating", "MinZoomDistance", 2f, 
                "Minimum zoom distance when spectating");
            
            configMaxZoomDistance = Config.Bind("Spectating", "MaxZoomDistance", 15f, 
                "Maximum zoom distance when spectating");
            
            configDefaultZoomDistance = Config.Bind("Spectating", "DefaultZoomDistance", 5f, 
                "Default zoom distance when spectating");
            
            configZoomSpeed = Config.Bind("Spectating", "ZoomSpeed", 5f, 
                "Zoom speed when using mouse wheel");
            
            configSpectateHeight = Config.Bind("Spectating", "SpectateHeight", 3f, 
                "Height offset when spectating players");

            configFreecamSpeed = Config.Bind("Freecam", "FreecamSpeed", 10f, 
                "Movement speed in freecam mode");
            
            configFreecamToggleKey = Config.Bind("Controls", "FreecamToggleKey", KeyCode.F5, 
                "Key to toggle freecam mode on/off");
            
            configPlayerCycleKey = Config.Bind("Controls", "PlayerCycleKey", KeyCode.Mouse0, 
                "Key to cycle between players when spectating");

            // Apply configuration values
            ApplyConfig();
            
            // Validate and fix any invalid config values
            ValidateAndFixConfig();
            
            ModLogger.LogInfo("BepInEx configuration initialized");
        }

        internal static void ApplyConfig()
        {
            // Apply the config values
            invertMouseX = configInvertMouseX.Value;
            invertMouseY = configInvertMouseY.Value;
            cameraSmoothing = configCameraSmoothing.Value;
            freecamSensitivity = configMouseSensitivity.Value;
            minSpectateDistance = configMinZoomDistance.Value;
            maxSpectateDistance = configMaxZoomDistance.Value;
            spectateDistance = configDefaultZoomDistance.Value;
            spectateHeight = configSpectateHeight.Value;
            freecamSpeed = configFreecamSpeed.Value;
            
            ModLogger.LogInfo($"Config applied - InvertX: {invertMouseX}, InvertY: {invertMouseY}, Smoothing: {cameraSmoothing}, Sensitivity: {freecamSensitivity}, FreecamSpeed: {freecamSpeed}");
        }

        internal static void ValidateAndFixConfig()
        {
            ModLogger.LogInfo("Validating configuration values...");
            
            // Validate and fix camera smoothing
            if (configCameraSmoothing.Value < 0.1f)
            {
                configCameraSmoothing.Value = 0.1f;
                ModLogger.LogWarning("Camera smoothing was too low, set to minimum value 0.1");
            }
            
            // Validate and fix mouse sensitivity
            if (configMouseSensitivity.Value < 0.1f)
            {
                configMouseSensitivity.Value = 0.1f;
                ModLogger.LogWarning("Mouse sensitivity was too low, set to minimum value 0.1");
            }
            
            // Validate and fix zoom distances
            if (configMinZoomDistance.Value < 0.5f)
            {
                configMinZoomDistance.Value = 0.5f;
                ModLogger.LogWarning("Min zoom distance was too low, set to minimum value 0.5");
            }
            
            if (configMaxZoomDistance.Value < configMinZoomDistance.Value)
            {
                configMaxZoomDistance.Value = configMinZoomDistance.Value + 5f;
                ModLogger.LogWarning("Max zoom distance was lower than min, adjusted automatically");
            }
            
            if (configDefaultZoomDistance.Value < configMinZoomDistance.Value || configDefaultZoomDistance.Value > configMaxZoomDistance.Value)
            {
                configDefaultZoomDistance.Value = (configMinZoomDistance.Value + configMaxZoomDistance.Value) / 2f;
                ModLogger.LogWarning("Default zoom distance was out of range, set to middle value");
            }
            
            // Validate and fix speeds
            if (configZoomSpeed.Value < 0.1f)
            {
                configZoomSpeed.Value = 0.1f;
                ModLogger.LogWarning("Zoom speed was too low, set to minimum value 0.1");
            }
            
            if (configFreecamSpeed.Value < 0.1f)
            {
                configFreecamSpeed.Value = 0.1f;
                ModLogger.LogWarning("Freecam speed was too low, set to minimum value 0.1");
            }
            
            ModLogger.LogInfo("Configuration validation complete");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        private void Update()
        {
            // Only allow spectator functionality if the current player is the host
            if (!IsCurrentPlayerHost())
            {
                return;
            }
            
            if (!isSpectator) return;
            
            // Handle freecam toggle with configurable key
            if (Input.GetKeyDown(configFreecamToggleKey.Value))
            {
                ToggleFreecam();
            }
            
            // Handle freecam movement if active
            if (isFreecamMode)
            {
                UpdateFreecam();
            }
            
            // Handle player cycling with configurable key (only when not in freecam)
            if (Input.GetKeyDown(configPlayerCycleKey.Value) && !isFreecamMode)
            {
                CycleToNextPlayer();
            }
        }

        internal static bool IsPlayerAlive(PlayerMovement pm)
        {
            if (pm == null) return false;
            
            // First check isDead (public field)
            if (pm.isDead) return false;
            
            if (pm.playerHealth <= 0f) return false;
            
            return true;
        }

        internal static void SetPlayerHealth(PlayerMovement pm, float health)
        {
            if (pm == null) return;
            
            pm.playerHealth = health;
        }

        internal static PlayerRespawnManager GetPlayerRespawnManager(PlayerMovement pm)
        {
            if (pm == null) return null;
            
            return pm.prm;
        }

        #region Harmony Patches

        [HarmonyPatch(typeof(MainMenuManager), "ActuallyStartGameActually")]
        internal static class KillSpectatorOnGameStartPatch
        {
            private static void Postfix(MainMenuManager __instance)
            {
                SpectatorPlus.ModLogger?.LogInfo("ActuallyStartGameActually called - Game started, checking for spectator player");
                
                if (__instance.pm != null)
                {
                    SpectatorPlus.ModLogger?.LogInfo($"Found pm: {__instance.pm.playername}, IsOwner: {__instance.pm.IsOwner}");
                    
                    if (SpectatorPlus.IsSpectatorPlayer(__instance.pm))
                    {
                        SpectatorPlus.ModLogger?.LogInfo("Local player detected as spectator! Killing immediately");
                        __instance.StartCoroutine(SpectatorPlus.KillSpectatorCoroutine(__instance.pm));
                    }
                    else
                    {
                        SpectatorPlus.ModLogger?.LogInfo("Player is not spectator (not owner)");
                    }
                }
                else
                {
                    SpectatorPlus.ModLogger?.LogWarning("pm is null in ActuallyStartGameActually");
                }
            }
        }

        [HarmonyPatch(typeof(PlayerRespawnManager), "RespawnRoutine")]
        internal static class DisableRespawnPatch
        {
            private static bool Prefix(PlayerRespawnManager __instance, ref IEnumerator __result)
            {
                SpectatorPlus.ModLogger?.LogInfo("RespawnRoutine called");
                // If this is the spectator player, prevent respawning
                if (SpectatorPlus.IsSpectatorPlayer(__instance.pmv))
                {
                    SpectatorPlus.ModLogger?.LogInfo("Replacing respawn routine with no-op for spectator player");
                    __result = SpectatorPlus.NoOpCoroutine(); // Return empty coroutine instead of blocking
                    return false; // Skip the original respawn method
                }
                return true; // Allow normal respawning for other players
            }
        }

        [HarmonyPatch(typeof(PlayerRespawnManager), "ColiRespawnRoutine")]
        internal static class DisableColiRespawnPatch
        {
            private static bool Prefix(PlayerRespawnManager __instance, ref IEnumerator __result)
            {
                SpectatorPlus.ModLogger?.LogInfo("ColiRespawnRoutine called");
                if (SpectatorPlus.IsSpectatorPlayer(__instance.pmv))
                {
                    SpectatorPlus.ModLogger?.LogInfo("Replacing coli respawn routine with no-op for spectator player");
                    __result = SpectatorPlus.NoOpCoroutine();
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(PlayerRespawnManager), "SpectateRoutine")]
        internal static class CustomSpectateRoutinePatch
        {
            private static bool Prefix(PlayerRespawnManager __instance)
            {
                SpectatorPlus.ModLogger?.LogInfo("SpectateRoutine called");
                if (SpectatorPlus.IsSpectatorPlayer(__instance.pmv))
                {
                    SpectatorPlus.ModLogger?.LogInfo("Starting custom spectate routine for spectator");
                    // Start our custom spectate routine that ignores team restrictions
                    __instance.StartCoroutine(SpectatorPlus.CustomSpectateCoroutine(__instance));
                    return false; // Skip original method
                }
                return true;
            }
        }

        // Game end hook to clear all spectator states
        [HarmonyPatch(typeof(PlayerRespawnManager), "EndGame")]
        internal static class EndGamePatch
        {
            private static void Prefix()
            {
                try
                {
                    SpectatorPlus.ModLogger?.LogInfo("Game ended - clearing all spectator states and resetting camera");
                    SpectatorPlus.ResetAllStates();
                }
                catch (System.Exception e)
                {
                    SpectatorPlus.ModLogger?.LogError($"Error in EndGame patch: {e.Message}");
                }
            }
        }

        #endregion

        #region Helper Methods

        internal static IEnumerator NoOpCoroutine()
        {
            yield break; // Empty coroutine that does nothing
        }

        internal static bool IsSpectatorPlayer(PlayerMovement pm)
        {
            if (pm == null) return false;
            
            // Only allow spectator mode if the current player is the host
            if (!IsCurrentPlayerHost())
            {
                return false;
            }
            
            // If the mod is installed and player is host, the local player is always the spectator
            return pm.IsOwner;
        }

        internal static bool IsCurrentPlayerHost()
        {
            try
            {
                // Check if BootstrapManager exists
                if (BootstrapManager.instance == null)
                {
                    return false;
                }

                // Check if we're in a lobby
                if (BootstrapManager.CurrentLobbyID == 0)
                {
                    return false;
                }

                // Get the lobby owner and local player Steam IDs
                CSteamID lobbyId = new CSteamID(BootstrapManager.CurrentLobbyID);
                CSteamID ownerId = SteamMatchmaking.GetLobbyOwner(lobbyId);
                CSteamID localId = SteamUser.GetSteamID();

                // Validate Steam IDs
                if (ownerId == CSteamID.Nil || localId == CSteamID.Nil)
                {
                    return false;
                }

                return ownerId == localId;
            }
            catch (System.Exception ex)
            {
                ModLogger?.LogError($"Error checking host status: {ex.Message}");
                return false;
            }
        }

        internal static void HideUIComponents()
        {
            // Only allow UI hiding if the current player is the host
            if (!IsCurrentPlayerHost())
            {
                return;
            }
            
            ModLogger.LogInfo("Hiding UI components for spectator mode");
            
            // Check if SceneManager is available
            if (SceneManager.sceneCount == 0)
            {
                ModLogger.LogWarning("No scenes loaded, cannot hide UI components");
                return;
            }
            
            // Hide GameScene UI components
            Scene gameScene = SceneManager.GetSceneByName("GameScene");
            if (gameScene.isLoaded)
            {
                ModLogger.LogInfo("Found GameScene, hiding inventory and level up UI");
                
                // Find Canvas root object and hide INVUI
                GameObject[] rootObjects = gameScene.GetRootGameObjects();
                if (rootObjects != null)
                {
                    foreach (GameObject rootObj in rootObjects)
                    {
                        if (rootObj != null && rootObj.name == "Canvas")
                        {
                            Transform invUI = rootObj.transform.Find("INVUI");
                            if (invUI != null)
                            {
                                invUI.gameObject.SetActive(false);
                                ModLogger.LogInfo("Hidden Canvas/INVUI");
                            }
                            
                            Transform lvlUpText = rootObj.transform.Find("lvluptext");
                            if (lvlUpText != null)
                            {
                                lvlUpText.gameObject.SetActive(false);
                                ModLogger.LogInfo("Hidden Canvas/lvluptext");
                            }
                            break;
                        }
                    }
                }
            }
            else
            {
                ModLogger.LogWarning("GameScene not found or not loaded");
            }
        }

        internal static void RestoreUIComponents()
        {
            // Only allow UI restoration if the current player is the host
            if (!IsCurrentPlayerHost())
            {
                return;
            }
            
            ModLogger.LogInfo("Restoring UI components");
            
            // Check if SceneManager is available
            if (SceneManager.sceneCount == 0)
            {
                ModLogger.LogWarning("No scenes loaded, cannot restore UI components");
                return;
            }
            
            // Restore GameScene UI components
            Scene gameScene = SceneManager.GetSceneByName("GameScene");
            if (gameScene.isLoaded)
            {
                GameObject[] rootObjects = gameScene.GetRootGameObjects();
                if (rootObjects != null)
                {
                    foreach (GameObject rootObj in rootObjects)
                    {
                        if (rootObj != null && rootObj.name == "Canvas")
                        {
                            Transform invUI = rootObj.transform.Find("INVUI");
                            if (invUI != null)
                            {
                                invUI.gameObject.SetActive(true);
                                ModLogger.LogInfo("Restored Canvas/INVUI");
                            }
                            
                            Transform lvlUpText = rootObj.transform.Find("lvluptext");
                            if (lvlUpText != null)
                            {
                                lvlUpText.gameObject.SetActive(true);
                                ModLogger.LogInfo("Restored Canvas/lvluptext");
                            }
                            break;
                        }
                    }
                }
            }
        }

        internal static IEnumerator KillSpectatorCoroutine(PlayerMovement pm)
        {
            ModLogger.LogInfo("KillSpectatorCoroutine started");
            
            // Validate player reference
            if (pm == null)
            {
                ModLogger.LogError("PlayerMovement is null in KillSpectatorCoroutine");
                yield break;
            }
            
            // Wait longer to ensure everything is fully initialized
            yield return new WaitForSeconds(2f);
            
            ModLogger.LogInfo($"Attempting to kill spectator player: {pm?.playername}");
            
            // Teleport player 100 units below current position before killing
            if (pm != null && pm.transform != null)
            {
                Vector3 currentPos = pm.transform.position;
                Vector3 newPos = new Vector3(currentPos.x, currentPos.y - 100f, currentPos.z);
                pm.transform.position = newPos;
                ModLogger.LogInfo($"Teleported player to {newPos} (100 units below original position)");
            }
            
            // Kill the player by setting health to 0 - let the game handle the rest
            if (IsPlayerAlive(pm))
            {
                ModLogger.LogInfo("Player is alive, setting health to 0 and marking as dead");
                SetPlayerHealth(pm, 0f);
                pm.isDead = true;
                ModLogger.LogInfo("Player killed - letting game systems handle death naturally");
            }
            else
            {
                ModLogger.LogInfo("Player is already dead");
            }
            
            // Hide UI components
            HideUIComponents();
            
            // Mark as spectator
            isSpectator = true;
            ModLogger.LogInfo("Spectator marked and killed");
            
            // Wait a moment for the death to register, then manually start spectating
            yield return new WaitForSeconds(1f);
            
            // Get the PlayerRespawnManager and start our custom spectate routine manually
            PlayerRespawnManager prm = GetPlayerRespawnManager(pm);
            if (prm == null)
            {
                // Fallback: find by tag
                GameObject netItemManager = GameObject.FindGameObjectWithTag("NetItemManager");
                if (netItemManager != null)
                {
                    prm = netItemManager.GetComponent<PlayerRespawnManager>();
                }
            }
            
            if (prm != null)
            {
                ModLogger.LogInfo("Manually starting custom spectate routine");
                prm.StartCoroutine(CustomSpectateCoroutine(prm));
            }
            else
            {
                ModLogger.LogError("Could not find PlayerRespawnManager to start spectating!");
            }
        }

        internal static IEnumerator CustomSpectateCoroutine(PlayerRespawnManager manager)
        {
            ModLogger.LogInfo("Starting custom spectate routine");
            
            // Validate manager reference
            if (manager == null)
            {
                ModLogger.LogError("PlayerRespawnManager is null in CustomSpectateCoroutine");
                yield break;
            }
            
            // Wait a bit for everything to settle
            yield return new WaitForSeconds(0.5f);
            
            // Find all players and start spectating the first alive one
            GameObject[] allPlayers = GameObject.FindGameObjectsWithTag("Player");
            if (allPlayers == null)
            {
                ModLogger.LogWarning("No players found for spectating (null array)");
                yield break;
            }
            
            ModLogger.LogInfo($"Found {allPlayers.Length} total players");
            
            if (allPlayers.Length == 0)
            {
                ModLogger.LogWarning("No players found for spectating");
                yield break;
            }

            // Log all players for debugging
            for (int i = 0; i < allPlayers.Length; i++)
            {
                if (allPlayers[i] != null)
                {
                    PlayerMovement pm = allPlayers[i].GetComponent<PlayerMovement>();
                    if (pm != null)
                    {
                        ModLogger.LogInfo($"Player {i}: {pm.playername}, IsOwner: {pm.IsOwner}, IsAlive: {IsPlayerAlive(pm)}");
                    }
                }
            }

            // Find first alive player regardless of team
            PlayerMovement firstAlivePlayer = null;
            for (int i = 0; i < allPlayers.Length; i++)
            {
                if (allPlayers[i] != null)
                {
                    PlayerMovement pm = allPlayers[i].GetComponent<PlayerMovement>();
                    if (pm != null && IsPlayerAlive(pm) && !IsSpectatorPlayer(pm))
                    {
                        firstAlivePlayer = pm;
                        currentPlayerIndex = i;
                        ModLogger.LogInfo($"Selected player {i}: {pm.playername} for spectating");
                        break;
                    }
                }
            }

            if (firstAlivePlayer == null)
            {
                ModLogger.LogWarning("No alive players found for spectating");
                yield break;
            }

            // Start spectating the first alive player
            ModLogger.LogInfo($"Starting to spectate {firstAlivePlayer.playername}");
            StartSpectating(firstAlivePlayer);
            
            // Main spectate loop
            while (isSpectator)
            {
                yield return null;
                
                UpdateSpectateCamera();
                // Check if current target is still alive
                if (spectateTarget == null || !IsPlayerAlive(spectateTarget))
                {
                    ModLogger.LogInfo("Current spectate target is no longer valid, cycling to next player");
                    // Find next alive player
                    CycleToNextPlayer();
                }
            }
        }

        internal static void StartSpectating(PlayerMovement targetPlayer)
        {
            if (targetPlayer == null)
            {
                ModLogger.LogError("Target player is null in StartSpectating");
                return;
            }
            
            if (targetPlayer.transform == null)
            {
                ModLogger.LogError("Target player transform is null in StartSpectating");
                return;
            }
            
            ModLogger.LogInfo($"Starting to spectate {targetPlayer.playername}");
            
            Camera mainCamera = Camera.main;
            if (mainCamera != null && spectateTarget == null) // Only store if this is the first spectate target
            {
                ModLogger.LogInfo($"Main camera found: {mainCamera.name}");
                originalCameraPosition = mainCamera.transform.position;
                originalCameraRotation = mainCamera.transform.rotation;
                originalCameraParent = mainCamera.transform.parent;
                ModLogger.LogInfo($"Stored original camera state - Pos: {originalCameraPosition}, Rot: {originalCameraRotation}, Parent: {originalCameraParent?.name ?? "None"}");
                
                // Unparent the camera so we can control it freely
                mainCamera.transform.SetParent(null);
                
                // Initialize camera angles for spectating
                freecamYaw = 0f;
                freecamPitch = 0f;
            }
            else if (mainCamera == null)
            {
                ModLogger.LogError("Main camera not found!");
                return;
            }
            
            // Set spectating target
            spectateTarget = targetPlayer;
            ModLogger.LogInfo($"Set spectate target to {targetPlayer.playername}");
            
            // Disable freecam if it was active
            if (isFreecamMode)
            {
                ModLogger.LogInfo("Disabling freecam before starting spectating");
                DisableFreecam();
            }
            
            // Reset camera angles and zoom for new target
            freecamYaw = 0f;
            freecamPitch = 0f;
            spectateDistance = configDefaultZoomDistance.Value; // Use config default
            
            ModLogger.LogInfo($"Reset camera angles and zoom for {targetPlayer.playername}");
        }

        internal static void ReloadConfig()
        {
            ModLogger.LogInfo("Reloading configuration...");
            ApplyConfig(); // Reloading config means applying it
            ModLogger.LogInfo("Configuration reloaded successfully");
        }

        internal static void UpdateSpectateCamera()
        {
            if (spectateTarget == null || isFreecamMode) return;
            
            if (spectateTarget.transform == null)
            {
                ModLogger.LogWarning("Spectate target transform is null, cannot update camera");
                return;
            }
            
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            
            // Use the player's SpectatePoint if available, otherwise use their transform
            Transform spectatePoint = spectateTarget.SpectatePoint;
            if (spectatePoint == null)
            {
                spectatePoint = spectateTarget.transform;
            }
            
            // Handle mouse input for camera control with invert options
            float mouseX = Input.GetAxis("Mouse X") * freecamSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * freecamSensitivity;

            // Apply invert settings
            if (invertMouseX) mouseX = -mouseX;
            if (invertMouseY) mouseY = -mouseY;
            
            // Handle scroll wheel zoom (use config speed)
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
            {
                spectateDistance -= scroll * configZoomSpeed.Value;
                spectateDistance = Mathf.Clamp(spectateDistance, minSpectateDistance, maxSpectateDistance);
            }
                
            // Update yaw (horizontal rotation around player)
            freecamYaw += mouseX;
            
            // Update pitch (vertical angle - up/down movement)
            freecamPitch -= mouseY / 2f;
            freecamPitch = Mathf.Clamp(freecamPitch, -60f, 60f); // Limit vertical rotation
            
            // Calculate camera position around the spectate point
            float angleInRadians = freecamYaw * 0.017453292f; // Convert to radians
            float pitchInRadians = freecamPitch * 0.017453292f; // Convert pitch to radians
            
            // Calculate the base horizontal offset
            Vector3 horizontalOffset = new Vector3(
                Mathf.Cos(angleInRadians) * spectateDistance, 
                0f, 
                Mathf.Sin(angleInRadians) * spectateDistance
            );
            
            // Apply vertical offset based on pitch
            float verticalOffset = spectateHeight + (Mathf.Sin(pitchInRadians) * spectateDistance);
            
            // Adjust horizontal distance based on pitch (when looking up/down, pull camera back slightly)
            float pitchCompensation = Mathf.Cos(pitchInRadians);
            horizontalOffset *= pitchCompensation;
            
            // Calculate target camera position and rotation
            targetCameraPosition = spectatePoint.position + Vector3.up * verticalOffset + horizontalOffset;
            Vector3 lookTarget = spectatePoint.position + Vector3.up * 1.5f; // Look slightly above player's feet
            targetCameraRotation = Quaternion.LookRotation(lookTarget - targetCameraPosition);

            // Apply smoothing to camera movement
            if (cameraSmoothing > 0f)
            {
                mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, targetCameraPosition, Time.deltaTime * cameraSmoothing);
                mainCamera.transform.rotation = Quaternion.Lerp(mainCamera.transform.rotation, targetCameraRotation, Time.deltaTime * cameraSmoothing);
            }
            else
            {
                // No smoothing - instant movement
                mainCamera.transform.position = targetCameraPosition;
                mainCamera.transform.rotation = targetCameraRotation;
            }
        }

        internal static void StopSpectating()
        {
            ModLogger.LogInfo("Stopping spectating");
            
            // Restore original camera state
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                if (mainCamera.transform == null)
                {
                    ModLogger.LogError("Main camera transform is null when stopping spectating");
                    return;
                }
                
                mainCamera.transform.position = originalCameraPosition;
                mainCamera.transform.rotation = originalCameraRotation;
                if (originalCameraParent != null)
                {
                    mainCamera.transform.SetParent(originalCameraParent);
                }
            }
            
            // Reset spectating state
            spectateTarget = null;
            isSpectator = false;
        }

        internal static void CycleToNextPlayer()
        {
            GameObject[] allPlayers = GameObject.FindGameObjectsWithTag("Player");
            if (allPlayers == null || allPlayers.Length == 0) return;
            
            // Find next alive player regardless of team
            int attempts = 0;
            do
            {
                currentPlayerIndex = (currentPlayerIndex + 1) % allPlayers.Length;
                if (allPlayers[currentPlayerIndex] != null)
                {
                    PlayerMovement pm = allPlayers[currentPlayerIndex].GetComponent<PlayerMovement>();
                    
                    if (pm != null && IsPlayerAlive(pm) && !IsSpectatorPlayer(pm))
                    {
                        StartSpectating(pm);
                        return;
                    }
                }
                
                attempts++;
            } while (attempts < allPlayers.Length);
            
            ModLogger.LogWarning("No alive players found to spectate");
        }

        internal static void ResetAllStates()
        {
            // Only allow state reset if the current player is the host
            if (!IsCurrentPlayerHost())
            {
                return;
            }
            
            ModLogger.LogInfo("Resetting all spectator and freecam states...");
            StopSpectating();
            DisableFreecam();
            RestoreUIComponents();
            isSpectator = false;
            isFreecamMode = false;
            spectateTarget = null;
            currentPlayerIndex = 0;
            ModLogger.LogInfo("All states reset.");
        }

        #endregion

        #region Freecam System

        internal static void ToggleFreecam()
        {
            if (isFreecamMode)
            {
                DisableFreecam();
            }
            else
            {
                EnableFreecam();
            }
        }

        internal static void EnableFreecam()
        {
            if (isFreecamMode) return;
            
            ModLogger.LogInfo("Enabling freecam mode");
            
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                ModLogger.LogError("Could not find main camera for freecam");
                return;
            }
            
            if (mainCamera.transform == null)
            {
                ModLogger.LogError("Main camera transform is null for freecam");
                return;
            }
            
            // Store current camera position and rotation
            freecamPosition = mainCamera.transform.position;
            freecamYaw = mainCamera.transform.eulerAngles.y;
            freecamPitch = mainCamera.transform.eulerAngles.x;
            
            // Unparent camera
            mainCamera.transform.SetParent(null);
            
            // Enable freecam
            isFreecamMode = true;
            
            ModLogger.LogInfo($"Freecam enabled at position {freecamPosition}");
        }

        internal static void DisableFreecam()
        {
            if (!isFreecamMode) return;
            
            ModLogger.LogInfo("Disabling freecam mode");
            
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                if (mainCamera.transform == null)
                {
                    ModLogger.LogError("Main camera transform is null when disabling freecam");
                    return;
                }
                
                // Restore camera parent if we have a spectate target
                if (spectateTarget != null)
                {
                    // Return to normal spectating mode
                    UpdateSpectateCamera();
                }
                else
                {
                    // Restore original camera state
                    mainCamera.transform.position = originalCameraPosition;
                    mainCamera.transform.rotation = originalCameraRotation;
                    if (originalCameraParent != null)
                    {
                        mainCamera.transform.SetParent(originalCameraParent);
                    }
                }
            }
            
            // Disable freecam
            isFreecamMode = false;
            
            ModLogger.LogInfo("Freecam disabled");
        }

        internal static void UpdateFreecam()
        {
            if (!isFreecamMode) return;
            
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            
            if (mainCamera.transform == null)
            {
                ModLogger.LogError("Main camera transform is null in UpdateFreecam");
                return;
            }
            
            // Handle mouse look
            float mouseX = Input.GetAxis("Mouse X") * freecamSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * freecamSensitivity;
            
            freecamYaw += mouseX;
            freecamPitch -= mouseY;
            freecamPitch = Mathf.Clamp(freecamPitch, -80f, 80f);
            
            // Handle movement
            float currentSpeed = freecamSpeed;
            if (Input.GetKey(KeyCode.LeftShift))
            {
                currentSpeed *= 2.5f;
            }
            if (Input.GetKey(KeyCode.LeftControl))
            {
                currentSpeed *= 0.5f;
            }
            
            Vector3 moveDirection = Vector3.zero;
            
            if (Input.GetKey(KeyCode.W))
                moveDirection += Vector3.forward;
            if (Input.GetKey(KeyCode.S))
                moveDirection += Vector3.back;
            if (Input.GetKey(KeyCode.A))
                moveDirection += Vector3.left;
            if (Input.GetKey(KeyCode.D))
                moveDirection += Vector3.right;
            if (Input.GetKey(KeyCode.Space))
                moveDirection += Vector3.up;
            if (Input.GetKey(KeyCode.LeftAlt))
                moveDirection += Vector3.down;
            
            // Apply movement
            if (moveDirection.magnitude > 0)
            {
                moveDirection = mainCamera.transform.TransformDirection(moveDirection.normalized);
                freecamPosition += moveDirection * currentSpeed * Time.deltaTime;
            }
            
            // Update camera
            mainCamera.transform.position = freecamPosition;
            mainCamera.transform.rotation = Quaternion.Euler(freecamPitch, freecamYaw, 0f);
        }

        #endregion
    }
}