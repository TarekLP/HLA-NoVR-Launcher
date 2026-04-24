using HLA_NoVRLauncher_Avalonia.Services;

namespace HLA_NoVRLauncher_Avalonia.ViewModels
{
    /// <summary>
    /// Carries the reason the user was blocked so PiracyView can show
    /// the right message and buttons for each case.
    /// </summary>
    public class PiracyViewModel : ViewModelBase
    {
        public OwnershipResult Reason { get; }

        // Heading shown on the piracy page
        public string Heading => Reason == OwnershipResult.SteamNotRunning
            ? "Steam Is Not Running"
            : "Game Not Owned";

        // Body text
        public string Body => Reason == OwnershipResult.SteamNotRunning
            ? "HLA NoVR needs Steam running to verify your copy of Half-Life: Alyx.\n\nPlease start Steam and click Re-check."
            : "Half-Life: Alyx was not found on this Steam account.\nHLA NoVR requires a legitimate copy of the game.";

        // Only show the "View on Steam Store" button when the game isn't owned
        public bool ShowStoreButton => Reason == OwnershipResult.NotOwned;

        public PiracyViewModel(OwnershipResult reason)
        {
            Reason = reason;
        }
    }
}
