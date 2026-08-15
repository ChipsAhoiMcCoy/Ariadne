#nullable enable

using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Ariadne.Logic;

namespace Ariadne.Ingame;

internal static class HerbNameResolver
{
	internal static string Describe(Tile tile, string baseName)
	{
		if (tile.TileType is not (TileID.ImmatureHerbs or TileID.MatureHerbs or TileID.BloomingHerbs))
		{
			return baseName;
		}

		int style = tile.TileFrameX / 18;
		HerbGrowthStage stage = WorldGen.IsHarvestableHerbWithSeed(tile.TileType, style)
			? HerbGrowthStage.Blooming
			: tile.TileType == TileID.ImmatureHerbs
				? HerbGrowthStage.Immature
				: HerbGrowthStage.Mature;
		string stageKey = stage switch
		{
			HerbGrowthStage.Immature => "Mods.Ariadne.HerbStages.Immature",
			HerbGrowthStage.Mature => "Mods.Ariadne.HerbStages.Mature",
			_ => "Mods.Ariadne.HerbStages.Blooming",
		};
		string stageText = Language.GetTextValue(stageKey);
		if (stageText == stageKey) stageText = HerbGrowthStageText.Describe(stage);
		return $"{baseName}, {stageText}";
	}
}
