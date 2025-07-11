using Google.Protobuf;

namespace ProtoMessage.Core;

public delegate byte[] ProtoCompressorDelegate(ReadOnlySpan<byte> a);

public delegate byte[] ProtoDecompressorDelegate(ReadOnlySpan<byte> a);

public class ProtoSerializer
{
	// private readonly ICompressor? _compressor;
	private readonly ProtoCompressorDelegate _compressor;
	private readonly ProtoCompressorDelegate _decompressor;
	// private readonly IEncryptor? _encryptor; // 암호화가 필요하다면 추가

	public ProtoSerializer(ProtoCompressorDelegate compressor, ProtoCompressorDelegate decompressor /*, IEncryptor? encryptor = null */)
	{
		_compressor = compressor;
		_decompressor = decompressor;
		// _encryptor = encryptor;
	}

	/// <summary>
	/// Protobuf 메세지를 직렬화하고, 헤더 태그에 따라 압축/암호화 적용
	/// </summary>
	/// <param name="headerTag">압축/암호화 플래그를 포함하는 헤더 태그</param>
	/// <param name="message">직렬화할 Protobuf 메세지</param>
	/// <returns>직렬화 및 처리된 메세지 데이터</returns>
	public ByteString Serialize(int headerTag, IMessage message)
	{
		if (headerTag == 0) return message.ToByteString();
		
		// 기본 직렬화: IMessage -> byte[]
		var bytes = message.ToByteArray();
		ByteString byteString;
		
		// 압축 로직
		if ((headerTag & ProtoTags.Lz4) != 0)
		{
			var compressedBytes = _compressor.Invoke(bytes);
			byteString = ByteString.CopyFrom(compressedBytes);
		}
		else
		{
			byteString = message.ToByteString();
		}
		
		// TODO: 암호화 로직 (필요하다면)
		if ((headerTag & ProtoTags.Encrypted) != 0 /* && _encryptor != null */)
		{
			// rawBytes = _encryptor.Encrypt(rawBytes);
			// Console.WriteLine("Warning: Encryption not implemented.");
		}

		return byteString;
	}

	/// <summary>
	/// 바이트 데이터를 역직렬화하고, 헤더 태그에 따라 역압축/복호화를 적용
	/// </summary>
	/// <param name="headerTag">압축/암호화 플래그를 포함하는 헤더 태그</param>
	/// <param name="payload">처리할 메세지 데이터</param>
	/// <returns>역직렬화 및 처리된 Protobuf 메세지</returns>
	public ReadOnlySpan<byte> Deserialize(int headerTag, ByteString payload)
	{
		// 아무것도 없을 경우, 바로 파싱해서 리턴
		if (headerTag == 0)
			return payload.Span;

		ReadOnlySpan<byte> bytes;
		
		// TODO: 복호화 로직 (필요하다면)
		if ((headerTag & ProtoTags.Encrypted) != 0)
		{
			// processedBytes = _encryptor.Decrypt(processedBytes);
			// Console.WriteLine("Warning: Decryption not implemented.");
		}
		
		// 압축 해제 로직
		if ((headerTag & ProtoTags.Lz4) != 0)
		{
			bytes = _decompressor.Invoke(payload.Span);
		}
		else
		{
			bytes = payload.Span;
		}
		
		return bytes;
	}
	
	// 제네릭 버전 (편의상)
	public T Deserialize<T>(int headerTag, ByteString dataBytes, MessageParser<T> parser) where T : IMessage<T>
	{
		return (T)Deserialize(headerTag, dataBytes, parser);
	}
}