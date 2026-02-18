namespace TranslocatorLocatorRedux.ModSystem.Item
{
    using TranslocatorLocatorRedux.ModConfig;
    using Vintagestory.API.Client;
    using Vintagestory.API.Common;
    using Vintagestory.API.Config;
    using Vintagestory.GameContent;

    public class ItemAgedWoodLocator : ItemBaseLocator
    {
        protected override int GetConeRange(ModConfig modConfig)
        {
            return modConfig.AgedWoodLocatorConeRange;
        }
        protected override int GetConeCost(ModConfig modConfig)
        {
            return modConfig.AgedWoodLocatorConeCost;
        }
        protected override int GetSmallCubeRange(ModConfig modConfig)
        {
            return modConfig.AgedWoodLocatorCubeSmallRange;
        }
        protected override int GetSmallCubeCost(ModConfig modConfig)
        {
            return modConfig.AgedWoodLocatorCubeSmallCost;
        }
        protected override int GetMediumCubeRange(ModConfig modConfig)
        {
            return modConfig.AgedWoodLocatorCubeMediumRange;
        }
        protected override int GetMediumCubeCost(ModConfig modConfig)
        {
            return modConfig.AgedWoodLocatorCubeMediumCost;
        }
        protected override int GetLargeCubeRange(ModConfig modConfig)
        {
            return modConfig.AgedWoodLocatorCubeLargeRange;
        }
        protected override int GetLargeCubeCost(ModConfig modConfig)
        {
            return modConfig.AgedWoodLocatorCubeLargeCost;
        }
        protected override bool IsSearchedBlock(Block block)
        {
            // class check of the block seems to be way faster than checking the code string

            return
                // early returns
                block is not BlockSoil &&
                !block.BlockMaterial.Equals(EnumBlockMaterial.Gravel) &&
                !block.BlockMaterial.Equals(EnumBlockMaterial.Stone) &&
                !block.BlockMaterial.Equals(EnumBlockMaterial.Sand) &&
                  // checks
                  ((block is BlockBed bedBlock && bedBlock.Code.Path.Contains("woodaged"))
                || (block is BlockTapestry)
                || (block is BlockLog logBlock && logBlock.Code.Path.Contains("aged"))
                || (block is BlockContainer containerBlock && containerBlock.Code.Path.Contains("collapsed"))
                || (block.BlockMaterial.Equals(EnumBlockMaterial.Wood) && block.Code.Path.Contains("planks-aged"))
                );
        }

        protected override void PrintCubeSearchResults(int count, int range, ICoreClientAPI capi)
        {
            string message;
            if (count < 1)
            {
                message = Lang.Get("translocatorlocatorredux:agedwood_cubefoundnothing");
            }
            else
            {
                message = Lang.Get("translocatorlocatorredux:agedwood_cubefoundsomething");
            }
            capi.ShowChatMessage(message.Replace("#range", "" + range));
        }

        protected override void PrintConeSearchResults(int count, int range, int direction, ICoreClientAPI capi)
        {
            var message = "";
            var adjustedDirection = direction switch
            {
                // wording in opposite direction of face clicked
                0 => Lang.Get("translocatorlocatorredux:agedwood_southofthatblock"),
                1 => Lang.Get("translocatorlocatorredux:agedwood_westofthatblock"),
                2 => Lang.Get("translocatorlocatorredux:agedwood_northofthatblock"),
                3 => Lang.Get("translocatorlocatorredux:agedwood_eastofthatblock"),
                4 => Lang.Get("translocatorlocatorredux:agedwood_belowthatblock"),
                5 => Lang.Get("translocatorlocatorredux:agedwood_abovethatblock"),
                _ => Lang.Get("translocatorlocatorredux:agedwood_somewherearoundthatblock"),
            };

            if (count < 1)
            {
                message = Lang.Get("translocatorlocatorredux:agedwood_conefoundnothing");
            }
            else
            {
                message = Lang.Get("translocatorlocatorredux:agedwood_conefoundsomething");
            }
            capi.ShowChatMessage(message.Replace("#range", "" + range).Replace("#direction", adjustedDirection));
        }
        protected override string GetFlavorText()
        {
            return Lang.Get("translocatorlocatorredux:agedwood_flavortext");
        }

        protected override bool ShouldSearchReturnEarly()
        {
            return true;
        }
    }
}

