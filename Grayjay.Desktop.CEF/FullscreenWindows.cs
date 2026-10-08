using JustCef;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.Desktop.CEF
{
    /// <summary>
    /// Bluejay is meant for a TV, so app windows always stay fullscreen.
    /// </summary>
    public static class FullscreenWindows
    {
        /// <summary>
        /// Puts the window back into fullscreen whenever something takes it out, e.g. the video player leaving element fullscreen.
        /// </summary>
        public static void KeepFullscreen(JustCefWindow window)
        {
            window.OnFullscreenChanged += async (fullscreen) =>
            {
                if (fullscreen)
                    return;
                try
                {
                    await window.SetFullscreenAsync(true);
                }
                catch (Exception ex)
                {
                    Logger.w(nameof(FullscreenWindows), "Failed to restore fullscreen", ex);
                }
            };
        }
    }
}
