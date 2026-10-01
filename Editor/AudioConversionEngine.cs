using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Piece 3 of the Sound Editor feature: real format conversion. WAV
/// reading/writing goes through NAudio's own WaveFileWriter and
/// AudioFileReader rather than anything hand-rolled - NAudio already does
/// this well, and there is no benefit to re-implementing an already-solved
/// problem. Amiga IFF/8SVX is the one format nothing else understands, so
/// AmigaIffCodec (Engine) is what makes it possible in either direction.
/// MP3/OGG/FLAC target export is not built here - NAudio decodes those but
/// does not encode them; that would need a further dependency decision
/// (e.g. an LGPL LAME wrapper for MP3) which hasn't been made yet.
/// </summary>
public static class AudioConversionEngine
{
    public static readonly string[] TargetFormats = { "WAV", "Amiga IFF (8SVX)" };

    public static void ConvertAndExport(string sourcePath, string outputPath, string targetFormat, int targetSampleRate, int targetBitDepth, SoundChannelFormat targetChannels)
    {
        IDisposable? toDispose = null;
        ISampleProvider source;

        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is ".iff" or ".8svx")
        {
            var (samples, sampleRate) = AmigaIffCodec.Read(sourcePath);
            var floatSamples = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++) floatSamples[i] = samples[i] / 128f;
            source = new ArraySampleProvider(floatSamples, sampleRate, 1);
        }
        else
        {
            var reader = new AudioFileReader(sourcePath);
            toDispose = reader;
            source = reader;
        }

        try
        {
            bool wantMono = targetFormat == "Amiga IFF (8SVX)" || targetChannels == SoundChannelFormat.Mono;
            if (wantMono && source.WaveFormat.Channels > 1)
                source = new DownmixToMonoProvider(source);

            if (source.WaveFormat.SampleRate != targetSampleRate)
                source = new WdlResamplingSampleProvider(source, targetSampleRate);

            if (targetFormat == "Amiga IFF (8SVX)")
                WriteAmigaIff(outputPath, source, targetSampleRate);
            else
                WriteWav(outputPath, source, targetBitDepth);
        }
        finally
        {
            toDispose?.Dispose();
        }
    }

    private static void WriteWav(string path, ISampleProvider source, int bitDepth)
    {
        var format = bitDepth switch
        {
            8 => WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, source.WaveFormat.SampleRate, source.WaveFormat.Channels,
                    source.WaveFormat.SampleRate * source.WaveFormat.Channels, source.WaveFormat.Channels, 8),
            24 => WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, source.WaveFormat.SampleRate, source.WaveFormat.Channels,
                    source.WaveFormat.SampleRate * source.WaveFormat.Channels * 3, source.WaveFormat.Channels * 3, 24),
            _ => new WaveFormat(source.WaveFormat.SampleRate, 16, source.WaveFormat.Channels)
        };

        using var writer = new WaveFileWriter(path, format);
        var buffer = new float[4096];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            byte[] bytes = ConvertSamples(buffer, read, bitDepth);
            writer.Write(bytes, 0, bytes.Length);
        }
    }

    private static void WriteAmigaIff(string path, ISampleProvider monoSource, int sampleRate)
    {
        var samples = new List<sbyte>();
        var buffer = new float[4096];
        int read;
        while ((read = monoSource.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                samples.Add((sbyte)Math.Clamp(buffer[i] * 127, -128, 127));
        }
        AmigaIffCodec.Write(path, samples.ToArray(), sampleRate);
    }

    private static byte[] ConvertSamples(float[] samples, int count, int bitDepth)
    {
        switch (bitDepth)
        {
            case 8:
                var b8 = new byte[count];
                for (int i = 0; i < count; i++) b8[i] = (byte)Math.Clamp(samples[i] * 127 + 128, 0, 255);
                return b8;

            case 24:
                var b24 = new byte[count * 3];
                for (int i = 0; i < count; i++)
                {
                    int v = (int)Math.Clamp(samples[i] * 8388607f, -8388608, 8388607);
                    b24[i * 3] = (byte)(v & 0xFF);
                    b24[i * 3 + 1] = (byte)((v >> 8) & 0xFF);
                    b24[i * 3 + 2] = (byte)((v >> 16) & 0xFF);
                }
                return b24;

            default: // 16
                var b16 = new byte[count * 2];
                for (int i = 0; i < count; i++)
                {
                    short v = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
                    b16[i * 2] = (byte)(v & 0xFF);
                    b16[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
                }
                return b16;
        }
    }

    /// <summary>Wraps a plain float array (e.g. decoded from Amiga IFF, which nothing in NAudio can read natively) as an ISampleProvider so it can flow through the same resample/downmix/write pipeline as any NAudio-native source.</summary>
    private sealed class ArraySampleProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _position;
        public WaveFormat WaveFormat { get; }

        public ArraySampleProvider(float[] data, int sampleRate, int channels)
        {
            _data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int available = Math.Min(count, _data.Length - _position);
            if (available <= 0) return 0;
            Array.Copy(_data, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }

    private sealed class DownmixToMonoProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        public WaveFormat WaveFormat { get; }

        public DownmixToMonoProvider(ISampleProvider source)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int channels = _source.WaveFormat.Channels;
            var temp = new float[count * channels];
            int read = _source.Read(temp, 0, count * channels);
            int framesRead = read / channels;
            for (int i = 0; i < framesRead; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += temp[i * channels + c];
                buffer[offset + i] = sum / channels;
            }
            return framesRead;
        }
    }
}
