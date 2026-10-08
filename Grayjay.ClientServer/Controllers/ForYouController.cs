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
            return pager.AsPagerResult(x => x is PlatformVideo, y => StateHistory.AddVideoMetadata((PlatformVideo)y));
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
                    return pager.AsPagerResult(x => x is PlatformVideo, y => StateHistory.AddVideoMetadata((PlatformVideo)y));
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
