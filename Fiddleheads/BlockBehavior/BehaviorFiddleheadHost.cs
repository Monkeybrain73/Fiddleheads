using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable

namespace Fiddleheads
{
    /// <summary>
    /// Allows a block to have fiddleheads naturally spawn on it.
    /// Uses the code "FiddleheadHost", and has no properties.
    /// </summary>
    /// <example><code lang="json">
    ///"behaviors": [
	///	{
	///		"name": "FiddleheadHost"
	///	}
	///]
    /// </code></example>
    [DocumentAsJson]
    public class BlockBehaviorFiddleheadHost : BlockBehavior
    {

        public BlockBehaviorFiddleheadHost(Block block) : base(block)
        {
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
        {
            world.BlockAccessor.RemoveBlockEntity(pos);
        }

    }
}