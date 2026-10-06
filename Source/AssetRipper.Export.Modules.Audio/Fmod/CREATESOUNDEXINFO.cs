using System.Runtime.InteropServices;

namespace AssetRipper.Export.Modules.Audio.Fmod;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CREATESOUNDEXINFO
{
	public int cbsize;
	public uint length;
	public uint fileoffset;
	public int numchannels;
	public int defaultfrequency;
	public SOUND_FORMAT format;
	public uint decodebuffersize;
	public int initialsubsound;
	public int numsubsounds;
	public IntPtr inclusionlist;
	public int inclusionlistnum;
	public IntPtr pcmreadcallback;
	public IntPtr pcmsetposcallback;
	public IntPtr nonblockcallback;
	public IntPtr dlsname;
	public IntPtr encryptionkey;
	public int maxpolyphony;
	public IntPtr userdata;
	public SOUND_TYPE suggestedsoundtype;
	public IntPtr fileuseropen;
	public IntPtr fileuserclose;
	public IntPtr fileuserread;
	public IntPtr fileuserseek;
	public IntPtr fileuserasyncread;
	public IntPtr fileuserasynccancel;
	public IntPtr fileuserdata;
	public int channelorder;
	public int channelmask;
	public IntPtr initialsoundgroup;
	public uint initialseekposition;
	public TIMEUNIT initialseekpostype;
	public int ignoresetfilesystem;
	public uint audioqueuepolicy;
	public uint minmidigranularity;
	public int nonblockthreadid;
}