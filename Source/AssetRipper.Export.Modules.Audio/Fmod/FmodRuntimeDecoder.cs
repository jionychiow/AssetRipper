using System;
using System.Runtime.InteropServices;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.Modules.Audio.Fmod;

public static class FmodRuntimeDecoder
{
	public static byte[]? DecodeToWav(byte[] rawAudioData)
	{
		if (rawAudioData is null || rawAudioData.Length == 0)
		{
			return null;
		}

		FmodSystem? system = null;
		FmodSound? sound = null;
		FmodSound? subSound = null;
		bool locked = false;
		IntPtr ptr1 = IntPtr.Zero, ptr2 = IntPtr.Zero;
		uint len1 = 0, len2 = 0;

		try
		{
			int structSize = Marshal.SizeOf<CREATESOUNDEXINFO>();

			RESULT result = FmodFactory.System_Create(out system);
			if (result != RESULT.OK)
			{
				Logger.Log(LogType.Error, LogCategory.Export, $"FMOD System_Create failed: {result}");
				return null;
			}

			if (system is null)
			{
				Logger.Log(LogType.Error, LogCategory.Export, "FMOD System_Create returned OK but system is null");
				return null;
			}

			result = system.Init(1, INITFLAGS.NORMAL, IntPtr.Zero);
			if (result != RESULT.OK)
			{
				Logger.Log(LogType.Error, LogCategory.Export, $"FMOD Init failed: {result}");
				return null;
			}

			CREATESOUNDEXINFO exinfo = default;
			exinfo.cbsize = structSize;
			exinfo.length = (uint)rawAudioData.Length;

			result = system.CreateSound(rawAudioData, MODE.OPENMEMORY, ref exinfo, out sound);
			if (result == RESULT.OK && sound is not null)
			{
				return DecodeSound(sound, subSound, ref locked, ref ptr1, ref ptr2, ref len1, ref len2, rawAudioData.Length);
			}

			sound?.Dispose();
			sound = null;

			SOUND_TYPE[] typesToTry = [SOUND_TYPE.OGGVORBIS, SOUND_TYPE.FADPCM, SOUND_TYPE.VORBIS, SOUND_TYPE.MPEG, SOUND_TYPE.RAW, SOUND_TYPE.FSB];
			foreach (SOUND_TYPE suggestedType in typesToTry)
			{
				exinfo = default;
				exinfo.cbsize = structSize;
				exinfo.length = (uint)rawAudioData.Length;
				exinfo.suggestedsoundtype = suggestedType;

				result = system.CreateSound(rawAudioData, MODE.OPENMEMORY, ref exinfo, out sound);
				if (result == RESULT.OK && sound is not null)
				{
					return DecodeSound(sound, subSound, ref locked, ref ptr1, ref ptr2, ref len1, ref len2, rawAudioData.Length);
				}
				sound?.Dispose();
				sound = null;
			}

			return null;
		}
		catch (DllNotFoundException ex)
		{
			Logger.Log(LogType.Error, LogCategory.Export, $"FMOD DLL load failed: {FmodDllSelector.GetDllName()}, error={ex.Message}");
			return null;
		}
		catch (Exception ex)
		{
			Logger.Log(LogType.Error, LogCategory.Export, $"FMOD decode failed: {ex.GetType().Name}: {ex.Message}");
			return null;
		}
		finally
		{
			if (locked && subSound is not null)
			{
				try { subSound.Unlock(ptr1, ptr2, len1, len2); } catch { }
			}
			subSound?.Dispose();
			sound?.Dispose();
			system?.Dispose();
		}
	}

	private static byte[]? DecodeSound(FmodSound sound, FmodSound? subSound, ref bool locked, ref IntPtr ptr1, ref IntPtr ptr2, ref uint len1, ref uint len2, int inputLength)
	{
		RESULT result = sound.GetSubSound(0, out subSound);
		if (result != RESULT.OK)
		{
			return null;
		}

		result = subSound!.GetFormat(out SOUND_TYPE type, out SOUND_FORMAT format, out int channels, out int bits);
		if (result != RESULT.OK)
		{
			return null;
		}

		result = subSound.GetDefaults(out float sampleRate, out _);
		if (result != RESULT.OK)
		{
			return null;
		}

		result = subSound.GetLength(out uint pcmLen, TIMEUNIT.PCMBYTES);
		if (result != RESULT.OK)
		{
			return null;
		}

		result = subSound.Lock(0, pcmLen, out ptr1, out ptr2, out len1, out len2);
		if (result != RESULT.OK)
		{
			return null;
		}
		locked = true;

		byte[] pcmData = new byte[len1];
		Marshal.Copy(ptr1, pcmData, 0, (int)len1);

		byte[] wavData = WavBuilder.BuildWav(pcmData, channels, (int)sampleRate, bits);

		result = subSound.Unlock(ptr1, ptr2, len1, len2);
		locked = false;

		return wavData;
	}
}
