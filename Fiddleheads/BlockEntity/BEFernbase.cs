using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

#nullable disable

namespace Fiddleheads
{
    public class FiddleheadProps
    {
        public float DieWhenTempBelow = 0;
        public bool DieAfterFruiting = false;
    }

    public class WildFiddleheadProps
    {
        public int GrowRange = 7;
        public int MinCount = 2;
        public int MaxCount = 12;

        public float GrowingMonthsMin = 0.5f;
        public float GrowingMonthsMax = 1.0f;

        public float FruitingMonthsMin = 1.5f;
        public float FruitingMonthsMax = 2.5f;

        public float WildGrowthStartTemp = 2f;
        public float WildGrowthOptimalTemp = 10f;

        public float WildFernDropMul = 0.85f;
    }


    public class FiddleheadSystem : ModSystem
    {
        ICoreAPI api;

        public static LCGRandom lcgrnd;
        public static NormalRandom rndn;
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            api.ChatCommands.GetOrCreate("debug")
                .BeginSubCommand("fidd")
                    .BeginSubCommand("regrow")
                        .WithDescription("FiddleheadSystem debug cmd")
                        .RequiresPrivilege(Privilege.controlserver)
                        .HandleWith(OnCmd)
                    .EndSubCommand()
                .EndSubCommand();

            api.Event.SaveGameLoaded += Event_SaveGameLoaded;

            this.api = api;
        }

        private void Event_SaveGameLoaded()
        {
            lcgrnd = new LCGRandom(api.World.Seed);
            rndn = new NormalRandom(api.World.Seed);
        }

