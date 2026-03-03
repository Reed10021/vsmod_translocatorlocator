namespace TranslocatorLocatorRedux.ModSystem.Item
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using TranslocatorLocatorRedux.ModConfig;
    using Vintagestory.API.Client;
    using Vintagestory.API.Common;
    using Vintagestory.API.Config;
    using Vintagestory.API.MathTools;
    using Vintagestory.GameContent;

    public class ItemAgedWoodLocator : ItemBaseLocator
    {
        protected override LocatorKind Kind => LocatorKind.AgedWood;
        protected override bool IsSearchedBlock(Block block)
        {
            // Not used currently/anymore.

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

        protected override int CountCubeMatches(IWorldAccessor world, BlockPos center, int range)
        {
            var minY = 0;
            var maxY = world.BlockAccessor.MapSizeY;

            var minPos = new BlockPos(center.X - range, Math.Clamp(center.Y - range, minY, maxY), center.Z - range);
            var maxPos = new BlockPos(center.X + range, Math.Clamp(center.Y + range, minY, maxY), center.Z + range);
            return CountRuinStructures(world, minPos, maxPos);
        }

        protected override int CountConeMatches(IWorldAccessor world, BlockPos center, int direction, int range)
        {
            // HashSet makes sure the same structure isn't counted multiple times.
            var seen = new HashSet<string>();

            var xMin = center.X;
            var xMax = center.X;
            var yMin = center.Y;
            var yMax = center.Y;
            var zMin = center.Z;
            var zMax = center.Z;

            for (var distance = 0; distance <= range && !(ShouldSearchReturnEarly() && seen.Count > 0); distance++)
            {
                var diameter = distance;

                switch (direction)
                {
                    case 0: // North
                        xMin = center.X - diameter; xMax = center.X + diameter;
                        yMin = center.Y - diameter; yMax = center.Y + diameter;
                        zMin = center.Z + distance; zMax = center.Z + distance;
                        break;
                    case 1: // East
                        xMin = center.X - distance; xMax = center.X - distance;
                        yMin = center.Y - diameter; yMax = center.Y + diameter;
                        zMin = center.Z - diameter; zMax = center.Z + diameter;
                        break;
                    case 2: // South
                        xMin = center.X - diameter; xMax = center.X + diameter;
                        yMin = center.Y - diameter; yMax = center.Y + diameter;
                        zMin = center.Z - distance; zMax = center.Z - distance;
                        break;
                    case 3: // West
                        xMin = center.X + distance; xMax = center.X + distance;
                        yMin = center.Y - diameter; yMax = center.Y + diameter;
                        zMin = center.Z - diameter; zMax = center.Z + diameter;
                        break;
                    case 4: // Up
                        xMin = center.X - diameter; xMax = center.X + diameter;
                        yMin = center.Y - distance; yMax = center.Y - distance;
                        zMin = center.Z - diameter; zMax = center.Z + diameter;
                        break;
                    case 5: // Down
                        xMin = center.X - diameter; xMax = center.X + diameter;
                        yMin = center.Y + distance; yMax = center.Y + distance;
                        zMin = center.Z - diameter; zMax = center.Z + diameter;
                        break;
                }

                var minPos = new BlockPos(Math.Min(xMin, xMax), Math.Min(yMin, yMax), Math.Min(zMin, zMax));
                var maxPos = new BlockPos(Math.Max(xMin, xMax), Math.Max(yMin, yMax), Math.Max(zMin, zMax));
                AddRuinStructures(world, minPos, maxPos, seen);
            }

            return seen.Count;
        }

        private static int CountRuinStructures(IWorldAccessor world, BlockPos minPos, BlockPos maxPos)
        {
            var seen = new HashSet<string>();
            AddRuinStructures(world, minPos, maxPos, seen);
            return seen.Count;
        }

        private static void AddRuinStructures(IWorldAccessor world, BlockPos minPos, BlockPos maxPos, HashSet<string> seen)
        {
            // Scan for ruins by walking through all structures in our bounds, minPos to maxPos.
            world.BlockAccessor.WalkStructures(minPos, maxPos, gs =>
            {
                if (!IsRuinStructure(gs))
                    return;
                seen.Add(GetStructureKey(gs));
            });
        }

        private static bool IsRuinStructure(GeneratedStructure gs)
        {
            //var loc = gs.Location;
            //sapi.Logger.Debug($"[translocatorlocatorredux] Struct code:" + gs.Code);
            //sapi.Logger.Debug($"[translocatorlocatorredux] Struct group:" + gs.Group);
            //sapi.Logger.Debug($"[translocatorlocatorredux] Struct X1:" + (loc.X1 - sapi.World.DefaultSpawnPosition.X) + "; X2: " + (loc.X2 - sapi.World.DefaultSpawnPosition.X));
            //sapi.Logger.Debug($"[translocatorlocatorredux] Struct Z1:" + (loc.Z1 - sapi.World.DefaultSpawnPosition.Z) + "; Z2: " + (loc.Z2 - sapi.World.DefaultSpawnPosition.Z));
            //sapi.Logger.Debug($"[translocatorlocatorredux] Struct Y1:" + loc.Y1 + "; Y2: " + loc.Y2);

            // Check if structure code or group match what we're looking for
            if (!string.IsNullOrEmpty(gs.Code) && (
                gs.Code.Contains("ruin", StringComparison.OrdinalIgnoreCase) ||
                gs.Code.Contains("grave", StringComparison.OrdinalIgnoreCase) ||
                gs.Code.Contains("monolith", StringComparison.OrdinalIgnoreCase) ||
                gs.Code.Contains("arcticsupplies", StringComparison.OrdinalIgnoreCase) ||
                gs.Code.Contains("small3.json/lakes", StringComparison.OrdinalIgnoreCase) ||
                gs.Code.Contains("small4.json/lakes", StringComparison.OrdinalIgnoreCase) ||
                gs.Code.Contains("small8.json/lakes", StringComparison.OrdinalIgnoreCase) ||
                gs.Code.Contains("small9.json/lakes", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            else if (!string.IsNullOrEmpty(gs.Group) && (
                gs.Group.Contains("ruin", StringComparison.OrdinalIgnoreCase) ||
                gs.Group.Contains("monolith", StringComparison.OrdinalIgnoreCase) ||
                gs.Group.Contains("desertspecific", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            return false;
        }

        private static string GetStructureKey(GeneratedStructure gs)
        {
            // We treat the bounding box + code & group as identity.
            var loc = gs.Location;
            return $"{gs.Group}|{gs.Code}|{loc.X1},{loc.Y1},{loc.Z1},{loc.X2},{loc.Y2},{loc.Z2}";
        }

        protected override void PrintCubeSearchResults(int count, int range, ICoreClientAPI capi)
        {
            var message = count switch
            {
                0 => Lang.Get("translocatorlocatorredux:agedwood_cubefoundnothing"),
                1 => Lang.Get("translocatorlocatorredux:agedwood_cubefoundlessthantwo"),
                _ => Lang.Get("translocatorlocatorredux:agedwood_cubefoundmorethanone"),
            };
            capi.ShowChatMessage(message.Replace("#no", count.ToString(CultureInfo.InvariantCulture)).Replace("#range", "" + range * 2));
            // range is a radius, so for the chat description to be correct we need to double it.
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
                0 => Lang.Get("translocatorlocatorredux:agedwood_conefoundnothing"),
                1 => Lang.Get("translocatorlocatorredux:agedwood_conefoundlessthantwo"),
                _ => Lang.Get("translocatorlocatorredux:agedwood_conefoundmorethanone"),
            };

            capi.ShowChatMessage(message.Replace("#no", count.ToString(CultureInfo.InvariantCulture)).Replace("#range", "" + range).Replace("#direction", adjustedDirection));
        }

        protected override string GetFlavorText()
        {
            return Lang.Get("translocatorlocatorredux:agedwood_flavortext");
        }

        protected override bool ShouldSearchReturnEarly()
        {
            return false;
        }
    }
}

