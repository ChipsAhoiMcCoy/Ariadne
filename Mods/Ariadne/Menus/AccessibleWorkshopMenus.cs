#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.Initializers;
using Terraria.IO;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.Social;

namespace Ariadne.Menus;

internal sealed class AccessibleWorkshopMenuState : AccessibleMenuState
{
	internal AccessibleWorkshopMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Language.GetTextValue("UI.WorkshopHub");

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => Language.GetTextValue("tModLoader.MenuManageMods"),
			() => Controller.Navigate(new AccessibleManageModsMenuState(Controller)),
			description: () => Language.GetTextValue("tModLoader.MenuManageModsDescription")));
		entries.Add(new(
			() => Language.GetTextValue("tModLoader.MenuDevelopMods"),
			() => Controller.Navigate(new AccessibleModSourcesMenuState(Controller)),
			description: () => Language.GetTextValue("tModLoader.MenuDevelopModsDescription")));
		entries.Add(new(
			() => Language.GetTextValue("tModLoader.MenuDownloadMods"),
			OpenModBrowser,
			description: () => "Opens tModLoader's in-game Mod Browser through Ariadne's universal semantic interface."));
		entries.Add(new(
			() => "Steam Workshop website",
			OpenModWorkshop,
			description: () => "Opens the tModLoader Steam Workshop in the platform web browser."));
		entries.Add(new(
			() => Language.GetTextValue("tModLoader.ModsModPacks"),
			() => Controller.Navigate(new AccessibleModPacksMenuState(Controller)),
			description: () => Language.GetTextValue("tModLoader.MenuModPackDescription")));
		entries.Add(new(
			() => Language.GetTextValue("Workshop.HubWorlds"),
			() => Controller.Navigate(new AccessibleWorkshopWorldImportMenuState(Controller)),
			description: () => Language.GetTextValue("Workshop.HubDescriptionImportWorlds"),
			enabled: () => SocialAPI.Workshop is not null));
		entries.Add(new(
			() => Language.GetTextValue("Workshop.HubResourcePacks"),
			() => Controller.Navigate(new AccessibleResourcePacksMenuState(Controller)),
			description: () => Language.GetTextValue("Workshop.HubDescriptionUseResourcePacks")));
		entries.Add(new(
			() => Language.GetTextValue("Workshop.ReportLogsButton"),
			OpenLogsFolder,
			description: () => "Opens the tModLoader log directory."));
		entries.Add(new(
			() => "All Workshop and publishing tools",
			OpenFullWorkshopHub,
			description: () => "Opens Terraria's complete Workshop hub through Ariadne's universal semantic interface, including world and resource-pack publishing screens."));
	}

	private static void OpenModWorkshop()
	{
		Utils.OpenToURL("https://steamcommunity.com/app/1281930/workshop/");
	}

	private static void OpenModBrowser()
	{
		Main.menuMode = 10007;
	}

	private static void OpenLogsFolder()
	{
		Utils.OpenFolder(Logging.LogDir);
	}

	private void OpenFullWorkshopHub()
	{
		Main.menuMode = 888;
		Main.MenuUI.SetState(new UIWorkshopHub(this));
	}
}

internal sealed class AccessibleManageModsMenuState : AccessibleMenuState
{
	private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
	private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

	private static readonly Type? OrganizerType = typeof(Mod).Assembly.GetType("Terraria.ModLoader.Core.ModOrganizer");
	private static readonly MethodInfo? FindMods = OrganizerType?.GetMethod("FindMods", StaticMembers);
	private static readonly MethodInfo? DeleteMod = OrganizerType?.GetMethod("DeleteMod", StaticMembers);
	private static readonly MethodInfo? ReloadMods = typeof(Terraria.ModLoader.ModLoader).GetMethod("Reload", StaticMembers);
	private readonly Dictionary<string, int> _actionIndices = new(StringComparer.OrdinalIgnoreCase);
	private List<LocalModView> _mods = [];

	internal AccessibleManageModsMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Language.GetTextValue("tModLoader.MenuManageMods");

	public override void OnActivate()
	{
		_mods = LoadMods();
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(
			() => "Reload mods to apply changes",
			Reload,
			description: () => "Reloads all enabled mods."));
		entries.Add(new(
			() => Language.GetTextValue("tModLoader.ModsOpenModsFolders"),
			OpenModsFolder));
		entries.Add(new(
			() => Language.GetTextValue("tModLoader.ModConfiguration"),
			() => Controller.Navigate(new AccessibleModConfigListMenuState(Controller)),
			description: () => "Browse the configurations registered by loaded mods."));

