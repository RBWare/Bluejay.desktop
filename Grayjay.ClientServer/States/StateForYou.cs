using System.Collections.Concurrent;
using Grayjay.ClientServer.Models.History;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC.Port.States;
using Grayjay.Engine.Models.Detail;
using Grayjay.Engine.Models.Feed;
using Grayjay.Engine.Pagers;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.States
{
    /// <summary>
    /// Builds the "For You" feed: subscription content ranked by how much the user engages with each
    /// channel, how fresh it is and how well it performs relative to the channel's other uploads,
    /// mixed with discovery content taken from recommendations of recently watched videos.
    /// </summary>
    public static class StateForYou
    {
        private const int PAGE_SIZE = 20;
        private const int MAX_CACHE_PAGES = 50;
        private const int MAX_LIVE_PAGES = 5;
        private const int MAX_SUBSCRIPTION_CANDIDATES = 500;
        private const int MAX_AGE_DAYS = 30;
        private const int HISTORY_DAYS = 30;
        private const double FRESHNESS_TAU_HOURS = 72.0;
        private const double AUTHOR_REPEAT_PENALTY = 0.6;
        private const int FEED_STALE_MINUTES = 15;

        private const int MAX_DISCOVERY_SEEDS = 6;
        private const int MAX_RECOMMENDATIONS_PER_SEED = 10;
        private const int DISCOVERY_TIMEOUT_MS = 20_000;
        private const int DISCOVERY_STALE_MINUTES = 60;
        private const int DISCOVERY_FIRST_SLOT = 3;
        private const int DISCOVERY_INTERVAL = 4;

        private static readonly object _lock = new object();

        private static List<PlatformContent> _feed = new List<PlatformContent>();
        private static DateTime _feedTime = DateTime.MinValue;

        private static List<PlatformVideo> _discovery = new List<PlatformVideo>();
        private static DateTime _discoveryTime = DateTime.MinValue;

        //Creators the user chose to hide from For You, persisted across restarts
        private static readonly StringArrayStore _hiddenCreators = new StringArrayStore("hiddenCreators", Array.Empty<string>()).Load();

        static StateForYou()
        {
            StateSubscriptions.OnSubscriptionsChanged += (_, _) => Clear();
        }

        public static bool IsCreatorHidden(string? creatorUrl) => !string.IsNullOrEmpty(creatorUrl) && _hiddenCreators.Contains(creatorUrl);
        public static int HiddenCreatorCount() => _hiddenCreators.GetCopy().Length;

        public static void HideCreator(string creatorUrl)
        {
            if (string.IsNullOrEmpty(creatorUrl))
                return;
            if (!_hiddenCreators.Contains(creatorUrl))
                _hiddenCreators.AddSave(creatorUrl);
            //Drop the creator from the cached feed so it is gone without rebuilding
            lock (_lock)
            {
                _feed = _feed.Where(x => x.Author?.Url != creatorUrl).ToList();
                _discovery = _discovery.Where(x => x.Author?.Url != creatorUrl).ToList();
            }
        }

        public static void ClearHiddenCreators()
        {
            _hiddenCreators.Save(Array.Empty<string>());
            //Hidden creators were filtered out while ranking, rebuild so they come back
            lock (_lock)
            {
                _feed = new List<PlatformContent>();
                _feedTime = DateTime.MinValue;
            }
        }

        public static bool IsStale()
        {
            lock (_lock)
                return _feed.Count == 0 || (DateTime.Now - _feedTime).TotalMinutes > FEED_STALE_MINUTES;
        }

        public static IPager<PlatformContent>? GetCachedPager()
        {
            lock (_lock)
                return _feed.Count == 0 ? null : CreatePager(_feed);
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _feed = new List<PlatformContent>();
                _feedTime = DateTime.MinValue;
                _discovery = new List<PlatformVideo>();
                _discoveryTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Rebuilds the feed. With withRefresh, subscriptions are refetched and discovery recommendations are requested again.
        /// </summary>
        public static async Task<IPager<PlatformContent>> BuildFeed(bool withRefresh)
        {
            var now = DateTime.Now;
            var history = StateHistory.GetRecentHistory(now.AddDays(-HISTORY_DAYS), 1000);

            var candidates = new Dictionary<string, PlatformContent>();
            var order = new List<string>();
            if (withRefresh)
            {
                try
                {
                    //Fresh items are cached asynchronously, so read the newest ones directly. Limited, deeper pages hit the network.
                    var livePager = await StateSubscriptions.GetGlobalSubscriptionFeed(true);
                    Collect(livePager, candidates, order, now, MAX_SUBSCRIPTION_CANDIDATES, MAX_LIVE_PAGES);
                }
                catch (Exception ex)
                {
                    Logger.w(nameof(StateForYou), "Failed to refresh subscriptions, falling back to cache", ex);
                }
            }
            Collect(StateCache.GetSubscriptionCachePager(), candidates, order, now, MAX_SUBSCRIPTION_CANDIDATES, MAX_CACHE_PAGES);

            var ranked = RankSubscriptionContent(order.Select(x => candidates[x]).ToList(), history, now);

            var discovery = await GetDiscovery(history, withRefresh, ranked.Select(x => x.Url).ToHashSet());
            var feed = Interleave(ranked, discovery);

            Logger.i(nameof(StateForYou), $"Built For You feed ({ranked.Count} subscription items, {discovery.Count} discovery items)");
            lock (_lock)
            {
                _feed = feed;
                _feedTime = DateTime.Now;
            }
            return CreatePager(feed);
        }

        private static void Collect(IPager<PlatformContent> pager, Dictionary<string, PlatformContent> into, List<string> order, DateTime now, int max, int maxPages)
        {
            var minDate = now.AddDays(-MAX_AGE_DAYS);
            var pages = 0;
            while (true)
            {
                var reachedEnd = false;
                foreach (var item in pager.GetResults())
                {
                    if (item.DateTime < minDate && item.DateTime > DateTime.MinValue)
                    {
                        reachedEnd = true;
                        continue;
                    }
                    if (item is not PlatformVideo || string.IsNullOrEmpty(item.Url) || IsCreatorHidden(item.Author?.Url))
                        continue;
                    if (item.DateTime > now.AddMinutes(5))
                        continue;
                    if (into.TryAdd(item.Url, item))
                        order.Add(item.Url);
                }
                pages++;
                if (reachedEnd || into.Count >= max || pages >= maxPages || !pager.HasMorePages())
                    break;
                pager.NextPage();
            }
        }

        private static List<PlatformContent> RankSubscriptionContent(List<PlatformContent> items, List<HistoryVideo> history, DateTime now)
        {
            var random = new Random();

            //Channel affinity: lifetime playback tracked on the subscription plus recent watch history
            var recentWatches = history
                .Where(x => x.Video?.Author?.Url != null)
                .GroupBy(x => x.Video.Author.Url)
                .ToDictionary(x => x.Key, x => x.Count());
            var rawAffinity = new Dictionary<string, double>();
            foreach (var item in items)
            {
                var url = item.Author?.Url ?? "";
                if (rawAffinity.ContainsKey(url))
                    continue;
                var sub = StateSubscriptions.GetSubscription(url);
                var playbackMinutes = (sub?.PlaybackSeconds ?? 0) / 60.0;
                var views = (double)(sub?.PlaybackViews ?? 0);
                var recent = (double)recentWatches.GetValueOrDefault(url);
                rawAffinity[url] = Math.Log(1.0 + playbackMinutes + views * 5.0 + recent * 10.0);
            }
            var maxAffinity = rawAffinity.Values.DefaultIfEmpty(0).Max();
            if (maxAffinity <= 0)
                maxAffinity = 1.0;

            //Performance relative to channel: view velocity compared to the channel's median velocity
            var velocities = new Dictionary<string, double>();
            foreach (var item in items)
            {
                var v = Velocity(item, now);
                if (v != null)
                    velocities[item.Url] = v.Value;
            }
            var channelMedians = items.GroupBy(x => x.Author?.Url ?? "").ToDictionary(x => x.Key, x =>
            {
                var sorted = x.Where(y => velocities.ContainsKey(y.Url)).Select(y => velocities[y.Url]).OrderBy(y => y).ToList();
                return sorted.Count == 0 ? (double?)null : sorted[sorted.Count / 2];
            });

            var scored = new List<(PlatformContent, double)>();
            foreach (var item in items)
            {
                var video = (PlatformVideo)item;
                var position = StateHistory.GetHistoryPosition(item.Url);
                if (video.Duration > 0 && StateHistory.IsHistoryWatched(item.Url, video.Duration))
                    continue;

                var ageHours = item.DateTime > DateTime.MinValue ? Math.Max(0, (now - item.DateTime).TotalHours) : MAX_AGE_DAYS * 24.0;
                var freshness = 0.15 + 0.85 * Math.Exp(-ageHours / FRESHNESS_TAU_HOURS);

                var affinity = 0.35 + 0.65 * (rawAffinity.GetValueOrDefault(item.Author?.Url ?? "") / maxAffinity);

                var median = channelMedians.GetValueOrDefault(item.Author?.Url ?? "");
                var performance = 1.0;
                if (median != null && median > 0 && velocities.TryGetValue(item.Url, out var velocity))
                    performance = 1.0 + 0.25 * Math.Clamp(Math.Log2(velocity / median.Value), -1.0, 1.5);

                var score = freshness * affinity * performance * (0.85 + random.NextDouble() * 0.3);
                if (video.IsLive)
                    score *= 1.2;
                if (position > 0)
                    score *= 0.7;
                scored.Add((item, score));
            }

            return Diversify(scored);
        }

        private static double? Velocity(PlatformContent item, DateTime now)
        {
            if (item is not PlatformVideo video || video.ViewCount <= 0 || item.DateTime <= DateTime.MinValue)
                return null;
            var ageHours = Math.Max(0, (now - item.DateTime).TotalHours);
            return video.ViewCount / Math.Sqrt(ageHours + 2.0);
        }

        /// <summary>
        /// Greedily picks the best remaining item while penalizing authors that were already picked,
        /// so a single prolific channel cannot dominate the top of the feed.
        /// </summary>
        private static List<T> Diversify<T>(List<(T, double)> scored) where T : PlatformContent
        {
            var remaining = scored.OrderByDescending(x => x.Item2).ToList();
            var picked = new Dictionary<string, int>();
            var result = new List<T>(remaining.Count);
            while (remaining.Count > 0)
            {
                var bestIndex = 0;
                var bestScore = double.NegativeInfinity;
                for (int i = 0; i < remaining.Count; i++)
                {
                    var (item, score) = remaining[i];
                    //Sorted by raw score, nothing further down can beat the current best after penalties
                    if (score <= bestScore)
                        break;
                    var adjusted = score * Math.Pow(AUTHOR_REPEAT_PENALTY, picked.GetValueOrDefault(item.Author?.Url ?? ""));
                    if (adjusted > bestScore)
                    {
                        bestScore = adjusted;
                        bestIndex = i;
                    }
                }
                var chosen = remaining[bestIndex].Item1;
                remaining.RemoveAt(bestIndex);
                var author = chosen.Author?.Url ?? "";
                picked[author] = picked.GetValueOrDefault(author) + 1;
                result.Add(chosen);
            }
            return result;
        }

        private static async Task<List<PlatformVideo>> GetDiscovery(List<HistoryVideo> history, bool withRefresh, HashSet<string> excludeUrls)
        {
            List<PlatformVideo>? discovery = null;
            lock (_lock)
            {
                if (!withRefresh && _discovery.Count > 0 && (DateTime.Now - _discoveryTime).TotalMinutes < DISCOVERY_STALE_MINUTES)
                    discovery = _discovery;
            }
            if (discovery == null)
            {
                discovery = await FetchDiscovery(history);
                lock (_lock)
                {
                    _discovery = discovery;
                    _discoveryTime = DateTime.Now;
                }
            }

            //Re-filter cached results, user may have watched or subscribed since they were fetched
            return discovery.Where(x => IsDiscoveryCandidate(x) && !excludeUrls.Contains(x.Url)).ToList();
        }

        private static async Task<List<PlatformVideo>> FetchDiscovery(List<HistoryVideo> history)
        {
            //Seeds: recently watched videos the user engaged with, at most one per channel
            var seeds = history
                .Where(x => x.Video?.Url != null && (x.Position >= 60 || (x.Video.Duration > 0 && x.Position >= x.Video.Duration * 0.3)))
                .DistinctBy(x => x.Video.Author?.Url ?? "")
                .Take(MAX_DISCOVERY_SEEDS)
                .ToList();
            if (seeds.Count == 0)
                return new List<PlatformVideo>();

            var now = DateTime.Now;
            var results = new ConcurrentDictionary<int, List<PlatformVideo>>();
            var tasks = seeds.Select((seed, index) => Task.Run(() =>
            {
                try
                {
                    if (StatePlatform.GetContentDetails(seed.Video.Url) is not PlatformVideoDetails details)
                        return;
                    var pager = details.GetContentRecommendations();
                    if (pager == null)
                        return;
                    results[index] = pager.GetResults().OfType<PlatformVideo>().Take(MAX_RECOMMENDATIONS_PER_SEED).ToList();
                }
                catch (Exception ex)
                {
                    Logger.w(nameof(StateForYou), $"Failed to get recommendations for {seed.Video.Url}", ex);
                }
            })).ToArray();
            //Plugin calls are blocking and cannot be interrupted, use whatever finished in time
            await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(DISCOVERY_TIMEOUT_MS));

            var scored = new Dictionary<string, (PlatformVideo, double)>();
            foreach (var (index, recommendations) in results)
            {
                var seed = seeds[index];
                var seedDays = Math.Max(0, (now - seed.Date).TotalDays);
                var seedWeight = Math.Exp(-seedDays / 7.0);
                for (int rank = 0; rank < recommendations.Count; rank++)
                {
                    var video = recommendations[rank];
                    if (string.IsNullOrEmpty(video.Url) || !IsDiscoveryCandidate(video))
                        continue;
                    var score = seedWeight / (1.0 + rank * 0.15);
                    //Recommended from multiple seeds is a stronger signal
                    var existing = scored.TryGetValue(video.Url, out var e) ? e.Item2 : 0.0;
                    scored[video.Url] = (video, existing + score);
                }
            }
            return Diversify(scored.Values.ToList());
        }

        private static bool IsDiscoveryCandidate(PlatformVideo video)
        {
            if (string.IsNullOrEmpty(video.Author?.Url) || IsCreatorHidden(video.Author.Url))
                return false;
            if (StateSubscriptions.IsSubscribed(video.Author.Url))
                return false;
            //Anything the user already started is not a discovery
            return StateHistory.GetHistoryPosition(video.Url) <= 0;
        }

        private static List<PlatformContent> Interleave(List<PlatformContent> subscriptions, List<PlatformVideo> discovery)
        {
            var result = new List<PlatformContent>(subscriptions.Count + discovery.Count);
            var seen = new HashSet<string>();
            int discoveryIndex = 0, subscriptionIndex = 0;
            while (subscriptionIndex < subscriptions.Count || discoveryIndex < discovery.Count)
            {
                var isDiscoverySlot = result.Count >= DISCOVERY_FIRST_SLOT && (result.Count - DISCOVERY_FIRST_SLOT) % DISCOVERY_INTERVAL == 0;
                PlatformContent next = ((isDiscoverySlot || subscriptionIndex >= subscriptions.Count) && discoveryIndex < discovery.Count)
                    ? discovery[discoveryIndex++]
                    : subscriptions[subscriptionIndex++];
                if (seen.Add(next.Url))
                    result.Add(next);
            }
            return result;
        }

        private static IPager<PlatformContent> CreatePager(List<PlatformContent> items)
        {
            return new AdhocPager<PlatformContent>(page => items.Skip(page * PAGE_SIZE).Take(PAGE_SIZE).ToArray(), items.Take(PAGE_SIZE).ToArray());
        }
    }
}
