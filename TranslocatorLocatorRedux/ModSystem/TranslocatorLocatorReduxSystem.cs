namespace TranslocatorLocatorRedux.ModSystem
{
    using System;
    using System.Text;
    using System.Reflection;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using ProtoBuf;
    using TranslocatorLocatorRedux.ModConfig;
    using TranslocatorLocatorRedux.ModSystem.Item;
    using Vintagestory.API.Client;
    using Vintagestory.API.Common;
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
        private IServerNetworkChannel? serverChannel;
        private IClientNetworkChannel? clientChannel;

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

            // Subscribe as early as possible so ConfigLib values can be applied before we patch assets.
            TrySubscribeToConfigLib(api);
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            base.AssetsLoaded(api);

            // Retry here in case StartPre/Start ran before ConfigLib's ModSystem was available
            TrySubscribeToConfigLib(api);

            // If client side, we're done here.
            if (api.Side != EnumAppSide.Server) return;

            // Ensure ConfigLib has had a chance to apply its stored values before we patch recipes.
            OnConfigLibConfigsLoaded();

            PatchRecipeMetalPlates(api, "recipes/grid/translocatorlocator.json", ModConfig.Current.TranslocatorLocatorUseCupronickelPlates);
            PatchRecipeMetalPlates(api, "recipes/grid/agedwoodlocator.json", ModConfig.Current.AgedWoodLocatorUseCupronickelPlates);
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
                .SetMessageHandler<ConfigRequestPacket>((fromPlayer, _packet) =>
                {
                    SyncConfigToClient(fromPlayer);
                });

            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            clientChannel = api.Network
                .RegisterChannel(ChannelName)
                .RegisterMessageType<ConfigRequestPacket>()
                .RegisterMessageType<ConfigSyncPacket>()
                .SetMessageHandler<ConfigSyncPacket>(packet =>
                {
                    if (packet?.Data == null) return;
                    ModConfig.LoadSettingsJson(packet.Data);
                    ModConfig.Current.Normalize();
                });

            // Ask the server for its config
            clientChannel.SendPacket(new ConfigRequestPacket());
        }

        private void OnPlayerNowPlaying(IServerPlayer byPlayer)
        {
            SyncConfigToClient(byPlayer);
        }

        private void SyncConfigToClient(IServerPlayer player)
        {
            if (serverChannel == null) return;
            var json = ModConfig.ToJson(ModConfig.Current);
            serverChannel.SendPacket(new ConfigSyncPacket(json), player);
        }

        private void BroadcastConfigToAllPlayers()
        {
            if (sapi == null || serverChannel == null) return;

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

        private void TrySubscribeToConfigLib(ICoreAPI api)
        {
            if (configLibSubscribed) return;
            if (!api.ModLoader.IsModEnabled("configlib")) return;

            // Use string-based lookup so we can compile/run without depending on ConfigLib.
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
                if (mi != null)
                {
                    try
                    {
                        configLibSettingChangedHandler = Delegate.CreateDelegate(settingChangedEvent.EventHandlerType!, this, mi);
                        settingChangedEvent.AddEventHandler(configLibModSystem, configLibSettingChangedHandler);
                    }
                    catch (Exception e)
                    {
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
                if (mi != null)
                {
                    try
                    {
                        configLibConfigsLoadedHandler = Delegate.CreateDelegate(configsLoadedEvent.EventHandlerType!, this, mi);
                        configsLoadedEvent.AddEventHandler(configLibModSystem, configLibConfigsLoadedHandler);
                    }
                    catch (Exception e)
                    {
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
            if (!string.Equals(domain, Domain, StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                // ConfigLib.ConfigSetting.AssignSettingValue(object target)
                var assign = setting.GetType().GetMethod("AssignSettingValue", [typeof(object)]);
                assign?.Invoke(setting, [ModConfig.Current]);
            }
            catch
            {
                // Ignore individual assignment errors; we'll still normalize below.
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
                if (configLibModSystem == null) return;

                var systemType = configLibModSystem.GetType();
                var getConfig = systemType.GetMethod("GetConfig", [typeof(string)]);
                var cfg = getConfig?.Invoke(configLibModSystem, [Domain]);
                if (cfg == null) return;

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
            if (sapi == null) return;

            try
            {
                ModConfig.Save(sapi);
            }
            catch (Exception e)
            {
                sapi.Logger.Warning($"[{Domain}] Failed to persist config to disk after ConfigLib update: {e}");
            }
        }

        [ProtoContract]
        public sealed class ConfigRequestPacket
        {
            // empty
        }

        [ProtoContract]
        public sealed class ConfigSyncPacket
        {
            [ProtoMember(1)]
            public string Data { get; set; } = "";

            public ConfigSyncPacket() { }
            public ConfigSyncPacket(string data) { Data = data; }
        }
    }
}