		foreach (LocalModView mod in _mods)
		{
			LocalModView capturedMod = mod;
			List<AccessibleMenuEntry> actions = GetActionEntries(capturedMod);
			_actionIndices[capturedMod.Name] = ((GetActionIndex(capturedMod.Name) % actions.Count) + actions.Count) % actions.Count;

			AccessibleMenuEntry CurrentAction()
			{
				int index = Math.Clamp(GetActionIndex(capturedMod.Name), 0, actions.Count - 1);
				return actions[index];
			}

			entries.Add(new(
				() => ModActionLabel(capturedMod, CurrentAction()),
				() => ActivateAction(CurrentAction()),
				previousValue: () => CycleAction(capturedMod.Name, actions.Count, -1),
				nextValue: () => CycleAction(capturedMod.Name, actions.Count, 1),
				description: () => ModActionDescription(capturedMod, CurrentAction(), GetActionIndex(capturedMod.Name), actions.Count),
				role: "mod with actions",
				adjustmentAnnouncement: () => ActionAdjustmentAnnouncement(CurrentAction())));
		}
	}

	private List<AccessibleMenuEntry> GetActionEntries(LocalModView mod)
	{
		List<AccessibleMenuEntry> actions =
		[
			new(
				() => mod.Enabled ? "Disable" : "Enable",
				() => Toggle(mod),
				description: () => $"{(mod.Enabled ? "Disable" : "Enable")} {mod.DisplayName}. Changes take effect after reloading mods."),
			new(
				() => Language.GetTextValue("tModLoader.ModsMoreInfo"),
				() => Controller.Navigate(new AccessibleModInfoMenuState(Controller, mod)),
				description: () => $"Read the description and metadata for {mod.DisplayName}."),
		];

		if (mod.HasConfig)
		{
			actions.Add(new(
				() => "Config",
				() => OpenConfig(mod),
				description: () => $"Open the configuration list for {mod.DisplayName}."));
		}
		if (mod.CanDelete)
		{
			actions.Add(new(
				() => Language.GetTextValue("UI.Delete"),
				() => ConfirmDelete(mod),
				description: () => $"Permanently remove {mod.DisplayName}."));
		}

		return actions;
	}

	private int GetActionIndex(string modName) => _actionIndices.GetValueOrDefault(modName);

	private void CycleAction(string modName, int count, int offset)
	{
		_actionIndices[modName] = (GetActionIndex(modName) + offset + count) % count;
	}

	private void ActivateAction(AccessibleMenuEntry action)
	{
		if (!action.IsEnabled)
		{
			Announce($"{action.Label()}, unavailable. {action.Description?.Invoke()}");
			return;
		}
		(action.Activate ?? action.NextValue ?? action.PreviousValue)?.Invoke();
	}

	private static string ModActionLabel(LocalModView mod, AccessibleMenuEntry action)
	{
		string unavailable = action.IsEnabled ? string.Empty : ", unavailable";
		return $"{mod.DisplayName}: {(mod.Enabled ? "enabled" : "disabled")}. Action: {action.Label()}{unavailable}";
	}

	private static string ActionAdjustmentAnnouncement(AccessibleMenuEntry action)
	{
		string unavailable = action.IsEnabled ? string.Empty : ", unavailable";
		return $"{action.Label()}{unavailable}";
	}

	private static string ModActionDescription(LocalModView mod, AccessibleMenuEntry action, int index, int count)
	{
		string author = string.IsNullOrWhiteSpace(mod.Author) ? string.Empty : $" By {mod.Author}.";
		string unavailable = action.IsEnabled ? string.Empty : " This action is unavailable.";
		string description = action.Description?.Invoke() ?? string.Empty;
		return $"Internal name {mod.Name}. Version {mod.Version}.{author} Installed from {mod.Location}. " +
			$"Current action {index + 1} of {count}: {action.Label()}.{unavailable} {description} " +
			"Left and Right change the action; Enter activates it.";
	}

	private void Reload()
	{
		if (ReloadMods is null)
		{
			Announce("Reload is unavailable because this tModLoader version changed its mod loader API.");
			return;
		}
		Controller.ClearHistory();
		ReloadMods.Invoke(null, null);
	}

	private static void OpenModsFolder()
	{
		Directory.CreateDirectory(Terraria.ModLoader.ModLoader.ModPath);
		Utils.OpenFolder(Terraria.ModLoader.ModLoader.ModPath);
	}

	private void Toggle(LocalModView mod)
	{
		try
		{
			mod.EnabledProperty.SetValue(mod.Source, !mod.Enabled);
			mod.Enabled = !mod.Enabled;
		}
		catch (Exception exception)
		{
			Announce($"{mod.DisplayName} could not be changed. See the tModLoader client log.");
			ModContent.GetInstance<AriadneMod>().Logger.Error($"Could not toggle mod {mod.Name}.", exception);
		}
	}

	private void OpenConfig(LocalModView mod)
	{
		AccessibleModConfigListMenuState.OpenForMod(Controller, mod.Name);
	}

	private void ConfirmDelete(LocalModView mod)
	{
		Controller.Navigate(new AccessibleConfirmationMenuState(
			Controller,
			$"Delete {mod.DisplayName}?",
			"This permanently removes the installed mod. Workshop mods may need to be unsubscribed by tModLoader.",
			() => Delete(mod)));
	}

	private void Delete(LocalModView mod)
	{
		if (DeleteMod is null)
		{
			Announce("Deleting mods is unavailable because this tModLoader version changed its mod organizer API.");
			return;
		}

		try
		{
			DeleteMod.Invoke(null, [mod.Source]);
			_actionIndices.Remove(mod.Name);
			Announce($"Deleted {mod.DisplayName}.");
			Controller.Back();
		}
		catch (Exception exception)
		{
			Announce($"{mod.DisplayName} could not be deleted. See the tModLoader client log.");
			ModContent.GetInstance<AriadneMod>().Logger.Error($"Could not delete mod {mod.Name}.", exception);
		}
	}

	private static List<LocalModView> LoadMods()
	{
		if (FindMods?.Invoke(null, [true]) is not IEnumerable mods)
		{
			return [];
		}

		List<LocalModView> result = [];
		HashSet<string> configurableMods = [.. AccessibleModConfigCatalog.Load().Select(view => view.Owner.Name)];
		foreach (object mod in mods)
		{
			Type type = mod.GetType();
			PropertyInfo? enabled = type.GetProperty("Enabled");
			if (enabled is null)
			{
				continue;
			}
			object? properties = type.GetField("properties", InstanceMembers)?.GetValue(mod);
			string PropertyText(string name)
			{
				return properties?.GetType().GetField(name, InstanceMembers)?.GetValue(properties)?.ToString() ?? string.Empty;
			}

			string name = type.GetProperty("Name")?.GetValue(mod)?.ToString() ?? "Unknown";
			string location = type.GetField("location", InstanceMembers)?.GetValue(mod)?.ToString() ?? "unknown source";
			bool loaded = Terraria.ModLoader.ModLoader.TryGetMod(name, out _);
			result.Add(new LocalModView(
				mod,
				enabled,
				name,
				type.GetField("DisplayNameClean")?.GetValue(mod)?.ToString()
					?? type.GetProperty("DisplayNameClean")?.GetValue(mod)?.ToString()
					?? type.GetProperty("DisplayName")?.GetValue(mod)?.ToString()
					?? "Unknown mod",
				type.GetProperty("Version")?.GetValue(mod)?.ToString() ?? "unknown",
				PropertyText("author"),
				PropertyText("description"),
				PropertyText("homepage"),
				location,
				configurableMods.Contains(name),
				!loaded && !string.Equals(location, "Modpack", StringComparison.OrdinalIgnoreCase),
				(bool)(enabled.GetValue(mod) ?? false)));
		}
		return [.. result.OrderByDescending(mod => mod.Enabled).ThenBy(mod => mod.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
	}

	private sealed record LocalModView(
		object Source,
		PropertyInfo EnabledProperty,
		string Name,
		string DisplayName,
		string Version,
		string Author,
		string Description,
		string Homepage,
		string Location,
		bool HasConfig,
		bool CanDelete,
		bool InitialEnabled)
	{
		internal bool Enabled { get; set; } = InitialEnabled;
	}

	private sealed class AccessibleModInfoMenuState : AccessibleMenuState
	{
		private readonly LocalModView _mod;

		internal AccessibleModInfoMenuState(AccessibleMenuController controller, LocalModView mod)
			: base(controller)
		{
			_mod = mod;
		}

		protected override string Title => $"More Info: {_mod.DisplayName}";

		protected override void BuildEntries(List<AccessibleMenuEntry> entries)
		{
			entries.Add(new(
				() => $"Status: {(_mod.Enabled ? "enabled" : "disabled")}",
				description: () => $"Internal name {_mod.Name}. Version {_mod.Version}. Installed from {_mod.Location}.",
				role: "information"));
			if (!string.IsNullOrWhiteSpace(_mod.Author))
			{
				entries.Add(new(() => $"Author: {_mod.Author}", role: "information"));
			}
			entries.Add(new(
				() => "Description",
				description: () => string.IsNullOrWhiteSpace(_mod.Description)
					? Language.GetTextValue("tModLoader.ModInfoNoDescriptionAvailable")
					: _mod.Description,
				role: "information"));
			if (!string.IsNullOrWhiteSpace(_mod.Homepage))
			{
				entries.Add(new(
					() => "Open homepage",
					() => Utils.OpenToURL(_mod.Homepage),
					description: () => _mod.Homepage));
			}
		}
	}
}

internal sealed class AccessibleResourcePacksMenuState : AccessibleMenuState
{
	private List<ResourcePack> _packs = [];

	internal AccessibleResourcePacksMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Language.GetTextValue("Workshop.HubResourcePacks");

	public override void OnActivate()
	{
		_packs = [.. AssetInitializer.CreateResourcePackList(((Game)Main.instance).Services).AllPacks];
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		foreach (ResourcePack pack in _packs.OrderByDescending(pack => pack.IsEnabled).ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase))
		{
			ResourcePack capturedPack = pack;
			entries.Add(new(
				() => $"{capturedPack.Name}: {(capturedPack.IsEnabled ? "enabled" : "disabled")}",
				() => Toggle(capturedPack),
				previousValue: () => Toggle(capturedPack),
				nextValue: () => Toggle(capturedPack),
				description: () => $"By {capturedPack.Author}. Version {capturedPack.Version}. {capturedPack.Description}",
				role: "toggle"));
		}
	}

	private void Toggle(ResourcePack pack)
	{
		pack.IsEnabled = !pack.IsEnabled;
		int order = 0;
		foreach (ResourcePack enabledPack in _packs.Where(candidate => candidate.IsEnabled).OrderBy(candidate => candidate.SortingOrder))
		{
			enabledPack.SortingOrder = order++;
		}
		Main.AssetSourceController.UseResourcePacks(new ResourcePackList(_packs));
		Main.SaveSettings();
	}
}

