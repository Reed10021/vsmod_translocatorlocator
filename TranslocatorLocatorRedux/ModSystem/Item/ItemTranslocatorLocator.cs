namespace TranslocatorLocatorRedux.ModSystem.Item
{
    using System.Globalization;
    using TranslocatorLocatorRedux.ModConfig;
    using Vintagestory.API.Client;
    using Vintagestory.API.Common;
    using Vintagestory.API.Config;
    using Vintagestory.GameContent;

    public class ItemTranslocatorLocator : ItemBaseLocator
    {
        protected override LocatorKind Kind => LocatorKind.Translocator;

        protected override bool IsSearchedBlock(Block block)
        {
            return block is BlockStaticTranslocator transBlock && !transBlock.Repaired;
        }

        protected override void PrintCubeSearchResults(int count, int range, ICoreClientAPI capi)
        {
            var message = count switch
            {
                0 => Lang.Get("translocatorlocatorredux:translocator_cubenofound"),
                1 => Lang.Get("translocatorlocatorredux:translocator_cubefoundlessthantwo"),
                _ => Lang.Get("translocatorlocatorredux:translocator_cubefoundmorethanone"),
            };
            capi.ShowChatMessage(message.Replace("#no", count.ToString(CultureInfo.InvariantCulture)).Replace("#range", "" + range));
        }

        protected override void PrintConeSearchResults(int count, int range, int direction, ICoreClientAPI capi)
        {
            var adjustedDirection = direction switch
            {
                // wording in opposite direction of face clicked
                0 => Lang.Get("translocatorlocatorredux:locator_southofthatblock"),
                1 => Lang.Get("translocatorlocatorredux:locator_westofthatblock"),
                2 => Lang.Get("translocatorlocatorredux:locator_northofthatblock"),
                3 => Lang.Get("translocatorlocatorredux:locator_eastofthatblock"),
                4 => Lang.Get("translocatorlocatorredux:locator_belowthatblock"),
                5 => Lang.Get("translocatorlocatorredux:locator_abovethatblock"),
                _ => Lang.Get("translocatorlocatorredux:locator_somewherearoundthatblock"),
            };

            var message = count switch
            {
                0 => Lang.Get("translocatorlocatorredux:translocator_conenofound"),
                1 => Lang.Get("translocatorlocatorredux:translocator_conefoundlessthantwo"),
                _ => Lang.Get("translocatorlocatorredux:translocator_conefoundmorethanone"),
            };

            capi.ShowChatMessage(message.Replace("#no", count.ToString(CultureInfo.InvariantCulture)).Replace("#range", "" + range).Replace("#direction", adjustedDirection));
        }

        protected override string GetFlavorText()
        {
            return Lang.Get("translocatorlocatorredux:translocator_flavortext");
        }

        protected override bool ShouldSearchReturnEarly()
        {
            return false;
        }
    }
}

