#nullable enable

using Terraria.Localization;
using Terraria.ModLoader;

namespace Ariadne.Accessibility;

[Autoload(Side = ModSide.Client)]
internal sealed class ScreenReaderRecoverySystem : ModSystem
{
	private const int FastRetryTicks = 60;
	private const int SlowRetryTicks = 300;
	private const int FastRetryCount = 10;

	private int _ticksUntilRetry = FastRetryTicks;
	private int _attempts;
	private bool _announcedRecovery;

	public override void PostUpdateEverything()
	{
		ScreenReaderService screenReader = AriadneMod.ScreenReader;
		if (screenReader.IsAvailable)
		{
			_attempts = 0;
			_ticksUntilRetry = FastRetryTicks;
			return;
		}
		if (!screenReader.CanRetry || --_ticksUntilRetry > 0)
		{
			return;
		}

		_attempts++;
		_ticksUntilRetry = _attempts < FastRetryCount ? FastRetryTicks : SlowRetryTicks;
		if (!screenReader.Initialize(Mod, logFailure: false))
		{
			return;
		}

		if (!_announcedRecovery)
		{
			_announcedRecovery = true;
			screenReader.Output(Language.GetTextValue("Mods.Ariadne.Announcements.SpeechRestored"));
		}
	}

	public override void Unload()
	{
		_ticksUntilRetry = FastRetryTicks;
		_attempts = 0;
		_announcedRecovery = false;
	}
}
