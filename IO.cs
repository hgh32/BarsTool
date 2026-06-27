using System.Buffers.Binary;
using System.Text;

namespace BarsTool;

public class DataReader : IDisposable
{
    public Stream BaseStream { get; }
    public bool IsBigEndian { get; set; }

    public DataReader(Stream stream, bool isBigEndian = false)
    {
        BaseStream = stream;
        IsBigEndian = isBigEndian;
    }

    public byte ReadByte()
    {
        int b = BaseStream.ReadByte();
        if (b == -1) throw new EndOfStreamException();
        return (byte)b;
    }

    public byte[] ReadBytes(int count)
    {
        byte[] buffer = new byte[count];
        int read = BaseStream.Read(buffer, 0, count);
        if (read < count) throw new EndOfStreamException();
        return buffer;
    }

    public short ReadInt16()
    {
        var buffer = ReadBytes(2);
        return IsBigEndian ? BinaryPrimitives.ReadInt16BigEndian(buffer) : BinaryPrimitives.ReadInt16LittleEndian(buffer);
    }

    public ushort ReadUInt16()
    {
        var buffer = ReadBytes(2);
        return IsBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(buffer) : BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    public int ReadInt32()
    {
        var buffer = ReadBytes(4);
        return IsBigEndian ? BinaryPrimitives.ReadInt32BigEndian(buffer) : BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    public uint ReadUInt32()
    {
        var buffer = ReadBytes(4);
        return IsBigEndian ? BinaryPrimitives.ReadUInt32BigEndian(buffer) : BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public float ReadSingle()
    {
        var buffer = ReadBytes(4);
        return IsBigEndian ? BinaryPrimitives.ReadSingleBigEndian(buffer) : BinaryPrimitives.ReadSingleLittleEndian(buffer);
    }

    public void Dispose()
    {
        BaseStream.Dispose();
    }
}

public class DataWriter : IDisposable
{
    public Stream BaseStream { get; }
    public bool IsBigEndian { get; set; }

    public DataWriter(Stream stream, bool isBigEndian = false)
    {
        BaseStream = stream;
        IsBigEndian = isBigEndian;
    }

    public void Write(byte value)
    {
        BaseStream.WriteByte(value);
    }

    public void Write(byte[] buffer)
    {
        BaseStream.Write(buffer, 0, buffer.Length);
    }

    public void Write(byte[] buffer, int offset, int count)
    {
        BaseStream.Write(buffer, offset, count);
    }

    public void Write(short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (IsBigEndian) BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        else BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void Write(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (IsBigEndian) BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        else BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void Write(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (IsBigEndian) BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        else BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void Write(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (IsBigEndian) BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        else BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void Write(float value)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (IsBigEndian) BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        else BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        BaseStream.Write(buffer);
    }

    public void Dispose()
    {
        BaseStream.Dispose();
    }
}
