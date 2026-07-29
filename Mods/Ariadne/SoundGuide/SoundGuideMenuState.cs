#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Ariadne.Configs;
using Ariadne.Menus;

namespace Ariadne.SoundGuide;

/// <summary>
/// The Ariadne Sound Guide: one option per cue the mod makes, with what the cue
/// means and a way to hear it.
///
/// Enter plays the variant the focused option names and then moves that option on to
/// the next one, so a listener who only ever presses Enter still hears every variant
/// of a cue; the label always says what the next press will play. Left and Right
/// choose a variant deliberately and speak its name, which is the same idiom every
/// other adjustable option in Ariadne uses.
/// </summary>
internal sealed class SoundGuideMenuState : AccessibleMenuState
{
	private readonly List<SoundGuideEntry> _cues = SoundGuideCatalog.Build();
	private readonly int[] _variants;
	private SoundGuidePlayer? _player;
	private int _lastSelectedIndex;

	internal SoundGuideMenuState(AccessibleMenuController controller)
		: base(controller)
	{
		_variants = new int[_cues.Count];
	}

	protected override string Title =>
		Language.GetTextValue("Mods.Ariadne.SoundGuide.Title");

	protected override string AdditionalControlHint =>
		"    " + Language.GetTextValue("Mods.Ariadne.SoundGuide.ControlHint");

	protected override string AdditionalNavigationInstructions =>
		" " + Language.GetTextValue("Mods.Ariadne.SoundGuide.Instructions");

	protected override string ActivationHelp =>
		Language.GetTextValue("Mods.Ariadne.SoundGuide.Help.Enter");

	public override void OnActivate()
	{
		_player = SoundGuideSystem.Open();
		_lastSelectedIndex = SelectedIndex;
		base.OnActivate();
	}

	public override void OnDeactivate()
	{
		SoundGuideSystem.Close();
		_player = null;
		base.OnDeactivate();
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		// The base call can navigate away, which deactivates this screen and releases
		// the player from under it.
		if (_player is null)
		{
			return;
		}

		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		if (SelectedIndex != _lastSelectedIndex)
		{
			_lastSelectedIndex = SelectedIndex;
			_player.Stop(config);
		}
		_player.Update(config);
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		for (int index = 0; index < _cues.Count; index++)
		{
			int cueIndex = index;
			bool hasVariants = _cues[index].Variants.Count > 1;
			entries.Add(new(
				() => Label(cueIndex),
				previousValue: hasVariants ? () => CycleVariant(cueIndex, -1) : null,
				nextValue: hasVariants ? () => CycleVariant(cueIndex, 1) : null,
				description: () => Describe(cueIndex),
				role: "sound",
				adjustmentAnnouncement: () => _cues[cueIndex].VariantName(_variants[cueIndex])));
		}
	}

	protected override bool HandleAdditionalInput(KeyboardState keyboard, GameTime gameTime)
	{
		if (!Pressed(keyboard, Keys.Enter))
		{
			return false;
		}

		// Claimed here rather than left to the base, whose activation would speak the
		// whole option again over the cue it had just started.
		Main.chatRelease = false;
		PlaySelection();
		return true;
	}

	protected override void AddContextHelpTopics(List<AccessibleHelpTopic> topics)
	{
		topics.Add(new(
			Language.GetTextValue("Mods.Ariadne.SoundGuide.Help.VariantsControl"),
			Language.GetTextValue("Mods.Ariadne.SoundGuide.Help.Variants")));
		topics.Add(new(
			Language.GetTextValue("Mods.Ariadne.SoundGuide.Help.VolumesControl"),
			Language.GetTextValue("Mods.Ariadne.SoundGuide.Help.Volumes")));
	}

	private void PlaySelection()
	{
		if (_cues.Count == 0)
		{
			return;
		}
		if (_player is null)
		{
			Announce(Language.GetTextValue("Mods.Ariadne.SoundGuide.Unavailable"));
			return;
		}

		int cueIndex = Math.Clamp(SelectedIndex, 0, _cues.Count - 1);
		SoundGuideEntry cue = _cues[cueIndex];
		cue.Variants[_variants[cueIndex]].Play(
			_player,
			ModContent.GetInstance<AriadneClientConfig>());
		if (cue.Variants.Count <= 1)
		{
			return;
		}

		_variants[cueIndex] = (_variants[cueIndex] + 1) % cue.Variants.Count;
		RebuildEntries();
	}

	private void CycleVariant(int cueIndex, int direction)
	{
		int count = _cues[cueIndex].Variants.Count;
		_variants[cueIndex] = (_variants[cueIndex] + direction + count) % count;
	}

	private string Label(int cueIndex)
	{
		SoundGuideEntry cue = _cues[cueIndex];
		return cue.Variants.Count > 1
			? Language.GetTextValue(
				"Mods.Ariadne.SoundGuide.Label",
				cue.Name,
				cue.VariantName(_variants[cueIndex]))
			: cue.Name;
	}

	private string Describe(int cueIndex)
	{
		SoundGuideEntry cue = _cues[cueIndex];
		int variant = _variants[cueIndex];
		StringBuilder description = new(cue.Description);
		description.Append(' ').Append(cue.VariantDetail(variant));
		if (cue.Variants.Count > 1)
		{
			description.Append(' ').Append(Language.GetTextValue(
				"Mods.Ariadne.SoundGuide.VariantPosition",
				variant + 1,
				cue.Variants.Count));
		}

		string? note = SilenceNote(cue);
		if (note is not null)
		{
			description.Append(' ').Append(note);
		}
		return description.ToString();
	}

	/// <summary>
	/// Why a press would produce nothing, when that is the case. Silence with no
	/// explanation reads as a broken cue rather than as a volume the listener set.
	/// </summary>
	private string? SilenceNote(SoundGuideEntry cue)
	{
		if (_player is null)
		{
			return Language.GetTextValue("Mods.Ariadne.SoundGuide.Unavailable");
		}
		if (Main.soundVolume <= 0f)
		{
			return Language.GetTextValue("Mods.Ariadne.SoundGuide.GameVolumeOff");
		}

		AriadneClientConfig config = ModContent.GetInstance<AriadneClientConfig>();
		return cue.VolumePercent?.Invoke(config) <= 0
			? Language.GetTextValue("Mods.Ariadne.SoundGuide.CueVolumeOff")
			: null;
	}
}
