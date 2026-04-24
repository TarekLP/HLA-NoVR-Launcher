using System;
using System.IO;
using Steamworks;

namespace HLA_NoVRLauncher_Avalonia.Services
{
    /// <summary>
    /// Four possible outcomes of the ownership check:
    ///
    ///   Owned          — Steam running, user owns HLA → allow
    ///   NotOwned       — Steam running, user does NOT own HLA → block + write marker
    ///   SteamNotRunning — Steamworks DLL found but Init() failed → block, no marker
    ///                    (user can start Steam and re-check without reinstalling)
    ///   Inconclusive   — Steamworks DLL missing entirely → allow
    ///                    (can't check, don't punish)
    /// </summary>
    public enum OwnershipResult
    {
        Owned,
        NotOwned,
        SteamNotRunning,
        Inconclusive
    }

    public static class OwnershipChecker
    {
        private const uint AlyxAppId = 546560;

        private static readonly string MarkerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".novr");

        private static readonly string SteamAppIdPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "steam_appid.txt");

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the ownership result. Callers decide what to do with each state.
        /// Note: does NOT write the piracy marker — caller is responsible for that
        /// so the UI can decide whether to write it.
        /// </summary>
        public static OwnershipResult Check()
        {
            // Fast path: permanent marker already written
            if (File.Exists(MarkerPath))
            {
                Console.WriteLine("[Ownership] Piracy marker found.");
                return OwnershipResult.NotOwned;
            }

            return RunSteamCheck();
        }

        /// <summary>Writes the permanent piracy marker to the user's home folder.</summary>
        public static void WriteMarker()
        {
            try
            {
                File.WriteAllText(MarkerPath, string.Empty);
                File.SetAttributes(MarkerPath, FileAttributes.Hidden | FileAttributes.ReadOnly);
                Console.WriteLine($"[Ownership] Piracy marker written: {MarkerPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ownership] Failed to write marker: {ex.Message}");
            }
        }

        /// <summary>Removes the piracy marker so the check runs fresh next launch.</summary>
        public static void RemoveMarker()
        {
            try
            {
                if (!File.Exists(MarkerPath)) return;
                File.SetAttributes(MarkerPath, FileAttributes.Normal);
                File.Delete(MarkerPath);
                Console.WriteLine("[Ownership] Marker removed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ownership] Failed to remove marker: {ex.Message}");
            }
        }

        public static bool MarkerExists() => File.Exists(MarkerPath);

        // -----------------------------------------------------------------------
        // Private
        // -----------------------------------------------------------------------

        private static OwnershipResult RunSteamCheck()
        {
            try
            {
                EnsureSteamAppIdFile();

                // If Init() fails here, the DLL loaded fine but Steam isn't running
                if (!SteamAPI.Init())
                {
                    Console.WriteLine("[Ownership] SteamAPI.Init() failed — Steam not running.");
                    return OwnershipResult.SteamNotRunning;
                }

                bool owns = SteamApps.BIsSubscribedApp((AppId_t)AlyxAppId);
                SteamAPI.Shutdown();

                Console.WriteLine($"[Ownership] BIsSubscribedApp({AlyxAppId}) = {owns}");
                return owns ? OwnershipResult.Owned : OwnershipResult.NotOwned;
            }
            catch (DllNotFoundException ex)
            {
                // Steamworks DLL not shipped with the launcher — can't check at all
                Console.WriteLine($"[Ownership] Steamworks DLL missing: {ex.Message}");
                return OwnershipResult.Inconclusive;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ownership] Unexpected error: {ex.Message}");
                return OwnershipResult.Inconclusive;
            }
        }

        private static void EnsureSteamAppIdFile()
        {
            try
            {
                if (!File.Exists(SteamAppIdPath))
                    File.WriteAllText(SteamAppIdPath, "546560");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ownership] Could not write steam_appid.txt: {ex.Message}");
            }
        }
    }
}
