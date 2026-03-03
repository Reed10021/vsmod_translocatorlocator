namespace TranslocatorLocatorRedux.ModSystem
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text;
    using TranslocatorLocatorRedux.ModConfig;
    using TranslocatorLocatorRedux.ModSystem.Item;
    using TranslocatorLocatorRedux.ModSystem.Proto;
    using Vintagestory.API.Client;
    using Vintagestory.API.Common;
    using Vintagestory.API.Config;
    using Vintagestory.API.MathTools;
    using Vintagestory.API.Server;

    public sealed class TranslocatorLocatorReduxSystem : ModSystem
    {
        private const string Domain = "translocatorlocatorredux";
        private const string ChannelName = "translocatorlocatorredux";
        private const string IronPlateCode = "game:metalplate-iron";
        private const string CupronickelPlateCode = "game:metalplate-cupronickel";

        // Run after JSON patching/overrides but before the vanilla GridRecipeLoader
        public override double ExecuteOrder() => 0.95;

        private ICoreServerAPI? sapi;
        private ICoreClientAPI? capi;
        private IServerNetworkChannel? serverChannel;
        private IClientNetworkChannel? clientChannel;

        // Scan request throttling
        private readonly HashSet<string> serverPlayersWithPendingScan = [];
        private bool clientScanPending;
        private long clientScanTimeoutCallbackId;

        // ConfigLib (optional)
        private bool configLibSubscribed;
        private object? configLibModSystem;
        private Delegate? configLibSettingChangedHandler;
        private Delegate? configLibConfigsLoadedHandler;

        public override void Dispose()
        {
            base.Dispose();

            if (sapi != null)
            {
                sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
            }

            if (capi != null)
            {
                capi.Event.LeaveWorld -= OnLeaveWorld;
            }

            // Unsubscribe from ConfigLib events
            if (configLibModSystem != null)
            {
                try
                {
                    var systemType = configLibModSystem.GetType();
                    if (configLibSettingChangedHandler != null)
                    {
                        systemType.GetEvent("SettingChanged")
                            ?.RemoveEventHandler(configLibModSystem, configLibSettingChangedHandler);
                    }
                    if (configLibConfigsLoadedHandler != null)
                    {
                        systemType.GetEvent("ConfigsLoaded")
                            ?.RemoveEventHandler(configLibModSystem, configLibConfigsLoadedHandler);
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);

            // Load the config early so we can patch recipes before the recipe loader runs.
            ModConfig.LoadOrCreate(api);

            // Try to subscribe as early as possible.
            TrySubscribeToConfigLib(api);
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            base.AssetsLoaded(api);

            // Retry here in case something happened to the other attempts.
            TrySubscribeToConfigLib(api);

            // If client side, we're done here.
            if (api.Side != EnumAppSide.Server)
                return;

            OnConfigLibConfigsLoaded();

            PatchRecipeMetalPlates(api, "recipes/grid/translocatorlocator.json", ModConfig.Current.TranslocatorLocatorUseCupronickelPlates);
            PatchRecipeMetalPlates(api, "recipes/grid/agedwoodlocator.json", ModConfig.Current.AgedWoodLocatorUseCupronickelPlates);
        }

        private static void PatchRecipeMetalPlates(ICoreAPI api, string recipeAssetPath, bool useCupronickelPlates)
        {
            var desired = useCupronickelPlates ? CupronickelPlateCode : IronPlateCode;
            var loc = new AssetLocation(Domain, recipeAssetPath);
            var asset = api.Assets.TryGet(loc);
            if (asset == null)
            {
                api.Logger.Warning($"[{Domain}] Could not find recipe asset {loc}; skipping plate swap patch.");
                return;
            }

            bool changed = false;
            try
            {
                var jsonText = asset.ToText();
                var token = JToken.Parse(jsonText);

                void PatchIngredients(JObject recipe)
                {
                    if (recipe["ingredients"] is not JObject ingredients)
                        return;

                    foreach (var prop in ingredients.Properties())
                    {
                        if (prop.Value is not JObject ingObj)
                            continue;
                        var codeToken = ingObj["code"];
                        if (codeToken == null || codeToken.Type != JTokenType.String)
                            continue;

                        var code = codeToken.Value<string>();
                        if (code != IronPlateCode && code != CupronickelPlateCode)
                            continue;

                        if (code != desired)
                        {
                            ingObj["code"] = desired;
                            changed = true;
                        }
                    }
                }

                if (token is JArray arr)
                {
                    foreach (var child in arr)
                    {
                        if (child is JObject obj) PatchIngredients(obj);
                    }

                    if (changed)
                    {
                        var updated = arr.ToString(Formatting.Indented);
                        asset.Data = Encoding.UTF8.GetBytes(updated);
                        asset.IsPatched = true;
                        api.Logger.Notification($"[{Domain}] Patched {loc} to use {(useCupronickelPlates ? "cupronickel" : "iron")} plates.");
                    }
                }
                else if (token is JObject singleObj)
                {
                    PatchIngredients(singleObj);
                    if (changed)
                    {
                        var updated = singleObj.ToString(Formatting.Indented);
                        asset.Data = Encoding.UTF8.GetBytes(updated);
                        asset.IsPatched = true;
                        api.Logger.Notification($"[{Domain}] Patched {loc} to use {(useCupronickelPlates ? "cupronickel" : "iron")} plates.");
                    }
                }
            }
            catch (Exception e)
            {
                api.Logger.Error($"[{Domain}] Failed to patch recipe {loc}: {e}");
            }
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            // Make sure we always have an instance (ConfigLib assigns into this).
            // ModConfig.LoadOrCreate will replace it with a loaded instance on the server.
            _ = ModConfig.Current;

            api.RegisterItemClass("ItemTranslocatorLocator", typeof(ItemTranslocatorLocator));
            api.RegisterItemClass("ItemAgedWoodLocator", typeof(ItemAgedWoodLocator));

            TrySubscribeToConfigLib(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            sapi = api;
            ModConfig.LoadOrCreate(api);

            serverChannel = api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<ConfigRequestPacket>()
                .RegisterMessageType<ConfigSyncPacket>()
                .RegisterMessageType<ScanRequestPacket>()
                .RegisterMessageType<ScanResultPacket>()
                .SetMessageHandler<ConfigRequestPacket>((fromPlayer, _packet) =>
                {
                    SyncConfigToClient(fromPlayer);
                })
                .SetMessageHandler<ScanRequestPacket>((fromPlayer, packet) =>
                {
                    HandleScanRequest(fromPlayer, packet);
                });

            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            capi = api;

            clientChannel = api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<ConfigRequestPacket>()
                .RegisterMessageType<ConfigSyncPacket>()
                .RegisterMessageType<ScanRequestPacket>()
                .RegisterMessageType<ScanResultPacket>()
                .SetMessageHandler<ConfigSyncPacket>(packet =>
                {
                    if (packet.Data == null) 
                        return;
                    ModConfig.LoadSettingsJson(packet.Data);
                    ModConfig.Current.Normalize();
                })
                .SetMessageHandler<ScanResultPacket>(packet =>
                {
                    OnScanResultPacketReceived(api, packet);
                });

            api.Event.LeaveWorld += OnLeaveWorld;
        }

        private void OnPlayerNowPlaying(IServerPlayer byPlayer)
        {
            SyncConfigToClient(byPlayer);
        }

        private void SyncConfigToClient(IServerPlayer player)
        {
            if (serverChannel == null)
                return;
            var json = ModConfig.ToJson(ModConfig.Current);
            serverChannel.SendPacket(new ConfigSyncPacket(json), player);
        }

        private void OnLeaveWorld()
        {
            ClearClientPendingScan();
        }

        private void BroadcastConfigToAllPlayers()
        {
            if (sapi == null || serverChannel == null)
                return;

            var json = ModConfig.ToJson(ModConfig.Current);
            var packet = new ConfigSyncPacket(json);

            foreach (var player in sapi.World.AllOnlinePlayers)
            {
                if (player is IServerPlayer sp && sp.ConnectionState == EnumClientState.Playing)
                {
                    serverChannel.SendPacket(packet, sp);
                }
            }
        }

        private void TrySubscribeToConfigLib(ICoreAPI api)
        {
            if (configLibSubscribed)
                return;
            if (!api.ModLoader.IsModEnabled("configlib"))
                return;

            // Use GetModSystem name lookup so we don't have to explicitly depend on ConfigLib.
            configLibModSystem = api.ModLoader.GetModSystem("ConfigLib.ConfigLibModSystem");
            if (configLibModSystem == null)
            {
                api.Logger.Warning($"[{Domain}] ConfigLib is enabled but ConfigLibModSystem couldn't be found.");
                return;
            }

            var systemType = configLibModSystem.GetType();

            // ConfigLibModSystem.SettingChanged: (string domain, IConfig config, ISetting setting)
            var settingChangedEvent = systemType.GetEvent("SettingChanged");
            if (settingChangedEvent != null)
            {
                var mi = GetType().GetMethod(nameof(OnConfigLibSettingChanged), BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi != null && configLibSettingChangedHandler == null)
                {
                    try
                    {
                        configLibSettingChangedHandler = Delegate.CreateDelegate(settingChangedEvent.EventHandlerType!, this, mi);
                        settingChangedEvent.AddEventHandler(configLibModSystem, configLibSettingChangedHandler);
                    }
                    catch (Exception e)
                    {
                        configLibSettingChangedHandler = null;
                        api.Logger.Warning($"[{Domain}] Failed to hook ConfigLib SettingChanged: {e}");
                        return;
                    }
                }
            }

            // ConfigLibModSystem.ConfigsLoaded: () => ...
            var configsLoadedEvent = systemType.GetEvent("ConfigsLoaded");
            if (configsLoadedEvent != null)
            {
                var mi = GetType().GetMethod(nameof(OnConfigLibConfigsLoaded), BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi != null && configLibConfigsLoadedHandler == null)
                {
                    try
                    {
                        configLibConfigsLoadedHandler = Delegate.CreateDelegate(configsLoadedEvent.EventHandlerType!, this, mi);
                        configsLoadedEvent.AddEventHandler(configLibModSystem, configLibConfigsLoadedHandler);
                    }
                    catch (Exception e)
                    {
                        configLibConfigsLoadedHandler = null;
                        api.Logger.Warning($"[{Domain}] Failed to hook ConfigLib ConfigsLoaded: {e}");
                        return;
                    }
                }
            }

            configLibSubscribed = true;
        }

        // This signature is deliberately broad so we can create the delegate without a hard reference.
#pragma warning disable IDE0060 // Remove unused parameter
        private void OnConfigLibSettingChanged(string domain, object _config, object setting)
        {
            if (!string.Equals(domain, Domain, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                // ConfigLib.ConfigSetting.AssignSettingValue(object target)
                var assign = setting.GetType().GetMethod("AssignSettingValue", [typeof(object)]);
                assign?.Invoke(setting, [ModConfig.Current]);
            }
            catch
            {
                // Ignore individual assignment errors; we'll still normalize later.
            }

            ModConfig.Current.Normalize();
            PersistConfigIfServer();

            // If this happened on the server, push the updated config to connected clients.
            BroadcastConfigToAllPlayers();
        }
#pragma warning restore IDE0060 // Remove unused parameter

        private void OnConfigLibConfigsLoaded()
        {
            try
            {
                if (configLibModSystem == null)
                    return;

                var systemType = configLibModSystem.GetType();
                var getConfig = systemType.GetMethod("GetConfig", [typeof(string)]);
                var cfg = getConfig?.Invoke(configLibModSystem, [Domain]);
                if (cfg == null)
                    return;

                var assignAll = cfg.GetType().GetMethod("AssignSettingsValues", [typeof(object)]);
                assignAll?.Invoke(cfg, [ModConfig.Current]);
            }
            catch
            {

            }

            ModConfig.Current.Normalize();
            PersistConfigIfServer();
            BroadcastConfigToAllPlayers();
        }

        private void PersistConfigIfServer()
        {
            if (sapi == null) 
                return;

            try
            {
                ModConfig.Save(sapi);
            }
            catch (Exception e)
            {
                sapi.Logger.Warning($"[{Domain}] Failed to persist config to disk after ConfigLib update: {e}");
            }
        }

        public bool SendScanRequest(BlockPos pos, int faceIndex, int hotbarSlotNumber, string itemCode)
        {
            if (capi == null || clientChannel == null || !clientChannel.Connected) 
                return false;

            if (clientScanPending)
            {
                capi.ShowChatMessage(Lang.Get("translocatorlocatorredux:scanpending"));
                return false;
            }

            clientScanPending = true;
            capi.ShowChatMessage(Lang.Get("translocatorlocatorredux:scanningarea"));

            clientScanTimeoutCallbackId = capi.Event.RegisterCallback(_ =>
            {
                if (!clientScanPending) 
                    return;

                ClearClientPendingScan();
                //capi.ShowChatMessage(Lang.Get("translocatorlocatorredux:scantimeout"));
            }, 20000, true);

            try
            {
                clientChannel.SendPacket(new ScanRequestPacket
                {
                    X = pos.X,
                    Y = pos.Y,
                    Z = pos.Z,
                    FaceIndex = faceIndex,
                    HotbarSlotNumber = hotbarSlotNumber,
                    ItemCode = itemCode
                });
            }
            catch (Exception e)
            {
                ClearClientPendingScan();
                capi.Logger.Warning($"[{Domain}] Failed to send scan request: {e}");
                return false;
            }

            return true;
        }

        private void ClearClientPendingScan()
        {
            if (capi == null)
                return;

            clientScanPending = false;

            if (clientScanTimeoutCallbackId != 0)
            {
                capi.Event.UnregisterCallback(clientScanTimeoutCallbackId);
                clientScanTimeoutCallbackId = 0;
            }
        }

        private void HandleScanRequest(IServerPlayer fromPlayer, ScanRequestPacket packet)
        {
            if (sapi == null || serverChannel == null || packet == null || fromPlayer == null) 
                return;

            if (!serverPlayersWithPendingScan.Add(fromPlayer.PlayerUID))
            {
                SendBadScanResponse(fromPlayer, packet, ScanResponseStatus.RejectedBusy);
                return;
            }

            try
            {
                IPlayerInventoryManager invMan = fromPlayer.InventoryManager;
                IInventory hotbarInv = invMan.GetHotbarInventory();

                if (packet.HotbarSlotNumber < 0 || packet.HotbarSlotNumber >= hotbarInv.Count)
                {
                    SendBadScanResponse(fromPlayer, packet, ScanResponseStatus.RejectedInvalid);
                    return;
                }

                var slot = hotbarInv[packet.HotbarSlotNumber];
                if (slot?.Itemstack?.Collectible is not ItemBaseLocator locator)
                {
                    SendBadScanResponse(fromPlayer, packet, ScanResponseStatus.RejectedInvalid);
                    return;
                }

                var currentCode = slot.Itemstack.Collectible.Code.ToString();

                // If they moved/changed the item in that slot, cancel to prevent weirdness/abuse
                if (!string.IsNullOrEmpty(packet.ItemCode))
                {
                    if (currentCode != packet.ItemCode)
                    {
                        SendBadScanResponse(fromPlayer, packet, ScanResponseStatus.RejectedInvalid);
                        return;
                    }
                }

                if (packet.FaceIndex < 0 || packet.FaceIndex > 5)
                {
                    SendBadScanResponse(fromPlayer, packet, ScanResponseStatus.RejectedInvalid);
                    return;
                }

                // Don't let the client request scans from far away locations they can't reach.
                var px = fromPlayer.Entity.Pos.X;
                var py = fromPlayer.Entity.Pos.Y;
                var pz = fromPlayer.Entity.Pos.Z;

                var tx = packet.X + 0.5;
                var ty = packet.Y + 0.5;
                var tz = packet.Z + 0.5;

                var dx = tx - px;
                var dy = ty - py;
                var dz = tz - pz;

                const double maxDist = 10.0;
                if (dx * dx + dy * dy + dz * dz > maxDist * maxDist)
                {
                    SendBadScanResponse(fromPlayer, packet, ScanResponseStatus.RejectedTooFar);
                    return;
                }

                var center = new BlockPos(packet.X, packet.Y, packet.Z);

                if (!locator.TryExecuteScanServer(fromPlayer, slot, center, packet.FaceIndex,
                    out var count, out var range, out var toolMode, out var durabilityCost))
                {
                    SendBadScanResponse(fromPlayer, packet, ScanResponseStatus.RejectedInvalid);
                    return;
                }

                ItemBaseLocator.DamageItemIfEnabled(sapi.World, fromPlayer.Entity, slot, durabilityCost);

                serverChannel.SendPacket(new ScanResultPacket
                {
                    Count = count,
                    Range = range,
                    FaceIndex = packet.FaceIndex,
                    ToolMode = toolMode,
                    ItemCode = currentCode
                }, fromPlayer);
            }
            catch (Exception e)
            {
                sapi.Logger.Error($"[{Domain}] Exception while processing scan request: {e}");
                SendBadScanResponse(fromPlayer, packet, ScanResponseStatus.RejectedInvalid);
            }
            finally
            {
                serverPlayersWithPendingScan.Remove(fromPlayer.PlayerUID);
            }
        }

        private void SendBadScanResponse(IServerPlayer toPlayer, ScanRequestPacket req, ScanResponseStatus status)
        {
            if (serverChannel == null || toPlayer == null || req == null)
                return;

            serverChannel.SendPacket(new ScanResultPacket
            {
                Status = status,
                Count = 0,
                Range = 0,
                FaceIndex = req.FaceIndex,
                ToolMode = 0,
                ItemCode = req.ItemCode ?? ""
            }, toPlayer);
        }

        private void OnScanResultPacketReceived(ICoreClientAPI api, ScanResultPacket packet)
        {
            // Clear pending 
            ClearClientPendingScan();

            // Handle any rejection status
            if (packet.Status != ScanResponseStatus.Ok)
            {
                switch (packet.Status)
                {
                    case ScanResponseStatus.RejectedTooFar:
                        api.ShowChatMessage(Lang.Get("translocatorlocatorredux:scanrejected_toofar"));
                        break;
                    case ScanResponseStatus.RejectedBusy:
                        api.ShowChatMessage(Lang.Get("translocatorlocatorredux:scanrejected_busy"));
                        break;
                    default:
                        api.ShowChatMessage(Lang.Get("translocatorlocatorredux:scanrejected_invalid"));
                        break;
                }
                return;
            }

            // If not rejected, show results
            HandleScanResultClient(api, packet);
        }

        private static void HandleScanResultClient(ICoreClientAPI api, ScanResultPacket packet)
        {
            if (api == null || packet == null) 
                return;

            var player = api.World.Player;
            if (player == null) 
                return;

            // Grab the item that actually initiated the scan (even if player swapped away)
            ItemBaseLocator? locator = null;

            if (!string.IsNullOrEmpty(packet.ItemCode))
            {
                try
                {
                    locator = api.World.GetItem(new AssetLocation(packet.ItemCode)) as ItemBaseLocator;
                }
                catch
                {
                    // ignore
                }
            }

            // Fallback for any older packets or edge cases
            locator ??= player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible as ItemBaseLocator;

            if (locator == null) 
                return;

            locator.HandleScanResultClient(packet, api, player.Entity);
        }
    }
}
