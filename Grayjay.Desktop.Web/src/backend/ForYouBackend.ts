import { Backend } from "./Backend";
import { IPlatformContent } from "./models/content/IPlatformContent";
import { Pager } from "./models/pagers/Pager";

export abstract class ForYouBackend {

    static async forYouLoad(refresh: boolean = false): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/foryou/ForYouLoad?refresh=" + refresh) as PagerResult<IPlatformContent>;
    }

    static async forYouNextPage(): Promise<PagerResult<IPlatformContent>> {
        return await Backend.GET("/foryou/ForYouNextPage") as PagerResult<IPlatformContent>;
    }

    static async hideCreator(url: string): Promise<boolean> {
        return await Backend.GET("/foryou/HideCreator?url=" + encodeURIComponent(url)) as boolean;
    }

    static async hiddenCreatorCount(): Promise<number> {
        return await Backend.GET("/foryou/HiddenCreatorCount") as number;
    }

    static async clearHiddenCreators(): Promise<boolean> {
        return await Backend.GET("/foryou/ClearHiddenCreators") as boolean;
    }

    static async forYouPager(refresh: boolean = false): Promise<Pager<IPlatformContent>> {
        return await Pager.fromMethods<IPlatformContent>(() => this.forYouLoad(refresh), this.forYouNextPage);
    }
}