        private TextCommandResult OnCmd(TextCommandCallingArgs args)
        {
            BlockPos pos = args.Caller.Entity.Pos.XYZ.AsBlockPos;

            BlockEntityFiddlehead bemc = api.World.BlockAccessor.GetBlockEntity(pos.DownCopy()) as BlockEntityFiddlehead;
            if (bemc == null)
            {
                return TextCommandResult.Success("No fiddlehead soil below you");
            }

            bemc.Regrow();

            return TextCommandResult.Success();
        }
    }

    public class BlockEntityFiddlehead : BlockEntity
    {
        Vec3i[] grownFiddleheadOffsets = Array.Empty<Vec3i>();

        double fiddleheadsGrownTotalDays = 0;
        double fiddleheadsDiedTotalDays = -999999;
        double fiddleheadsGrowingDays = 0;
        double lastUpdateTotalDays = 0;
        double patchSpawnTotalDays = 0;
        public double LastGrowthCheckDays = 0;

        AssetLocation fiddleheadBlockCode;

        FiddleheadProps props;
        WildFiddleheadProps wildProps;
        Block fiddleheadBlock;

        double fruitingDays = 10;
        double growingDays = 20;
        // int growRange = 7;



        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api.Side == EnumAppSide.Server)
            {
                int interval = 10000;
                RegisterGameTickListener(onServerTick, interval, -api.World.Rand.Next(interval));

                if (fiddleheadBlockCode != null && !setFiddleheadBlock(Api.World.GetBlock(fiddleheadBlockCode)))
                {
                    api.Logger.Error("Invalid fiddlehead type '{0}' at {1}. Will delete block entity.", fiddleheadBlockCode, Pos);
                    Api.Event.EnqueueMainThreadTask(() => Api.World.BlockAccessor.RemoveBlockEntity(Pos), "deletefiddleheadBE");
                }
            }

            if (LastGrowthCheckDays <= 0)
            {
                LastGrowthCheckDays = api.World.Calendar.TotalDays;
            }
        }

        private void onServerTick(float dt)
        {
            bool isFruiting = grownFiddleheadOffsets.Length > 0;
            if (isFruiting && props.DieWhenTempBelow > -99)
            {
                float temperature = Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, Api.World.Calendar.TotalDays).Temperature;
                if (temperature < props.DieWhenTempBelow)
                {
                    DestroyGrownFiddleheads();
                    return;
                }
            }

            if (props.DieAfterFruiting && isFruiting && fiddleheadsGrownTotalDays + fruitingDays < Api.World.Calendar.TotalDays)
            {
                DestroyGrownFiddleheads();
                return;
            }

            if (!isFruiting)
            {
                lastUpdateTotalDays = Math.Max(lastUpdateTotalDays, Api.World.Calendar.TotalDays - 50); // Don't check more than 50 days into the past

                while (Api.World.Calendar.TotalDays - lastUpdateTotalDays > 1)
                {
                    float temperature = Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, lastUpdateTotalDays + 0.5).Temperature;

                    double elapsedDays = Api.World.Calendar.TotalDays - lastUpdateTotalDays;
                    float growthFactor = BlockFiddlehead.GetWildGrowthFactor(temperature, wildProps);

                    fiddleheadsGrowingDays += elapsedDays * growthFactor;

                    lastUpdateTotalDays++;
                }

                if (fiddleheadsGrowingDays > growingDays)
                {
                    growFiddleheads(Api.World.BlockAccessor, FiddleheadSystem.rndn);
                    fiddleheadsGrowingDays = 0;
                }
            }
            else
            {
                if (Api.World.Calendar.TotalDays - lastUpdateTotalDays > 0.1)
                {
                    UpdatePatchGrowth();
                    lastUpdateTotalDays = Api.World.Calendar.TotalDays;

                    for (int i = 0; i < grownFiddleheadOffsets.Length; i++)
                    {
                        var offset = grownFiddleheadOffsets[i];
                        var pos = Pos.AddCopy(offset);
                        var chunk = Api.World.BlockAccessor.GetChunkAtBlockPos(pos);
                        if (chunk == null) return;

                        if (!Api.World.BlockAccessor.GetBlock(pos).Code.Equals(fiddleheadBlockCode))
                        {
                            grownFiddleheadOffsets = grownFiddleheadOffsets.RemoveAt(i);
                            i--;
                        }
                    }
                }
            }
        }

        public void Regrow()
        {
            DestroyGrownFiddleheads();
            growFiddleheads(Api.World.BlockAccessor, FiddleheadSystem.rndn);
        }
        private void DestroyGrownFiddleheads()
        {
            fiddleheadsDiedTotalDays = Api.World.Calendar.TotalDays;
            foreach (var offset in grownFiddleheadOffsets)
            {
                // Api.Logger.Notification($"Destroying patch from {Pos}");

                BlockPos pos = Pos.AddCopy(offset);
                var block = Api.World.BlockAccessor.GetBlock(pos);
                if (block.Variant["fiddlehead"] == fiddleheadBlock.Variant["fiddlehead"])
                {
                    Api.World.BlockAccessor.SetBlock(0, pos);
                }
            }

            grownFiddleheadOffsets = Array.Empty<Vec3i>();
        }

        bool setFiddleheadBlock(Block block)
        {
            this.fiddleheadBlock = block;
            this.fiddleheadBlockCode = block?.Code;

            if (block == null || Api?.Side != EnumAppSide.Server) return false;
            if (block.Attributes?["fiddleheadProps"].Exists != true) return false;

            props = block.Attributes["fiddleheadProps"].AsObject<FiddleheadProps>();
            wildProps = block.Attributes?["wildFiddleheadProps"]?.AsObject<WildFiddleheadProps>();
            FiddleheadSystem.lcgrnd.InitPositionSeed(fiddleheadBlockCode.GetHashCode(), (int)Api.World.Calendar.GetHemisphere(Pos) + 5);

            float daysPerMonth = Api.World.Calendar.DaysPerMonth;
            float randomFactor = (float)FiddleheadSystem.lcgrnd.NextDouble();

            growingDays = (wildProps.GrowingMonthsMin + randomFactor * (wildProps.GrowingMonthsMax - wildProps.GrowingMonthsMin)) * daysPerMonth;
            fruitingDays = (wildProps.FruitingMonthsMin + randomFactor * (wildProps.FruitingMonthsMax - wildProps.FruitingMonthsMin)) * daysPerMonth;

            return true;
        }

        public void OnGenerated(IBlockAccessor blockAccessor, IRandom rnd, BlockFiddlehead block)
        {
            setFiddleheadBlock(block);

            FiddleheadSystem.lcgrnd.InitPositionSeed(fiddleheadBlockCode.GetHashCode(), (int)(fiddleheadBlock as BlockFiddlehead).Api.World.Calendar.GetHemisphere(Pos));
            if (FiddleheadSystem.lcgrnd.NextDouble() < 0.33)
            {
                fiddleheadsGrowingDays = FiddleheadSystem.lcgrnd.NextDouble() * 10;
                return;
            }
            growFiddleheads(blockAccessor, rnd);
        }

        private void growFiddleheads(IBlockAccessor blockAccessor, IRandom rnd)
        {
            generateUpGrowingFiddleheads(blockAccessor, rnd);

            patchSpawnTotalDays = Api.World.Calendar.TotalDays;

            fiddleheadsGrownTotalDays = (fiddleheadBlock as BlockFiddlehead).Api.World.Calendar.TotalDays - rnd.NextDouble() * fruitingDays;
        }

        private void generateUpGrowingFiddleheads(IBlockAccessor blockAccessor, IRandom rnd)
        {
            if (fiddleheadBlock == null) return;

            int cnt = wildProps.MinCount + rnd.NextInt(wildProps.MaxCount - wildProps.MinCount + 1);

            // Org
            // int cnt = 2 + rnd.NextInt(11);

            BlockPos pos = new BlockPos(Pos.dimension);
            const int chunkSize = GlobalConstants.ChunkSize;
            List<Vec3i> offsets = new List<Vec3i>();

            if (!isChunkAreaLoaded(blockAccessor, wildProps.GrowRange)) return;

            while (cnt-- > 0)
            {
                int dx = wildProps.GrowRange - rnd.NextInt(2 * wildProps.GrowRange + 1);
                int dz = wildProps.GrowRange - rnd.NextInt(2 * wildProps.GrowRange + 1);

                pos.Set(Pos.X + dx, 0, Pos.Z + dz);

                var mapChunk = blockAccessor.GetMapChunkAtBlockPos(pos);
                if (mapChunk == null) continue;
                int lx = GameMath.Mod(pos.X, chunkSize);
                int lz = GameMath.Mod(pos.Z, chunkSize);

                pos.Y = mapChunk.WorldGenTerrainHeightMap[lz * GlobalConstants.ChunkSize + lx] + 1;

                Block hereBlock = blockAccessor.GetBlock(pos);
                Block belowBlock = blockAccessor.GetBlockBelow(pos);

                if (belowBlock.Fertility < 10 || hereBlock.LiquidCode != null) continue;

                if ((fiddleheadsGrownTotalDays == 0 && hereBlock.Replaceable >= 6000) || hereBlock.Id == 0)
                {
                    blockAccessor.SetBlock(fiddleheadBlock.Id, pos);
                    offsets.Add(new Vec3i(dx, pos.Y - Pos.Y, dz));
                }
            }

            this.grownFiddleheadOffsets = offsets.ToArray();
        }

        private bool isChunkAreaLoaded(IBlockAccessor blockAccessor, int growRange)
        {
            const int chunksize = GlobalConstants.ChunkSize;
            int mincx = (Pos.X - growRange) / chunksize;
            int maxcx = (Pos.X + growRange) / chunksize;

            int mincz = (Pos.Z - growRange) / chunksize;
            int maxcz = (Pos.Z + growRange) / chunksize;

            for (int cx = mincx; cx <= maxcx; cx++)
            {
                for (int cz = mincz; cz <= maxcz; cz++)
                {
                    if (blockAccessor.GetChunk(cx, Pos.InternalY / chunksize, cz) == null) return false;
                }
            }

            return true;
        }

        private void UpdatePatchGrowth()
        {
            int targetStage = GetTargetStage();

            foreach (var offset in grownFiddleheadOffsets)
            {
                BlockPos pos = Pos.AddCopy(offset);

                Block block =
                    Api.World.BlockAccessor.GetBlock(pos);

                if (!(block is BlockFiddlehead fiddlehead))
                {
                    continue;
                }

                if (fiddlehead.CurrentStage() >= targetStage)
                {
                    continue;
                }

                Block newBlock =
                    Api.World.GetBlock(
                        block.CodeWithParts(
                            targetStage.ToString()
                        )
                    );

                if (newBlock != null)
                {
                    Api.World.BlockAccessor.ExchangeBlock(
                        newBlock.Id,
                        pos
                    );
                }
            }
        }

        private double CalculateGrowthDaysSinceSpawn()
        {
            double totalGrowthDays = 0;

            double now = Api.World.Calendar.TotalDays;

            for (double day = patchSpawnTotalDays; day < now; day += 1)
            {
                float temp =
                    Api.World.BlockAccessor.GetClimateAt(
                        Pos,
                        EnumGetClimateMode.ForSuppliedDate_TemperatureOnly,
                        day + 0.5
                    ).Temperature;

                totalGrowthDays +=
                    BlockFiddlehead.GetWildGrowthFactor(
                        temp,
                        wildProps
                    );
            }

            return totalGrowthDays;
        }

        private int GetTargetStage()
        {
            double growthDays =
                CalculateGrowthDaysSinceSpawn();

            double stageDuration =
                growingDays / 8.0;

            int stage =
                1 + (int)(growthDays / stageDuration);

            return GameMath.Clamp(stage, 1, 9);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            fiddleheadBlockCode = new AssetLocation(tree.GetString("fiddleheadBlockCode"));
            grownFiddleheadOffsets = tree.GetVec3is("grownFiddleheadOffsets");

            fiddleheadsGrownTotalDays = tree.GetDouble("fiddleheadsGrownTotalDays");
            fiddleheadsDiedTotalDays = tree.GetDouble("fiddleheadsDiedTotalDays");
            lastUpdateTotalDays = tree.GetDouble("lastUpdateTotalDays");
            fiddleheadsGrowingDays = tree.GetDouble("fiddleheadsGrowingDays");
            LastGrowthCheckDays = tree.GetDouble("lastGrowthCheckDays");
            patchSpawnTotalDays = tree.GetDouble("patchSpawnTotalDays");

            setFiddleheadBlock(worldAccessForResolve.GetBlock(fiddleheadBlockCode));
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetString("fiddleheadBlockCode", fiddleheadBlockCode.ToShortString());
            tree.SetVec3is("grownFiddleheadOffsets", grownFiddleheadOffsets);
            tree.SetDouble("fiddleheadsGrownTotalDays", fiddleheadsGrownTotalDays);
            tree.SetDouble("fiddleheadsDiedTotalDays", fiddleheadsDiedTotalDays);

            tree.SetDouble("lastUpdateTotalDays", lastUpdateTotalDays);
            tree.SetDouble("fiddleheadsGrowingDays", fiddleheadsGrowingDays);
            tree.SetDouble("lastGrowthCheckDays", LastGrowthCheckDays);
            tree.SetDouble("patchSpawnTotalDays", patchSpawnTotalDays);
        }


    }
}