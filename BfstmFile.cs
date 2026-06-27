using System.Buffers.Binary;
using System.Text;
using VGAudio.Containers;
using VGAudio.Containers.NintendoWare;
using VGAudio.Containers.Wave;

namespace BarsTool;

public record BfstmInfo(
    int SampleRate,
    int SampleCount,
    int ChannelCount,
    bool IsLooped,
    int LoopStart,
    byte Codec,
    int SampleBlockCount,
    int SampleBlockSize,
    int SampleBlockSampleCount,
    int LastBlockSize,
    int LastBlockSampleCount,
    int LastBlockPadSize,
    int SeekSize,
    int SeekIntervalSampleCount
);

public static class BfstmFile
{
    private const int FILE_HEADER_SIZE = 0x40;
    private const int PDAT_HEADER_SIZE = 0x40;
    private const int PREFETCH_BLOCKS_PER_CHANNEL = 5;

    public static BfstmInfo ReadInfo(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new DataReader(ms);

        string magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic != "FSTM" && magic != "FSTP")
            throw new InvalidDataException($"Not a BFSTM/BFSTP file (got '{magic}').");

        ushort bom = reader.ReadUInt16();
        if (bom == 0xFFFE)
            reader.IsBigEndian = true;
        else if (bom != 0xFEFF)
            throw new InvalidDataException($"Unexpected BOM: 0x{bom:X4}");

        reader.BaseStream.Position = 16;
        int numBlocks = reader.ReadUInt16();

        int infoOff = -1;
        reader.BaseStream.Position = 20;
        for (int i = 0; i < numBlocks; i++)
        {
            ushort blkType = reader.ReadUInt16();
            reader.ReadUInt16(); // padding
            int blkOff = reader.ReadInt32();
            if (blkType == 0x4000) infoOff = blkOff;
            reader.ReadInt32(); // size
        }

        if (infoOff < 0) throw new InvalidDataException("INFO block not found.");

        reader.BaseStream.Position = infoOff + 8 + 4;
        int stmInfoRefOff = reader.ReadInt32();
        
        long si = infoOff + 8 + stmInfoRefOff;
        reader.BaseStream.Position = si;

        byte codec = reader.ReadByte();
        bool isLooped = reader.ReadByte() != 0;
        int channelCount = reader.ReadByte();
        reader.ReadByte(); // padding
        int sampleRate = reader.ReadInt32();
        int loopStart = reader.ReadInt32();
        int sampleCount = reader.ReadInt32();
        int sampleBlockCount = reader.ReadInt32();
        int sampleBlockSize = reader.ReadInt32();
        int sampleBlockSampleCount = reader.ReadInt32();
        int lastBlockSize = reader.ReadInt32();
        int lastBlockSampleCount = reader.ReadInt32();
        int lastBlockPadSize = reader.ReadInt32();
        int seekSize = reader.ReadInt32();
        int seekIntervalSampleCount = reader.ReadInt32();

