using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using JustCef;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.Desktop.CEF
{
    /// <summary>
    /// Applies the TV friendly interface size (Settings > Interface > Interface Size) to an app window,
    /// on every load of the interface and whenever the setting changes.
    /// </summary>
    public static class InterfaceZoom
    {
        public static void Attach(JustCefWindow window, Func<double> getZoomLevel)
        {
            async Task Apply()
            {
                try
                {
                    await window.SetZoomAsync(getZoomLevel());
                }
                catch (Exception ex)
                {
                    Logger.w(nameof(InterfaceZoom), "Failed to set interface zoom", ex);
                }
            }

            window.OnFrameLoadEnd += async (frameLoadInfo) =>
            {
                if (frameLoadInfo.Url != null && frameLoadInfo.Url.EndsWith("/web/index.html"))
                    await Apply();
            };

            Action<GrayjaySettings> onSettingsChanged = (settings) => _ = Apply();
            StateApp.OnSettingsChanged += onSettingsChanged;
            window.OnClose += () => StateApp.OnSettingsChanged -= onSettingsChanged;
        }

        public static double SettingsZoomLevel() => GrayjaySettings.Instance.Interface.GetZoomLevel();
    }
}
