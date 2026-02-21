namespace TranslocatorLocatorRedux.ModConfig
{
    using System;
    using System.IO;
    using Newtonsoft.Json;
    using Vintagestory.API.Common;
    using Vintagestory.API.Config;

    public enum LocatorKind
    {
        Translocator,
        AgedWood,
    }

    public enum LocatorMode
    {
        SmallCone,
        Cone,
        CubeExtraSmall,
        CubeSmall,
        CubeMedium,
        CubeLarge,
    }

    /// <summary>
    /// Flat config values.
    /// <para>
    /// Kept flat on purpose:
    /// - preserves existing JSON keys for backwards compatibility
    /// - works well with ConfigLib (settings map to fields/properties by name)
    /// </para>
    /// </summary>
    public sealed class ModConfig
    {
        public const string FileName = "TranslocatorLocatorRedux.json";

        /// <summary>
        /// Live config instance used by gameplay code. Always non-null.
        /// </summary>
        public static ModConfig Current { get; private set; } = new ModConfig();

        // Translocator locator
        public int TranslocatorLocatorDurability { get; set; } = 1500;

        public int TranslocatorLocatorSmallConeRange { get; set; } = 10;
        public int TranslocatorLocatorSmallConeCost { get; set; } = 1;

        public int TranslocatorLocatorConeRange { get; set; } = 50;
        public int TranslocatorLocatorConeCost { get; set; } = 4;

        public int TranslocatorLocatorCubeExtraSmallRange { get; set; } = 10;
        public int TranslocatorLocatorCubeExtraSmallCost { get; set; } = 1;

        public int TranslocatorLocatorCubeSmallRange { get; set; } = 25;
        public int TranslocatorLocatorCubeSmallCost { get; set; } = 2;

        public int TranslocatorLocatorCubeMediumRange { get; set; } = 75;
        public int TranslocatorLocatorCubeMediumCost { get; set; } = 10;

        public int TranslocatorLocatorCubeLargeRange { get; set; } = 150;
        public int TranslocatorLocatorCubeLargeCost { get; set; } = 25;
        public bool TranslocatorLocatorUseCupronickelPlates { get; set; } = false;

        // Ruin locator
        public int AgedWoodLocatorDurability { get; set; } = 1500;

        public int AgedWoodLocatorSmallConeRange { get; set; } = 10;
        public int AgedWoodLocatorSmallConeCost { get; set; } = 1;

        public int AgedWoodLocatorConeRange { get; set; } = 50;
        public int AgedWoodLocatorConeCost { get; set; } = 4;

        public int AgedWoodLocatorCubeExtraSmallRange { get; set; } = 10;
        public int AgedWoodLocatorCubeExtraSmallCost { get; set; } = 1;

        public int AgedWoodLocatorCubeSmallRange { get; set; } = 25;
        public int AgedWoodLocatorCubeSmallCost { get; set; } = 2;

        public int AgedWoodLocatorCubeMediumRange { get; set; } = 75;
        public int AgedWoodLocatorCubeMediumCost { get; set; } = 10;

        public int AgedWoodLocatorCubeLargeRange { get; set; } = 150;
        public int AgedWoodLocatorCubeLargeCost { get; set; } = 25;
        public bool AgedWoodLocatorUseCupronickelPlates { get; set; } = false;


        public static ModConfig LoadOrCreate(ICoreAPI api)
        {
            ModConfig config;
            try
            {
                config = api.LoadModConfig<ModConfig>(FileName) ?? new ModConfig();
            }
            catch (JsonReaderException e)
            {
                BackupInvalidConfig(api, e);
                config = new ModConfig();
            }
            catch (Exception e)
            {
                BackupInvalidConfig(api, e);
                config = new ModConfig();
            }

            config.Normalize();
            api.StoreModConfig(config, FileName);
            Current = config;
            return config;
        }

        public static void Save(ICoreAPI api)
        {
            Current.Normalize();
            api.StoreModConfig(Current, FileName);
        }

        /// <summary>
        /// Serialize to JSON for network sync.
        /// </summary>
        public static string ToJson(ModConfig config)
        {
            return JsonConvert.SerializeObject(config, Formatting.None);
        }

        /// <summary>
        /// Load config from JSON (network sync or other sources). Best effort.
        /// </summary>
        public static void LoadSettingsJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var cfg = JsonConvert.DeserializeObject<ModConfig>(json);
                if (cfg == null) return;
                cfg.Normalize();
                Current = cfg;
            }
            catch
            {
                // ignore
            }
        }

        public void Normalize()
        {
            // Ranges: must be >= 1 (0 breaks searching), and clamp to something sane.
            // Costs: must be >= 0.
            TranslocatorLocatorSmallConeRange = ClampRange(TranslocatorLocatorSmallConeRange);
            TranslocatorLocatorConeRange = ClampRange(TranslocatorLocatorConeRange);
            TranslocatorLocatorCubeExtraSmallRange = ClampRange(TranslocatorLocatorCubeExtraSmallRange);
            TranslocatorLocatorCubeSmallRange = ClampRange(TranslocatorLocatorCubeSmallRange);
            TranslocatorLocatorCubeMediumRange = ClampRange(TranslocatorLocatorCubeMediumRange);
            TranslocatorLocatorCubeLargeRange = ClampRange(TranslocatorLocatorCubeLargeRange);

            AgedWoodLocatorSmallConeRange = ClampRange(AgedWoodLocatorSmallConeRange);
            AgedWoodLocatorConeRange = ClampRange(AgedWoodLocatorConeRange);
            AgedWoodLocatorCubeExtraSmallRange = ClampRange(AgedWoodLocatorCubeExtraSmallRange);
            AgedWoodLocatorCubeSmallRange = ClampRange(AgedWoodLocatorCubeSmallRange);
            AgedWoodLocatorCubeMediumRange = ClampRange(AgedWoodLocatorCubeMediumRange);
            AgedWoodLocatorCubeLargeRange = ClampRange(AgedWoodLocatorCubeLargeRange);

            TranslocatorLocatorSmallConeCost = ClampCost(TranslocatorLocatorSmallConeCost);
            TranslocatorLocatorConeCost = ClampCost(TranslocatorLocatorConeCost);
            TranslocatorLocatorCubeExtraSmallCost = ClampCost(TranslocatorLocatorCubeExtraSmallCost);
            TranslocatorLocatorCubeSmallCost = ClampCost(TranslocatorLocatorCubeSmallCost);
            TranslocatorLocatorCubeMediumCost = ClampCost(TranslocatorLocatorCubeMediumCost);
            TranslocatorLocatorCubeLargeCost = ClampCost(TranslocatorLocatorCubeLargeCost);

            AgedWoodLocatorSmallConeCost = ClampCost(AgedWoodLocatorSmallConeCost);
            AgedWoodLocatorConeCost = ClampCost(AgedWoodLocatorConeCost);
            AgedWoodLocatorCubeExtraSmallCost = ClampCost(AgedWoodLocatorCubeExtraSmallCost);
            AgedWoodLocatorCubeSmallCost = ClampCost(AgedWoodLocatorCubeSmallCost);
            AgedWoodLocatorCubeMediumCost = ClampCost(AgedWoodLocatorCubeMediumCost);
            AgedWoodLocatorCubeLargeCost = ClampCost(AgedWoodLocatorCubeLargeCost);

            // Clamp durability at <= 1000000, >= 0
            TranslocatorLocatorDurability = ClampDurability(TranslocatorLocatorDurability);
            AgedWoodLocatorDurability = ClampDurability(AgedWoodLocatorDurability);
        }

        public int GetRange(LocatorKind kind, LocatorMode mode)
        {
            return kind switch
            {
                LocatorKind.Translocator => mode switch
                {
                    LocatorMode.SmallCone => TranslocatorLocatorSmallConeRange,
                    LocatorMode.Cone => TranslocatorLocatorConeRange,
                    LocatorMode.CubeExtraSmall => TranslocatorLocatorCubeExtraSmallRange,
                    LocatorMode.CubeSmall => TranslocatorLocatorCubeSmallRange,
                    LocatorMode.CubeMedium => TranslocatorLocatorCubeMediumRange,
                    LocatorMode.CubeLarge => TranslocatorLocatorCubeLargeRange,
                    _ => TranslocatorLocatorSmallConeRange,
                },

                LocatorKind.AgedWood => mode switch
                {
                    LocatorMode.SmallCone => AgedWoodLocatorSmallConeRange,
                    LocatorMode.Cone => AgedWoodLocatorConeRange,
                    LocatorMode.CubeExtraSmall => AgedWoodLocatorCubeExtraSmallRange,
                    LocatorMode.CubeSmall => AgedWoodLocatorCubeSmallRange,
                    LocatorMode.CubeMedium => AgedWoodLocatorCubeMediumRange,
                    LocatorMode.CubeLarge => AgedWoodLocatorCubeLargeRange,
                    _ => AgedWoodLocatorSmallConeRange,
                },

                _ => 10,
            };
        }

        public int GetCost(LocatorKind kind, LocatorMode mode)
        {
            return kind switch
            {
                LocatorKind.Translocator => mode switch
                {
                    LocatorMode.SmallCone => TranslocatorLocatorSmallConeCost,
                    LocatorMode.Cone => TranslocatorLocatorConeCost,
                    LocatorMode.CubeExtraSmall => TranslocatorLocatorCubeExtraSmallCost,
                    LocatorMode.CubeSmall => TranslocatorLocatorCubeSmallCost,
                    LocatorMode.CubeMedium => TranslocatorLocatorCubeMediumCost,
                    LocatorMode.CubeLarge => TranslocatorLocatorCubeLargeCost,
                    _ => TranslocatorLocatorSmallConeCost,
                },

                LocatorKind.AgedWood => mode switch
                {
                    LocatorMode.SmallCone => AgedWoodLocatorSmallConeCost,
                    LocatorMode.Cone => AgedWoodLocatorConeCost,
                    LocatorMode.CubeExtraSmall => AgedWoodLocatorCubeExtraSmallCost,
                    LocatorMode.CubeSmall => AgedWoodLocatorCubeSmallCost,
                    LocatorMode.CubeMedium => AgedWoodLocatorCubeMediumCost,
                    LocatorMode.CubeLarge => AgedWoodLocatorCubeLargeCost,
                    _ => AgedWoodLocatorSmallConeCost,
                },

                _ => 1,
            };
        }

        private static int ClampRange(int value)
        {
            if (value < 1) return 1;
            if (value > 1024) return 1024;
            return value;
        }

        private static int ClampCost(int value)
        {
            if (value < 0) return 0;
            if (value > 9999) return 9999;
            return value;
        }

        private static int ClampDurability(int value)
        {
            if (value < 0) return 0;
            if (value > 1000000) return 1000000;
            return value;
        }

        private static void BackupInvalidConfig(ICoreAPI api, Exception e)
        {
            try
            {
                var configPath = Path.Combine(GamePaths.ModConfig, FileName);
                if (!File.Exists(configPath))
                {
                    api.Logger.Error($"[TranslocatorLocatorRedux] Failed to read config '{FileName}', but file was not found. Using defaults.\n{e}");
                    return;
                }

                var backupPath = configPath + $".invalid.{DateTime.UtcNow:yyyyMMdd_HHmmss}";
                File.Copy(configPath, backupPath, true);
                api.Logger.Error($"[TranslocatorLocatorRedux] Failed to read config '{FileName}'. Backed up the invalid file to '{backupPath}'. Using defaults.\n{e}");
            }
            catch (Exception backupEx)
            {
                api.Logger.Error($"[TranslocatorLocatorRedux] Failed to read config '{FileName}' and also failed to back it up. Using defaults.\nOriginal: {e}\nBackup: {backupEx}");
            }
        }
    }
}
