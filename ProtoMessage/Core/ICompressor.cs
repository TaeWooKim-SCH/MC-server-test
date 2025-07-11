namespace ProtoMessage.Core;

/// <summary>
/// 압축 인터페이스
/// </summary>
public interface ICompressor
{
	byte[] Compress(ReadOnlySpan<byte> data);
	byte[] Decompress(ReadOnlySpan<byte> compressedData);
}