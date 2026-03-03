namespace TranslocatorLocatorRedux.ModSystem.Item
{
    using Cairo;
    using System;
    using System.Text;
    using TranslocatorLocatorRedux.ModConfig;
    using TranslocatorLocatorRedux.ModSystem.Proto;
    using Vintagestory.API.Client;
    using Vintagestory.API.Common;
    using Vintagestory.API.Config;
    using Vintagestory.API.MathTools;
    using Vintagestory.API.Server;
    using Vintagestory.API.Util;

    public abstract class ItemBaseLocator : Item
    {
        private SkillItem[] toolModes;
        private string toolModesCacheKey;

        /// <summary>
        /// Which config section this locator item reads from.
        /// </summary>
        protected abstract LocatorKind Kind { get; }

        /// <summary>
        /// Override max durability so it can be configured via ModConfig (and synced in multiplayer).
        /// 0 = disable durability (unbreakable).
        /// </summary>
        public override int GetMaxDurability(ItemStack itemstack)
        {
            var cfg = ModConfig.Current;
            return Kind == LocatorKind.Translocator ? cfg.TranslocatorLocatorDurability : cfg.AgedWoodLocatorDurability;
        }

        protected abstract bool IsSearchedBlock(Block block);
        protected abstract void PrintCubeSearchResults(int count, int range, ICoreClientAPI capi);
        protected abstract void PrintConeSearchResults(int count, int range, int direction, ICoreClientAPI capi);
        protected abstract bool ShouldSearchReturnEarly();
        protected abstract string GetFlavorText();

        internal static LocatorMode ModeFromToolMode(int toolMode)
        {
            return toolMode switch
            {
                0 => LocatorMode.SmallCone,
                1 => LocatorMode.Cone,
                2 => LocatorMode.CubeExtraSmall,
                3 => LocatorMode.CubeSmall,
                4 => LocatorMode.CubeMedium,
                5 => LocatorMode.CubeLarge,
                _ => LocatorMode.SmallCone,
            };
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (byEntity is not EntityPlayer || blockSel == null || !firstEvent)
                return;

            handling = EnumHandHandling.PreventDefaultAction;

            if (byEntity.Api.Side != EnumAppSide.Client)
                return;

            if (byEntity.Api is not ICoreClientAPI capi)
                return;

            var invMan = ((EntityPlayer)byEntity).Player.InventoryManager;
            var hotbarSlotNum = invMan?.ActiveHotbarSlotNumber ?? 0;
            var itemCode = slot.Itemstack.Collectible.Code.ToString();
            var system = capi.ModLoader.GetModSystem<TranslocatorLocatorReduxSystem>();
            if(system != null)
            {
                if(system.SendScanRequest(blockSel.Position, blockSel.Face.Index, hotbarSlotNum, itemCode))
                {
                    // Cosmetic feedback 
                    SpawnParticles(blockSel, byEntity);
                }
            }
        }

        internal bool TryExecuteScanServer(IServerPlayer player, ItemSlot slot, BlockPos center, int faceIndex,
            out int count, out int range, out int toolMode, out int durabilityCost)
        {
            count = 0;
            range = 0;
            toolMode = 0;
            durabilityCost = 0;
            BlockSelection blockSelection = new();

            if (player == null || slot.Itemstack == null)
                return false;

            toolMode = GetToolMode(slot, player, blockSelection);
            var locatorMode = ModeFromToolMode(toolMode);

            var cfg = ModConfig.Current;
            range = cfg.GetRange(Kind, locatorMode);
            durabilityCost = cfg.GetCost(Kind, locatorMode);

            if (toolMode <= 1)
                count = CountConeMatches(player.Entity.World, center, faceIndex, range);
            else
                count = CountCubeMatches(player.Entity.World, center, range);

            return true;
        }

        internal void HandleScanResultClient(ScanResultPacket packet, ICoreClientAPI capi, EntityAgent byEntity)
        {
            if (packet == null || capi == null || byEntity == null)
                return;

            if (packet.ToolMode == 0 || packet.ToolMode == 1)
                PrintConeSearchResults(packet.Count, packet.Range, packet.FaceIndex, capi);
            else
                PrintCubeSearchResults(packet.Count, packet.Range, capi);

            PlaySound(packet.Count > 0, byEntity);
        }

        internal static void DamageItemIfEnabled(IWorldAccessor world, EntityAgent byEntity, ItemSlot slot, int durabilityDamage)
        {
            if (durabilityDamage <= 0 || slot.Itemstack == null)
                return;

            // 0 = unbreakable
            var maxDurability = slot.Itemstack.Collectible.GetMaxDurability(slot.Itemstack);
            if (maxDurability <= 0)
                return;

            slot.Itemstack.Collectible.DamageItem(world, byEntity, slot, durabilityDamage);
            slot.MarkDirty();
        }

        protected virtual int CountCubeMatches(IWorldAccessor world, BlockPos center, int range)
        {
            var minY = 0;
            var maxY = world.BlockAccessor.MapSizeY;

            return SearchCubeArea(world,
                new BlockPos(center.X - range, Math.Clamp(center.Y - range, minY, maxY), center.Z - range),
                new BlockPos(center.X + range, Math.Clamp(center.Y + range, minY, maxY), center.Z + range));
        }

        protected virtual int CountConeMatches(IWorldAccessor world, BlockPos center, int direction, int range)
        {
            var count = 0;

            var xMin = center.X;
            var xMax = center.X;
            var yMin = center.Y;
            var yMax = center.Y;
            var zMin = center.Z;
            var zMax = center.Z;

            for (var distance = 0; distance <= range && !(ShouldSearchReturnEarly() && count > 0); distance++)
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
                count += SearchCubeArea(world, new BlockPos(xMin, yMin, zMin), new BlockPos(xMax, yMax, zMax));
            }
            return count;
        }

        public override void OnLoaded(ICoreAPI api)
        {
            this.toolModesCacheKey = $"translocatorlocatorredux:toolModes:{this.GetType().FullName}";
            this.toolModes = ObjectCacheUtil.GetOrCreate(api, this.toolModesCacheKey, () =>
            {
                SkillItem[] modes;

                modes = new SkillItem[6];
                modes[0] = new SkillItem() { Code = new AssetLocation("smallcone"), Name = Lang.Get("translocatorlocatorredux:smalldirectionalmode") };
                modes[1] = new SkillItem() { Code = new AssetLocation("cone"), Name = Lang.Get("translocatorlocatorredux:directionalmode") };
                modes[2] = new SkillItem() { Code = new AssetLocation("extrasmallcube"), Name = Lang.Get("translocatorlocatorredux:extrasmallcubemode") };
                modes[3] = new SkillItem() { Code = new AssetLocation("smallcube"), Name = Lang.Get("translocatorlocatorredux:smallcubemode") };
                modes[4] = new SkillItem() { Code = new AssetLocation("mediumcube"), Name = Lang.Get("translocatorlocatorredux:mediumcubemode") };
                modes[5] = new SkillItem() { Code = new AssetLocation("largecube"), Name = Lang.Get("translocatorlocatorredux:largecubemode") };

                if (api is ICoreClientAPI capi)
                {
                    modes[0].WithIcon(capi, (cr, x, y, w, h, c) => DrawIcons(cr, x, y, w, h, c, 0));
                    modes[1].WithIcon(capi, (cr, x, y, w, h, c) => DrawIcons(cr, x, y, w, h, c, 1));
                    modes[2].WithIcon(capi, (cr, x, y, w, h, c) => DrawIcons(cr, x, y, w, h, c, 2));
                    modes[3].WithIcon(capi, (cr, x, y, w, h, c) => DrawIcons(cr, x, y, w, h, c, 3));
                    modes[4].WithIcon(capi, (cr, x, y, w, h, c) => DrawIcons(cr, x, y, w, h, c, 4));
                    modes[5].WithIcon(capi, (cr, x, y, w, h, c) => DrawIcons(cr, x, y, w, h, c, 5));
                }

                return modes;
            });
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
        {
            return this.toolModes;
        }

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection)
        {
            return Math.Min(this.toolModes.Length - 1, slot.Itemstack.Attributes.GetInt("toolMode"));
        }

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSelection, int toolMode)
        {
            slot.Itemstack.Attributes.SetInt("toolMode", toolMode);
            slot.MarkDirty();
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            for (var i = 0; this.toolModes != null && i < this.toolModes.Length; i++)
            {
                this.toolModes[i]?.Dispose();
            }

            if (!string.IsNullOrEmpty(this.toolModesCacheKey))
            {
                try
                {
                    ObjectCacheUtil.Delete(api, this.toolModesCacheKey);
                }
                catch
                {
                    // ignore
                }
            }
        }

        protected int SearchCubeArea(IWorldAccessor world, BlockPos lowerCorner, BlockPos upperCorner)
        {
            var count = 0;
            world.BlockAccessor.SearchBlocks(lowerCorner, upperCorner, (block, pos) =>
            {
                if (this.IsSearchedBlock(block))
                    count++;
                return true;
            });
            return count;
        }

        private static void SpawnParticles(BlockSelection blockSel, EntityAgent byEntity)
        {
            var byPlayer = ((EntityPlayer)byEntity).Player;
            var pos = blockSel.Position.ToVec3d().Add(blockSel.HitPosition.ToVec3f().ToVec3d());
            byEntity.World.SpawnCubeParticles(blockSel.Position, pos, 0.5f, 8, 0.7f, byPlayer);
        }

        private static void PlaySound(bool found, EntityAgent byEntity)
        {
            if (found)
                PlaySoundFound(byEntity);
            else
                PlaySoundNotFound(byEntity);
        }

        private static void PlaySoundNotFound(EntityAgent byEntity)
        {
            var pos = byEntity.Pos;
            byEntity.World.PlaySoundAt(new AssetLocation("sounds/tool/padlock"), pos.X, pos.Y, pos.Z, null);
        }

        private static void PlaySoundFound(EntityAgent byEntity)
        {
            var pos = byEntity.Pos;
            byEntity.World.PlaySoundAt(new AssetLocation("sounds/player/projectilehit"), pos.X, pos.Y, pos.Z, null);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            dsc.AppendLine("\n" + this.GetFlavorText());
        }

#pragma warning disable IDE0060
        private static void DrawIcons(Context cr, int x, int y, float width, float height, double[] rgba, int toolMode)
#pragma warning restore IDE0060
        {
            cr.SetSourceRGB(1D, 1D, 1D);
            switch (toolMode)
            {
                case 0:
                    cr.MoveTo(16, 24);
                    cr.LineTo(32, 15);
                    cr.LineTo(32, 33);
                    cr.LineTo(16, 24);
                    cr.Fill();
                    break;
                case 1:
                    cr.MoveTo(11, 24);
                    cr.LineTo(37, 10);
                    cr.LineTo(37, 38);
                    cr.LineTo(11, 24);
                    cr.Fill();
                    break;
                case 2:
                    cr.Rectangle(20, 20, 8, 8);
                    cr.Fill();
                    break;
                case 3:
                    cr.Rectangle(16, 16, 16, 16);
                    cr.Fill();
                    break;
                case 4:
                    cr.Rectangle(10, 10, 28, 28);
                    cr.Fill();
                    break;
                case 5:
                default:
                    cr.Rectangle(4, 4, 40, 40);
                    cr.Fill();
                    break;
            }
        }
    }
}
