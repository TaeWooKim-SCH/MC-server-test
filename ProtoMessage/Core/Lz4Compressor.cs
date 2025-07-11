namespace ProtoMessage.Core;

public class Lz4Compressor : ICompressor
{
	public byte[] Compress(ReadOnlySpan<byte> data)
	{
		// using var inputStream = new ReadOnlyMemoryStream(data);
		return [];
	}

	public byte[] Decompress(ReadOnlySpan<byte> compressedData)
	{
		return [];
	}
}
internal class ReadOnlyMemoryStream : Stream
{
	private readonly ReadOnlyMemory<byte> _memory;
	private long _position;

	public ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory)
	{
		_memory = memory;
		_position = 0;
	}
	
	public override bool CanRead => true;
	public override bool CanSeek => true;
	public override bool CanWrite => false;
	public override long Length => _memory.Length;

	public override long Position
	{
		get => _position;
		set => _position = value;
	}
	
	public override void Flush() { }

	public override int Read(byte[] buffer, int offset, int count)
	{
		var remaining = (int)(Length - Position);
		var bytesToRead = Math.Min(count, remaining);
		_memory.Span.Slice((int)_position, bytesToRead).CopyTo(buffer.AsSpan(offset));
		return bytesToRead;
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		_position = origin switch
		{
			SeekOrigin.Begin => offset,
			SeekOrigin.Current => Position + offset,
			SeekOrigin.End => Length + offset,
			_ => throw new ArgumentOutOfRangeException(nameof(origin))
		};
		return _position;
	}
	
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