internal sealed class AccessibleWorkshopWorldImportMenuState : AccessibleMenuState
{
	private List<WorldFileData> _worlds = [];

	internal AccessibleWorkshopWorldImportMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Language.GetTextValue("Workshop.HubWorlds");

	public override void OnActivate()
	{
		_worlds = [];
		if (SocialAPI.Workshop is not null)
		{
			foreach (string path in SocialAPI.Workshop.GetListOfSubscribedWorldPaths())
			{
				_worlds.Add(WorldFile.GetAllMetadata(path, cloudSave: false) ?? WorldFileData.FromInvalidWorld(path, cloudSave: false));
			}
		}
		base.OnActivate();
	}

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		foreach (WorldFileData world in _worlds.OrderBy(world => world.Name, StringComparer.CurrentCultureIgnoreCase))
		{
			WorldFileData capturedWorld = world;
			entries.Add(new(
				() => capturedWorld.Name,
				() => AskImportName(capturedWorld),
				description: () => $"{capturedWorld.WorldSizeName}. Press Enter to import this subscribed world.",
				enabled: () => capturedWorld.IsValid));
		}
	}

	private void AskImportName(WorldFileData world)
	{
		Controller.Navigate(new AccessibleTextInputState(
			Controller,
			Language.GetTextValue("Workshop.EnterNewNameForImportedWorld"),
			world.Name,
			27,
			name => Import(world, name)));
	}

	private void Import(WorldFileData world, string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			Announce("Enter a name for the imported world.");
			return;
		}
		SocialAPI.Workshop?.ImportDownloadedWorldToLocalSaves(world, null, name);
		Announce($"Imported {name}.");
		Controller.Back();
	}
}

