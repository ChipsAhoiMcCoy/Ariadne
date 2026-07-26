#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Chat;
using Terraria.ModLoader;

namespace Ariadne.Ingame.Chat;

/// <summary>
/// Keeps a spoken-text copy of received chat messages. Terraria's chat monitor
/// stores its own messages privately and only exposes drawing, so every message
/// is captured as it is added and given a sequence number that stays stable
/// while newer messages push older ones out of the log.
/// </summary>
internal static class ChatMessageLog
{
	private const int MaxMessages = 500;

	private static readonly List<Entry> Messages = [];
	private static readonly object Gate = new();

	private static bool _hookInstalled;
	private static long _nextSequence;

	private readonly record struct Entry(long Sequence, ChatSpeechParts Parts);

	/// <summary>
	/// The sequence of the most recent message ever recorded, including messages
	/// that have since been evicted or cleared. Never moves backwards, so it is
	/// safe to use as an announcement watermark.
	/// </summary>
	internal static long LastRecordedSequence
	{
		get
		{
			lock (Gate)
			{
				return _nextSequence - 1;
			}
		}
	}

	internal static void Install()
	{
		if (_hookInstalled)
		{
			return;
		}

		// Both IChatMonitor entry points funnel into AddNewMessage, so a single
		// hook covers local messages, mod messages, and server broadcasts.
		MethodInfo? addNewMessage = typeof(RemadeChatMonitor).GetMethod(
			nameof(RemadeChatMonitor.AddNewMessage),
			BindingFlags.Public | BindingFlags.Instance,
			binder: null,
			[
				typeof(string),
				typeof(Color),
				typeof(int),
			],
			modifiers: null);
		if (addNewMessage is null)
		{
			throw new MissingMethodException(
				typeof(RemadeChatMonitor).FullName,
				"AddNewMessage(string, Color, int)");
		}

		MonoModHooks.Add(addNewMessage, (AddNewMessageHook)InterceptAddNewMessage);
		_hookInstalled = true;
	}

	internal static bool TryGetRange(out long oldest, out long newest)
	{
		lock (Gate)
		{
			if (Messages.Count == 0)
			{
				oldest = -1;
				newest = -1;
				return false;
			}

			oldest = Messages[0].Sequence;
			newest = Messages[^1].Sequence;
			return true;
		}
	}

	internal static bool TryGetParts(long sequence, out ChatSpeechParts parts)
	{
		lock (Gate)
		{
			if (Messages.Count == 0)
			{
				parts = default;
				return false;
			}

			int index = (int)(sequence - Messages[0].Sequence);
			if (index < 0 || index >= Messages.Count)
			{
				parts = default;
				return false;
			}

			parts = Messages[index].Parts;
			return true;
		}
	}

	/// <summary>
	/// Appends every message newer than <paramref name="sequence"/> to
	/// <paramref name="destination"/> and returns the new watermark.
	/// </summary>
	internal static long CopyPartsAfter(long sequence, List<ChatSpeechParts> destination)
	{
		lock (Gate)
		{
			foreach (Entry entry in Messages)
			{
				if (entry.Sequence > sequence)
				{
					destination.Add(entry.Parts);
				}
			}

			return _nextSequence - 1;
		}
	}

	internal static void Clear()
	{
		lock (Gate)
		{
			Messages.Clear();
		}
	}

	private static void InterceptAddNewMessage(
		AddNewMessageOrig original,
		RemadeChatMonitor self,
		string text,
		Color color,
		int widthLimitInPixels)
	{
		original(self, text, color, widthLimitInPixels);

		if (Main.dedServ)
		{
			return;
		}

		Record(text);
	}

	private static void Record(string text)
	{
		// Servers can ask clients to process packets on the client loop thread
		// instead of the main thread, so recording stays free of anything that
		// assumes the main thread. Localized wording is applied when spoken.
		ChatSpeechParts parts = ChatSpeechFormatter.Parse(text);
		if (parts.IsEmpty)
		{
			return;
		}

		lock (Gate)
		{
			Messages.Add(new Entry(_nextSequence++, parts));
			while (Messages.Count > MaxMessages)
			{
				Messages.RemoveAt(0);
			}
		}
	}

	private delegate void AddNewMessageOrig(
		RemadeChatMonitor self,
		string text,
		Color color,
		int widthLimitInPixels);

	private delegate void AddNewMessageHook(
		AddNewMessageOrig original,
		RemadeChatMonitor self,
		string text,
		Color color,
		int widthLimitInPixels);
}
