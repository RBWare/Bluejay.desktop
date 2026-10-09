import { createResource, createSignal, type Component, Show } from 'solid-js';

import styles from './index.module.css';
import { ForYouBackend } from '../../backend/ForYouBackend';
import ContentGrid from '../../components/containers/ContentGrid';
import NavigationBar from '../../components/topbars/NavigationBar';
import ScrollContainer from '../../components/containers/ScrollContainer';
import IconButton from '../../components/buttons/IconButton';
import Button from '../../components/buttons/Button';
import { IPlatformContent } from '../../backend/models/content/IPlatformContent';
import LoaderGrid from '../../components/basics/loaders/LoaderGrid';
import EmptyContentView from '../../components/EmptyContentView';

import iconRefresh from "../../assets/icons/icon_reload_temp.svg"
import iconForYou from "../../assets/icons/icon_nav_for_you.svg"
import iconCreators from "../../assets/icons/icon_nav_creators.svg"
import { useNavigate } from '@solidjs/router';
import { focusable } from '../../focusable'; void focusable;

/**
 * Subscription content ranked by channel affinity, freshness and performance, mixed with
 * discovery picks from recommendations of recently watched videos.
 */
const ForYouPage: Component = () => {
  const nav = useNavigate();
  //Opening the page reuses the last built feed, the refresh button refetches subscriptions and recommendations
  const [refresh$, setRefresh] = createSignal({ refresh: false });
  const [forYouPager$] = createResource(refresh$, async (r) => await ForYouBackend.forYouPager(r.refresh));
  const reload = () => setRefresh({ refresh: true });

  //Hidden creators disappear from the grid immediately, the backend also drops them from the feed
  const [hiddenCount$, { refetch: refetchHiddenCount }] = createResource(async () => await ForYouBackend.hiddenCreatorCount());
  const hiddenThisSession = new Set<string>();
  const watchedThisSession = new Set<string>();
  const applyFilter = () => forYouPager$()?.setFilter((item) => !hiddenThisSession.has(item.author?.url) && !watchedThisSession.has(item.url));
  const hideCreator = async (content: IPlatformContent) => {
    const url = content.author?.url;
    if (!url)
      return;
    await ForYouBackend.hideCreator(url);
    hiddenThisSession.add(url);
    applyFilter();
    refetchHiddenCount();
  };
  //For You leaves out watched videos, so a video marked as watched disappears right away
  const markedWatched = (content: IPlatformContent) => {
    watchedThisSession.add(content.url);
    applyFilter();
  };
  const unhideCreators = async () => {
    await ForYouBackend.clearHiddenCreators();
    hiddenThisSession.clear();
    refetchHiddenCount();
    setRefresh({ refresh: false });
  };

  let scrollContainerRef: HTMLDivElement | undefined;
  return (
    <div class={styles.container}>
        <NavigationBar isRoot={true} childrenAfter={
          <IconButton
            icon={iconRefresh}
            variant="none"
            shape="circle"
            width="30px"
            height="30px"
            iconInset="0px"
            style={{ "margin-left": "24px" }}
            onClick={reload}
            focusableOpts={{
              groupId: 'nav-bar',
              groupIndices: [1],
              groupType: 'horizontal',
              onPress: reload,
            }}
          />
        } />
        <Show when={(hiddenCount$() ?? 0) > 0}>
          <div style={{ "display": "flex", "margin-left": "24px", "margin-bottom": "12px" }}>
            <Button
              text={`Unhide ${hiddenCount$()} hidden creator${hiddenCount$() === 1 ? '' : 's'}`}
              small={true}
              onClick={unhideCreators}
              focusableOpts={{ onPress: unhideCreators }}
            />
          </div>
        </Show>
        <Show when={forYouPager$.loading}>
          <LoaderGrid />
        </Show>
        <Show when={!forYouPager$.loading && forYouPager$.state == 'ready'}>
          <Show when={forYouPager$() && forYouPager$()!.data.length > 0}>
            <ScrollContainer ref={scrollContainerRef}>
              <ContentGrid pager={forYouPager$()} outerContainerRef={scrollContainerRef} openChannelButton={true} onHideCreator={hideCreator} onMarkedWatched={markedWatched} />
            </ScrollContainer>
          </Show>
          <Show when={forYouPager$() && forYouPager$()!.data.length == 0}>
            <EmptyContentView icon={iconForYou} title='Nothing for you yet' description='For You recommends content based on your subscriptions and watch history. Subscribe to some creators to get started.' actions={[
              {
                icon: iconCreators,
                title: "Go to Creators",
                action: () => nav("/web/creators")
              }
            ]} />
          </Show>
        </Show>
    </div>
  );
};

export default ForYouPage;