        return new BfstmInfo(
            SampleRate: sampleRate,
            SampleCount: sampleCount,
            ChannelCount: channelCount,
            IsLooped: isLooped,
            LoopStart: loopStart,
            Codec: codec,
            SampleBlockCount: sampleBlockCount,
            SampleBlockSize: sampleBlockSize,
            SampleBlockSampleCount: sampleBlockSampleCount,
            LastBlockSize: lastBlockSize,
            LastBlockSampleCount: lastBlockSampleCount,
            LastBlockPadSize: lastBlockPadSize,
            SeekSize: seekSize,
            SeekIntervalSampleCount: seekIntervalSampleCount
        );
    }

    public static byte[] ConvertToWav(byte[] bfstmData)
    {
        var reader = new BCFstmReader();
        using var ms = new MemoryStream(bfstmData);
        var audioData = reader.Read(ms);
        var wavWriter = new WaveWriter();
        using var outStream = new MemoryStream();
        wavWriter.WriteToStream(audioData, outStream);
        return outStream.ToArray();
    }

    public static byte[] ConvertFromWav(byte[] wavData, bool loop = false, int loopStart = 0, int loopEnd = 0, bool isBigEndian = false)
    {
        var wavReader = new WaveReader();
        using var ms = new MemoryStream(wavData);
        var audioData = wavReader.Read(ms);

        var writer = new BCFstmWriter(NwTarget.Cafe);
        writer.Configuration.Endianness = isBigEndian ? VGAudio.Utilities.Endianness.BigEndian : VGAudio.Utilities.Endianness.LittleEndian;
        writer.Configuration.Version = new NwVersion(5, 0, 0, 0);
        writer.Configuration.SamplesPerSeekTableEntry = 0x3800;
        writer.Configuration.SamplesPerInterleave = 0x3800;

        using var outStream = new MemoryStream();
        if (loop && loopEnd > 0)
        {
            var format = audioData.GetAllFormats().First();
            writer.WriteToStream(format.WithLoop(true, loopStart, loopEnd), outStream);
        }
        else
        {
            writer.WriteToStream(audioData, outStream);
        }
        return outStream.ToArray();
    }

    public static byte[] RoundtripBfstm(byte[] bfstmData)
    {
        var reader = new BCFstmReader();
        using var readMs = new MemoryStream(bfstmData);
        AudioWithConfig awc = reader.ReadWithConfig(readMs);

        bool isBigEndian = BitConverter.ToUInt16(bfstmData, 4) == 0xFFFE;
        var writer = new BCFstmWriter(NwTarget.Cafe);
        writer.Configuration.Endianness = isBigEndian ? VGAudio.Utilities.Endianness.BigEndian : VGAudio.Utilities.Endianness.LittleEndian;
        
        writer.Configuration.Version = new NwVersion(5, 0, 0, 0);
        writer.Configuration.SamplesPerSeekTableEntry = 0x3800;
        writer.Configuration.SamplesPerInterleave = 0x3800;
        writer.Configuration.RecalculateSeekTable = false;

        using var outMs = new MemoryStream();
        writer.WriteToStream(awc.AudioFormat, outMs, awc.Configuration);
        return outMs.ToArray();
    }

    public static byte[] GenerateBfstp(byte[] bfstmData)
    {
        string magic = Encoding.ASCII.GetString(bfstmData, 0, 4);
        if (magic != "FSTM")
            throw new InvalidDataException("Input must be a BFSTM file.");

        int numBlocks = BitConverter.ToUInt16(bfstmData, 16);
        // We probably should read the BOM to determine if it's BigEndian for GenerateBfstp too.
        bool isBigEndian = BitConverter.ToUInt16(bfstmData, 4) == 0xFFFE;
        
        ushort ReadUInt16(int offset) => isBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bfstmData.AsSpan(offset)) : BinaryPrimitives.ReadUInt16LittleEndian(bfstmData.AsSpan(offset));
        int ReadInt32(int offset) => isBigEndian ? BinaryPrimitives.ReadInt32BigEndian(bfstmData.AsSpan(offset)) : BinaryPrimitives.ReadInt32LittleEndian(bfstmData.AsSpan(offset));

        numBlocks = ReadUInt16(16);

        int infoOff = -1, infoSize = -1;
        int dataOff = -1;
        int pos = 20;
        for (int i = 0; i < numBlocks; i++)
        {
            ushort blkType = ReadUInt16(pos);
            int blkOff = ReadInt32(pos + 4);
            int blkSize = ReadInt32(pos + 8);
            if (blkType == 0x4000) { infoOff = blkOff; infoSize = blkSize; }
            if (blkType == 0x4002) { dataOff = blkOff; }
            pos += 12;
        }

        if (infoOff < 0 || dataOff < 0)
            throw new InvalidDataException("BFSTM missing INFO or DATA block.");

        byte[] infoBlock = new byte[infoSize];
        Array.Copy(bfstmData, infoOff, infoBlock, 0, infoSize);

        int stmInfoRefOff = isBigEndian ? BinaryPrimitives.ReadInt32BigEndian(infoBlock.AsSpan(8 + 4)) : BinaryPrimitives.ReadInt32LittleEndian(infoBlock.AsSpan(8 + 4));
        int stmInfoPos = 8 + stmInfoRefOff;
        int channels = infoBlock[stmInfoPos + 2];
        int sampleBlkSize = isBigEndian ? BinaryPrimitives.ReadInt32BigEndian(infoBlock.AsSpan(stmInfoPos + 20)) : BinaryPrimitives.ReadInt32LittleEndian(infoBlock.AsSpan(stmInfoPos + 20));

        int dataAudioStart = dataOff + 0x20;
        int prefetchDataSize = PREFETCH_BLOCKS_PER_CHANNEL * channels * sampleBlkSize;

        int pdatBlockSize = PDAT_HEADER_SIZE + prefetchDataSize;
        int pdatOff = FILE_HEADER_SIZE + infoSize;
        int fileSize = pdatOff + pdatBlockSize;

        int sampleDataRefFieldOff = stmInfoPos + 48 + 4;
        byte[] modifiedInfo = (byte[])infoBlock.Clone();
        int newSdRefValue = pdatOff + PDAT_HEADER_SIZE;
        if (isBigEndian) BinaryPrimitives.WriteInt32BigEndian(modifiedInfo.AsSpan(sampleDataRefFieldOff), newSdRefValue);
        else BinaryPrimitives.WriteInt32LittleEndian(modifiedInfo.AsSpan(sampleDataRefFieldOff), newSdRefValue);

        using var ms = new MemoryStream(fileSize);
        using var w = new DataWriter(ms, isBigEndian);

        w.Write(Encoding.ASCII.GetBytes("FSTP"));
        w.Write((ushort)(isBigEndian ? 0xFEFF : 0xFEFF)); // In EndianWriter, Write(ushort 0xFEFF) will correctly output FE FF if big endian and FF FE if little endian
        w.Write((ushort)FILE_HEADER_SIZE);
        w.Write(0x00020100u); // version
        w.Write(fileSize);
        w.Write((ushort)2); // 2 blocks
        w.Write((ushort)0);

        w.Write((ushort)0x4000); w.Write((ushort)0);
        w.Write(FILE_HEADER_SIZE); w.Write(infoSize);

        w.Write((ushort)0x4004); w.Write((ushort)0);
        w.Write(pdatOff); w.Write(pdatBlockSize);

        while (ms.Position < FILE_HEADER_SIZE) w.Write((byte)0);

        w.Write(modifiedInfo);

        // PDAT block
        w.Write(Encoding.ASCII.GetBytes("PDAT"));
        w.Write(pdatBlockSize);
        w.Write(isBigEndian ? 0 : 1); // flag: 1 = Switch LE
        w.Write(0);
        w.Write(prefetchDataSize);
        w.Write(0);
        w.Write(0);
        w.Write(0x34); // data start offset
        while (ms.Position < pdatOff + PDAT_HEADER_SIZE) w.Write((byte)0);

        w.Write(bfstmData, dataAudioStart, prefetchDataSize);

        return ms.ToArray();
    }

    public static (float Loudness, float Peak) ComputeAudioMetrics(byte[] bfstmData)
    {
        byte[] wavData = ConvertToWav(bfstmData);

        using var ms = new MemoryStream(wavData);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        reader.ReadBytes(4); // RIFF
        reader.ReadInt32();
        reader.ReadBytes(4); // WAVE

        int channels = 0, bitsPerSample = 0;
        while (ms.Position < ms.Length)
        {
            string chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            int chunkSize = reader.ReadInt32();
            if (chunkId == "fmt ")
            {
                reader.ReadInt16();
                channels = reader.ReadInt16();
                reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
                if (chunkSize > 16) reader.ReadBytes(chunkSize - 16);
            }
            else if (chunkId == "data")
            {
                if (bitsPerSample != 16 || chunkSize == 0) return (0f, 0f);
                int totalSamples = chunkSize / 2;
                double sumSquares = 0;
                int maxAbs = 0;
                for (int i = 0; i < totalSamples; i++)
                {
                    short sample = reader.ReadInt16();
                    int abs = Math.Abs((int)sample);
                    if (abs > maxAbs) maxAbs = abs;
                    sumSquares += sample / 32768.0 * (sample / 32768.0);
                }
                float peak = maxAbs / 32768f;
                double rms = Math.Sqrt(sumSquares / totalSamples);
                float loudness = rms > 0 ? (float)(20 * Math.Log10(rms)) : -100f;
                return (loudness, peak);
            }
            else reader.ReadBytes(chunkSize);
        }
        return (0f, 0f);
    }

    private static int AlignUp(int value, int alignment) =>
        (value + alignment - 1) / alignment * alignment;
}
