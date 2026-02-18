namespace TranslocatorLocator.ModSystem.Item
{
    using System.Globalization;
    using TranslocatorLocator.ModConfig;
    using Vintagestory.API.Client;
    using Vintagestory.API.Common;
    using Vintagestory.API.Config;
    using Vintagestory.GameContent;

    public class ItemTranslocatorLocator : ItemBaseLocator
    {
        protected override int GetConeRange(ModConfig modConfig)
        {
            return modConfig.TranslocatorLocatorConeRange;
        }
        protected override int GetConeCost(ModConfig modConfig)
        {
            return modConfig.TranslocatorLocatorConeCost;
        }
        protected override int GetSmallCubeRange(ModConfig modConfig)
        {
            return modConfig.TranslocatorLocatorCubeSmallRange;
        }
        protected override int GetSmallCubeCost(ModConfig modConfig)
        {
            return modConfig.TranslocatorLocatorCubeSmallCost;
        }
        protected override int GetMediumCubeRange(ModConfig modConfig)
        {
            return modConfig.TranslocatorLocatorCubeMediumRange;
        }
        protected override int GetMediumCubeCost(ModConfig modConfig)
        {
            return modConfig.TranslocatorLocatorCubeMediumCost;
        }
        protected override int GetLargeCubeRange(ModConfig modConfig)
        {
            return modConfig.TranslocatorLocatorCubeLargeRange;
        }
        protected override int GetLargeCubeCost(ModConfig modConfig)
        {
            return modConfig.TranslocatorLocatorCubeLargeCost;
        }

        protected override bool IsSearchedBlock(Block block)
        {
            return block is BlockStaticTranslocator transBlock && !transBlock.Repaired;
        }

        protected override void PrintCubeSearchResults(int count, int range, ICoreClientAPI capi)
        {
            //var noOfTLs = (count > 0) ? "" + count : Lang.Get("translocatorlocatorredux:translocator_no");
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
                0 => Lang.Get("translocatorlocatorredux:translocator_southofthatblock"),
                1 => Lang.Get("translocatorlocatorredux:translocator_westofthatblock"),
                2 => Lang.Get("translocatorlocatorredux:translocator_northofthatblock"),
                3 => Lang.Get("translocatorlocatorredux:translocator_eastofthatblock"),
                4 => Lang.Get("translocatorlocatorredux:translocator_belowthatblock"),
                5 => Lang.Get("translocatorlocatorredux:translocator_abovethatblock"),
                _ => Lang.Get("translocatorlocatorredux:translocator_somewherearoundthatblock"),
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

