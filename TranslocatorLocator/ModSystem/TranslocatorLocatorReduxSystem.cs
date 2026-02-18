#pragma warning disable IDE1006 // Naming Styles
#pragma warning disable CA1051 // Visible Instance Fields
namespace TranslocatorLocatorRedux.ModSystem
{
    using TranslocatorLocatorRedux.ModConfig;
    using TranslocatorLocatorRedux.ModSystem.Item;
    using Vintagestory.API.Client;
    using Vintagestory.API.Common;
    using Vintagestory.API.Server;
    using Newtonsoft.Json;
    using ProtoBuf;

    public class TranslocatorLocatorReduxSystem : ModSystem
    {
        private const string ChannelName = "translocatorlocatorredux";
        private ICoreServerAPI sapi;
        //private ICoreClientAPI spapi;
        private IServerNetworkChannel serverChannel;
        private IClientNetworkChannel clientChannel;

        [ProtoContract]
        public class ConfigRequestPacket
        {
            [ProtoMember(1)]
            public int RequestId = 1;
        }

        [ProtoContract]
        public class ConfigSyncPacket
        {
            [ProtoMember(1)]
            public string Json;
        }

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return true;
        }

        public override void Start(ICoreAPI api)
        {
            //api.Logger.Debug("[TranslocatorLocatorRedux] Start");
            base.Start(api);

            api.RegisterItemClass("ItemTranslocatorLocator", typeof(ItemTranslocatorLocator));
            api.RegisterItemClass("ItemAgedWoodLocator", typeof(ItemAgedWoodLocator));
            ModConfig.Current ??= new ModConfig();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            //api.Logger.Debug("[TranslocatorLocatorRedux] StartServer");
            base.StartServerSide(api);
            this.sapi = api;

            ModConfig.Load(api);

            this.serverChannel = api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<ConfigSyncPacket>()
                .RegisterMessageType<ConfigRequestPacket>()
                .SetMessageHandler<ConfigRequestPacket>(this.OnConfigRequestFromClient);

            // Sync config after the client is fully loaded and ready to play.
            api.Event.PlayerNowPlaying += this.OnPlayerNowPlaying;
        }

        private void OnPlayerNowPlaying(IServerPlayer player)
        {
            this.SendConfigToClient(player);
        }

        private void OnConfigRequestFromClient(IServerPlayer fromPlayer, ConfigRequestPacket packet)
        {
            this.SendConfigToClient(fromPlayer);
        }

        private void SendConfigToClient(IServerPlayer player)
        {
            if (this.serverChannel == null || player == null)
            {
                //this.sapi.Logger.Debug("[TranslocatorLocatorRedux] this.serverChannel == null || player == null");
                return;
            }

            // Ensure config exists
            if (ModConfig.Current == null && this.sapi != null)
            {
                //this.sapi.Logger.Debug("[TranslocatorLocatorRedux] ModConfig.Current == null && this.sapi != null");
                ModConfig.Load(this.sapi);
            }

            var json = JsonConvert.SerializeObject(ModConfig.Current ?? new ModConfig());
            this.serverChannel.SendPacket(new ConfigSyncPacket { Json = json }, player);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            //api.Logger.Debug("[TranslocatorLocatorRedux] StartClient");
            base.StartClientSide(api);
            //this.spapi = api;

            this.clientChannel = api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType<ConfigSyncPacket>()
                .RegisterMessageType<ConfigRequestPacket>()
                .SetMessageHandler<ConfigSyncPacket>(this.OnConfigSyncFromServer);

            // Request config once we're in-world.
            api.Event.LevelFinalize += () =>
            {
                ModConfig.Current ??= new ModConfig();
                this.clientChannel.SendPacket(new ConfigRequestPacket());
            };
        }

        private void OnConfigSyncFromServer(ConfigSyncPacket packet)
        {
            if (packet?.Json == null)
            {
                //this.spapi.Logger.Debug("[TranslocatorLocatorRedux] packet?.Json == null");
                return;
            }

            try
            {
                ModConfig.Current = JsonConvert.DeserializeObject<ModConfig>(packet.Json) ?? new ModConfig();
                //this.spapi.Logger.Debug("[TranslocatorLocatorRedux] try");
            }
            catch
            {
                // If anything goes wrong, keep defaults rather than crashing
                ModConfig.Current ??= new ModConfig();
                //this.spapi.Logger.Debug("[TranslocatorLocatorRedux] catch");
            }
        }
    }
}
