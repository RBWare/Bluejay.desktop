using Grayjay.ClientServer.Models;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class ForYouController : ControllerBase
    {
        public class ForYouState
        {
            public IPager<PlatformContent> ForYouPager { get; set; }
        }

        private IPager<PlatformContent> EnsureForYouPager() => this.State().ForYouState.ForYouPager ?? throw new BadHttpRequestException("No For You feed loaded");

        /// <summary>
        /// Loads the For You feed, reusing the last built feed unless it is stale or a refresh is requested.
        /// </summary>
        [HttpGet]
        public async Task<PagerResult<PlatformVideo>> ForYouLoad(bool refresh)
        {
            await StatePlatform.WaitForStartup();
            var pager = (!refresh && !StateForYou.IsStale() ? StateForYou.GetCachedPager() : null)
                ?? await StateForYou.BuildFeed(refresh);
            this.State().ForYouState.ForYouPager = pager;
            return pager.AsPagerResult(IsVisible, y => StateHistory.AddVideoMetadata((PlatformVideo)y));
        }

        //Pages of an already built feed may still contain a creator hidden or a video watched since
        private static bool IsVisible(PlatformContent x) => x is PlatformVideo v
            && !StateForYou.IsCreatorHidden(v.Author?.Url)
            && !(v.Duration > 0 && StateHistory.IsHistoryWatched(v.Url, v.Duration));

        [HttpGet]
        public bool HideCreator(string url)
        {
            StateForYou.HideCreator(url);
            return true;
        }

        [HttpGet]
        public int HiddenCreatorCount() => StateForYou.HiddenCreatorCount();

        [HttpGet]
        public bool ClearHiddenCreators()
        {
            StateForYou.ClearHiddenCreators();
            return true;
        }

        [HttpGet]
        public PagerResult<PlatformVideo> ForYouNextPage()
        {
            try
            {
                var pager = EnsureForYouPager();
                lock (pager)
                {
                    pager.NextPage();
                    return pager.AsPagerResult(IsVisible, y => StateHistory.AddVideoMetadata((PlatformVideo)y));
                }
            }
            catch (Exception ex)
            {
                return new PagerResult<PlatformVideo>()
                {
                    Results = new PlatformVideo[0],
                    HasMore = false,
                    Exception = ex.Message
                };
            }
        }
    }
}