internal sealed class AccessibleModSourcesMenuState : AccessibleMenuState
{
	private static readonly Type? CompileType = typeof(Mod).Assembly.GetType("Terraria.ModLoader.Core.ModCompile");
	private static readonly string SourcePath = CompileType?.GetField("ModSourcePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as string
		?? Path.Combine(Main.SavePath, "ModSources");

	internal AccessibleModSourcesMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Language.GetTextValue("tModLoader.MenuDevelopMods");

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(() => "Open Mod Sources folder", OpenSourceFolder));
		if (!Directory.Exists(SourcePath))
		{
			return;
		}

		foreach (string directory in Directory.GetDirectories(SourcePath).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
		{
			string capturedDirectory = directory;
			entries.Add(new(
				() => Path.GetFileName(capturedDirectory),
				() => Utils.OpenFolder(capturedDirectory),
				description: () => capturedDirectory));
		}
	}

	private static void OpenSourceFolder()
	{
		Directory.CreateDirectory(SourcePath);
		Utils.OpenFolder(SourcePath);
	}
}

internal sealed class AccessibleModPacksMenuState : AccessibleMenuState
{
	private static readonly string PacksPath = Path.Combine(Terraria.ModLoader.ModLoader.ModPath, "ModPacks");
	private static readonly Type? OrganizerType = typeof(Mod).Assembly.GetType("Terraria.ModLoader.Core.ModOrganizer");
	private static readonly MethodInfo? FindMods = OrganizerType?.GetMethod("FindMods", BindingFlags.NonPublic | BindingFlags.Static);

