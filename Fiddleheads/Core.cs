using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo("fiddleheads",
                    Authors = new string[] { "xXx_Ape_xXx" },
                    Description = "Adds natural spawning fiddlehead ferns that can be propagated and used in meals",
                    Version = "1.0.1")]


namespace Fiddleheads
{
    public class Core : ModSystem
    {
        ICoreAPI api;
        // ICoreClientAPI capi;
        // ICoreServerAPI sapi;

        public override void Start(ICoreAPI api)
        {
            this.api = api;

            RegisterDefaultBlocks();
            RegisterDefaultBlockBehaviors();

            RegisterDefaultBlockEntities();

            api.Logger.Event("[Fiddleheads] mod started");

        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            // base.StartServerSide(api);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            // base.StartClientSide(api);
        }

        private void RegisterDefaultBlocks()
        {
            api.RegisterBlockClass("BlockFiddlehead", typeof(BlockFiddlehead));
        }

        private void RegisterDefaultBlockBehaviors()
        {
            api.RegisterBlockBehaviorClass("FiddleheadHost", typeof(BlockBehaviorFiddleheadHost));
        }

        private void RegisterDefaultBlockEntities()
        {
            api.RegisterBlockEntityClass("BEFiddlehead", typeof(BlockEntityFiddlehead));
        }

        public override void Dispose()
        {
            base.Dispose();
        }


    }
}
