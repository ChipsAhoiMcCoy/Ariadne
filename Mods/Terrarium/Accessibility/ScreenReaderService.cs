#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ModLoader;

namespace Terrarium.Accessibility;

internal sealed class ScreenReaderService : IDisposable
{
	private const string PrismVersion = "0.17.3";
	private const string PrismResourcePath = "Native/Prism/windows-x64/prism.dll";
	private static readonly Regex EllipsisBeforeText = new(@"(?:\.{2,}|…)+(?=\p{L}|\p{N})", RegexOptions.Compiled);
	private static readonly Regex Ellipsis = new(@"(?:\.{2,}|…)+", RegexOptions.Compiled);
	private static readonly Regex SentencePunctuationBeforeComma = new(@"([.!?])\s*,\s*", RegexOptions.Compiled);

	private nint _library;
	private nint _context;
	private nint _backend;
	private Mod? _mod;

	private PrismShutdown? _shutdown;
	private PrismBackendFree? _backendFree;
	private PrismBackendOutput? _backendOutput;
	private PrismBackendStop? _backendStop;
	private PrismErrorString? _errorString;

	internal bool IsAvailable => _backend != 0;

	internal string? BackendName { get; private set; }

	internal string? FailureReason { get; private set; }

	internal void Initialize(Mod mod)
	{
		_mod = mod;

		if (Main.dedServ)
		{
			FailureReason = "Speech is disabled on dedicated servers.";
			return;
		}

		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
		{
			FailureReason = "This build currently includes Prism only for 64-bit Windows.";
			mod.Logger.Warn(FailureReason);
			return;
		}

		try
		{
			string libraryPath = ExtractNativeLibrary(mod);
			_library = NativeLibrary.Load(libraryPath);

			PrismInit initialize = GetExport<PrismInit>("prism_init");
			PrismAcquireBestBackend acquireBestBackend = GetExport<PrismAcquireBestBackend>("prism_registry_acquire_best");
			PrismBackendName backendName = GetExport<PrismBackendName>("prism_backend_name");
			_shutdown = GetExport<PrismShutdown>("prism_shutdown");
			_backendFree = GetExport<PrismBackendFree>("prism_backend_free");
			_backendOutput = GetExport<PrismBackendOutput>("prism_backend_output");
			_backendStop = GetExport<PrismBackendStop>("prism_backend_stop");
			_errorString = GetExport<PrismErrorString>("prism_error_string");

			_context = initialize(0);
			if (_context == 0)
			{
				throw new InvalidOperationException("Prism could not create a context.");
			}

			// Prism tries available backends in priority order. Active screen readers,
			// including NVDA, take precedence over Windows speech synthesis fallbacks.
			_backend = acquireBestBackend(_context);
			if (_backend == 0)
			{
				throw new InvalidOperationException("Prism could not find an available speech backend.");
			}

			BackendName = Marshal.PtrToStringUTF8(backendName(_backend)) ?? "Unknown";
			mod.Logger.Info($"Prism {PrismVersion} initialized with the {BackendName} backend.");
		}
		catch (Exception exception)
		{
			FailureReason = exception.Message;
			mod.Logger.Error("Prism screen-reader initialization failed. Menu speech will be unavailable.", exception);
			DisposeNativeResources();
		}
	}

	internal bool Output(string text, bool interrupt = true)
	{
		string speechText = PrepareForSpeech(text);
		if (_backend == 0 || _backendOutput is null || speechText.Length == 0)
		{
			return false;
		}

		nint utf8Text = Marshal.StringToCoTaskMemUTF8(speechText);
		try
		{
			int result = _backendOutput(_backend, utf8Text, interrupt);
			if (result == 0)
			{
				return true;
			}

			string message = _errorString is null
				? $"Prism error {result}"
				: Marshal.PtrToStringUTF8(_errorString(result)) ?? $"Prism error {result}";
			_mod?.Logger.Warn($"Prism could not output menu text: {message}");
			return false;
		}
		catch (Exception exception)
		{
			_mod?.Logger.Warn($"Prism could not output menu text: {exception.Message}");
			return false;
		}
		finally
		{
			Marshal.FreeCoTaskMem(utf8Text);
		}
	}

	internal static string PrepareForSpeech(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		// Literal ellipses can be announced as "dot dot dot" by screen readers
		// configured to speak punctuation. Preserve their pause without sending the
		// repeated punctuation to any Prism backend.
		string speechText = EllipsisBeforeText.Replace(text, ", ");
		speechText = Ellipsis.Replace(speechText, ".");
		// Semantic metadata sometimes follows a user or game supplied sentence.
		// Avoid sending combinations such as ".," that can expose punctuation names.
		return SentencePunctuationBeforeComma.Replace(speechText, "$1 ").Trim();
	}

	public void Dispose()
	{
		DisposeNativeResources();
		_mod = null;
		GC.SuppressFinalize(this);
	}

	private T GetExport<T>(string name) where T : Delegate
	{
		nint address = NativeLibrary.GetExport(_library, name);
		return Marshal.GetDelegateForFunctionPointer<T>(address);
	}

	private static string ExtractNativeLibrary(Mod mod)
	{
		byte[] packagedLibrary = mod.GetFileBytes(PrismResourcePath);
		string directory = Path.Combine(
			Main.SavePath,
			"Terrarium",
			"Native",
			"Prism",
			PrismVersion,
			"windows-x64");
		string libraryPath = Path.Combine(directory, "prism.dll");

		Directory.CreateDirectory(directory);
		if (File.Exists(libraryPath) && FilesMatch(libraryPath, packagedLibrary))
		{
			return libraryPath;
		}

		string temporaryPath = Path.Combine(directory, $"prism-{Environment.ProcessId}.tmp");
		try
		{
			File.WriteAllBytes(temporaryPath, packagedLibrary);
			File.Move(temporaryPath, libraryPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}

		return libraryPath;
	}

	private static bool FilesMatch(string path, byte[] expected)
	{
		using FileStream existing = File.OpenRead(path);
		if (existing.Length != expected.Length)
		{
			return false;
		}

		byte[] existingHash = SHA256.HashData(existing);
		byte[] expectedHash = SHA256.HashData(expected);
		return CryptographicOperations.FixedTimeEquals(existingHash, expectedHash);
	}

	private void DisposeNativeResources()
	{
		if (_backend != 0)
		{
			_backendStop?.Invoke(_backend);
			_backendFree?.Invoke(_backend);
			_backend = 0;
		}

		if (_context != 0)
		{
			_shutdown?.Invoke(_context);
			_context = 0;
		}

		if (_library != 0)
		{
			NativeLibrary.Free(_library);
			_library = 0;
		}

		_shutdown = null;
		_backendFree = null;
		_backendOutput = null;
		_backendStop = null;
		_errorString = null;
		BackendName = null;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate nint PrismInit(nint config);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate nint PrismAcquireBestBackend(nint context);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void PrismShutdown(nint context);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void PrismBackendFree(nint backend);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate nint PrismBackendName(nint backend);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int PrismBackendOutput(
		nint backend,
		nint text,
		[MarshalAs(UnmanagedType.I1)] bool interrupt);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int PrismBackendStop(nint backend);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate nint PrismErrorString(int error);
}
