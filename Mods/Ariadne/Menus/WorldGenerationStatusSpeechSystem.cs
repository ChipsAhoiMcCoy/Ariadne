#nullable enable

using System;
using Ariadne.Logic;
using Terraria;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace Ariadne.Menus;

[Autoload(Side = ModSide.Client)]
internal sealed class WorldGenerationStatusSpeechSystem : ModSystem
{
	private static bool _monitoring;
	private static bool _sawTransition;
	private static bool _waitingForFreshStatus;
	private static string _statusAtStart = string.Empty;
	private static string _lastStatus = string.Empty;

	internal static void BeginGeneration() => Begin();

	internal static void BeginLoading() => Begin();

	internal static void Cancel() => Reset();

	private static void Begin()
	{
		_monitoring = true;
		_sawTransition = false;
		_waitingForFreshStatus = true;
		_statusAtStart = Main.statusText ?? string.Empty;
		_lastStatus = string.Empty;
	}

	public override void PostUpdateInput()
	{
		if (!_monitoring)
		{
			return;
		}

		bool transitionActive = WorldGen.generatingWorld || Main.gameMenu && Main.menuMode == 10;
		if (transitionActive)
		{
			_sawTransition = true;
			GenerationProgress? generationProgress = WorldGenerator.CurrentGenerationProgress;
			string rawStatus = generationProgress?.Message ?? Main.statusText ?? string.Empty;
			if (_waitingForFreshStatus)
			{
				bool generationStatusAvailable = generationProgress is not null;
				bool loadingStatusChanged = !string.Equals(rawStatus, _statusAtStart, StringComparison.Ordinal);
				if (!generationStatusAvailable && !loadingStatusChanged)
				{
					return;
				}
				_waitingForFreshStatus = false;
			}

			string status = WorldTransitionLogic.NormalizeStatus(rawStatus);
			if (status.Length > 0 && !string.Equals(status, _lastStatus, StringComparison.Ordinal))
			{
				_lastStatus = status;
				AriadneMod.ScreenReader.Output(status, interrupt: true);
			}
			return;
		}

		if (_sawTransition)
		{
			Reset();
		}
	}

	public override void Unload() => Reset();

	private static void Reset()
	{
		_monitoring = false;
		_sawTransition = false;
		_waitingForFreshStatus = false;
		_statusAtStart = string.Empty;
		_lastStatus = string.Empty;
	}
}