	internal AccessibleModPacksMenuState(AccessibleMenuController controller)
		: base(controller)
	{
	}

	protected override string Title => Language.GetTextValue("tModLoader.ModsModPacks");

	protected override void BuildEntries(List<AccessibleMenuEntry> entries)
	{
		entries.Add(new(() => Language.GetTextValue("tModLoader.OpenModPackFolder"), OpenFolder));
		if (!Directory.Exists(PacksPath))
		{
			return;
		}

		foreach (string file in Directory.GetFiles(PacksPath, "*.json").OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
		{
			string capturedFile = file;
			entries.Add(new(
				() => Path.GetFileNameWithoutExtension(capturedFile),
				() => ConfirmApply(capturedFile),
				description: () => PackDescription(capturedFile) + " Press Enter to apply its enabled-mod list.",
				role: "mod pack"));
		}

		foreach (string directory in Directory.GetDirectories(PacksPath).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
		{
			string enabledFile = Path.Combine(directory, "Mods", "enabled.json");
			if (!File.Exists(enabledFile))
			{
				continue;
			}
			string capturedFile = enabledFile;
			entries.Add(new(
				() => Path.GetFileName(directory),
				() => ConfirmApply(capturedFile),
				description: () => PackDescription(capturedFile) + " Press Enter to apply its enabled-mod list.",
				role: "mod pack"));
		}
	}

	private static void OpenFolder()
	{
		Directory.CreateDirectory(PacksPath);
		Utils.OpenFolder(PacksPath);
	}

	private static string PackDescription(string path)
	{
		try
		{
			string[] mods = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
			return mods.Length == 0 ? "Empty mod pack." : $"Contains {mods.Length} mods: {string.Join(", ", mods)}.";
		}
		catch
		{
			return "This mod pack could not be read.";
		}
	}

	private void ConfirmApply(string path)
	{
		Controller.Navigate(new AccessibleConfirmationMenuState(
			Controller,
			$"Apply {Path.GetFileNameWithoutExtension(path)}?",
			"This changes which installed mods are enabled. Reload mods afterward to apply the changes.",
			() => ApplyPack(path)));
	}

	private void ApplyPack(string path)
	{
		try
		{
			HashSet<string> enabledNames = new(
				JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [],
				StringComparer.OrdinalIgnoreCase);
			if (FindMods?.Invoke(null, [true]) is not IEnumerable installedMods)
			{
				Announce("Installed mods could not be enumerated.");
				return;
			}

			int changed = 0;
			foreach (object mod in installedMods)
			{
				Type type = mod.GetType();
				string name = type.GetProperty("Name")?.GetValue(mod)?.ToString() ?? string.Empty;
				PropertyInfo? enabledProperty = type.GetProperty("Enabled");
				if (enabledProperty is null || string.IsNullOrEmpty(name))
				{
					continue;
				}
				bool wanted = enabledNames.Contains(name);
				bool current = (bool)(enabledProperty.GetValue(mod) ?? false);
				if (current != wanted)
				{
					enabledProperty.SetValue(mod, wanted);
					changed++;
				}
			}

			Announce($"Applied the mod pack enabled list. {changed} installed mod states changed. Reload mods to apply it.");
			Controller.Back();
		}
		catch (Exception exception)
		{
			Announce("The mod pack could not be applied. See the tModLoader client log.");
			ModContent.GetInstance<AriadneMod>().Logger.Error("Could not apply mod pack.", exception);
		}
	}
}
