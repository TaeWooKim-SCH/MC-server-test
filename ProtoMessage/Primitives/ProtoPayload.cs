using System.Buffers.Binary;
using System.Text;
using Google.Protobuf;

using ProtoMessage.Extensions;

namespace ProtoMessage.Core.Primitives;

public class ProtoPayload
{
	public const int HeaderSize = sizeof(int) + sizeof(int) + sizeof(int); // length(4) + id(4) + typeNumber(4)
	
	public byte[] CachedBytes;
	public int CachedSize;
	
	public int Id; // 메세지 고유 식별자
	public int TypeNumber; // Protobuf 메세지의 타입을 나타내는 고유 번호(CRC32) -> 수신 측에서 어떤 종류의 Protobuf 메세지인지 빠르게 식별 가능
	public IMessage Message; // 실제 전송될 Protobuf 메세지 인스턴스 -> 메세지가 null인 경우는 파싱에 실패한 경우 밖에 없어야 함

	public ProtoPayload()
	{
	}

	public ProtoPayload(int id, int typeNumber, IMessage message)
	{
		Id = id;
		TypeNumber = typeNumber;
		Message = message;
	}

	/// <summary>
	/// 정적 팩토리 메서드로, 바이트 배열로부터 ProtoPayload 객체를 역직렬화하여 생성하는 메서드
	/// </summary>
	/// <param name="registry"></param>
	/// <param name="bytes"></param>
	/// <param name="readSize"></param>
	/// <returns></returns>
	public static ProtoPayload ParseFrom(ProtoRegistry registry, ReadOnlySpan<byte> bytes, out int readSize)
	{
		var payload = new ProtoPayload();
		readSize = payload.MergeFrom(registry, bytes);
		return payload;
	}

	/// <summary>
	/// Id, TypeNumber, Protobuf 메세지의 크기를 모두 합한 전체 페이로드의 크기를 계산하는 메서드
	/// </summary>
	/// <returns></returns>
	public int CalculateSize()
	{
		if (TypeNumber == 0 || Message == null) return 0;

		var messageSize = Message?.CalculateSize() ?? 0;

		return HeaderSize + messageSize;
	}

	/// <summary>
	/// 캐시 무효화 메서드 -> 새로운 직렬화가 필요할 때 사용됨
	/// </summary>
	public void InvalidateCache()
	{
		CachedSize = 0;
		CachedBytes = null;
	}

	/// <summary>
	/// 한 번 직렬화된 바이트 배열과 계산된 크기를 캐싱하는 메서드
	/// -> 캐시된 크기가 유효하면 그대로 반환하고 아니면 다시 계산 후 캐싱
	/// </summary>
	/// <returns></returns>
	public int CachedCalculateSize()
	{
		if (CachedSize >= 0) CachedSize = CalculateSize();

		return CachedSize;
	}

	/// <summary>
	/// 캐시된 바이트 배열을 반환하는 메서드
	/// -> 캐시되지 않았다면 ToByteArray를 이용해 생성 후 캐싱
	/// </summary>
	/// <returns></returns>
	public byte[] GetCachedBytes()
	{
		if (CachedBytes == null && Message != null)
			lock (Message)
			{
				if (CachedBytes == null) CachedBytes = ToByteArray();
			}

		return CachedBytes;
	}

	/// <summary>
	/// 주어진 바이트 배열로부터 헤더 정보(길이, ID, 타입 번호)를 읽고,
	/// ProtoRegistry를 사용해 TypeNumber에 해당하는 Protobuf 메세지 타입을 찾아 실제 메세지 바디를 역직렬화하는 메서드
	/// -> 바이트 순서와 버퍼 길이에 대한 유효성 검사를 수행
	/// </summary>
	/// <param name="registry"></param>
	/// <param name="bytes"></param>
	/// <returns></returns>
	/// <exception cref="ArgumentOutOfRangeException"></exception>
	public int MergeFrom(ProtoRegistry registry, ReadOnlySpan<byte> bytes)
	{
		var readIndex = 0;
		
		// 버퍼의 길이가 페이로드 헤더의 길이보다 짧을 경우 예외 발생
		if (bytes.Length < HeaderSize) throw new ArgumentOutOfRangeException($"buffer size is smaller than the proto payload header size - {bytes.Length}");
		
		// length
		var readPayloadSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(readIndex));
		readIndex += 4;
		
		// 읽어야 할 페이로드 사이즈가 헤더 사이즈보다 작을 경우
		if (readPayloadSize < HeaderSize) throw new ArgumentOutOfRangeException($"proto payload length - {readPayloadSize}");
		
		// id
		var readId = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(readIndex));
		readIndex += 4;
		
		// type number
		var readTypeNumber = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(readIndex));
		readIndex += 4;

		var readBodyLength = readPayloadSize - readIndex;
		var messageByteLength = bytes.Length - readIndex;
		
		// 남아 있는 버퍼의 길이가 읽어야 할 길이보다 짧을 경우 에러
		if (readBodyLength > messageByteLength) throw new ArgumentOutOfRangeException($"buffer size is smaller than the proto payload size to be read - read({readPayloadSize}) buffer({messageByteLength})");

		var messageBytes = readBodyLength > 0 ? bytes.Slice(readIndex, readBodyLength) : Array.Empty<byte>(); // 공백이라도 넣어줘야 함

		readIndex += readBodyLength;
		
		// 설정
		Id = readId;
		TypeNumber = readTypeNumber;
		
		// 못찾았을 때, 디코드는 안해도 타입 넘버는 전파해야 함
		IMessage decoded = null;
		var desc = registry.GetDescriptor(readTypeNumber);
		if (desc != null)
			try
			{
				decoded = desc.Parser.ParseFrom(messageBytes);
			}
			catch (Exception e)
			{
				// 익셉션일 경우 null 유지
			}

		Message = decoded;

		return readIndex;
	}

	/// <summary>
	/// Id, TypeNumber, Protobuf 메세지를 포함하는 전체 페이로드를 바이트 배열로 직렬화 하는 메서드
	/// -> BinaryPrimitives.WriteUInt32LittleEndian를 사용해 헤더 정보를 little-endian 바이트 순서로 기록
	/// </summary>
	/// <param name="bytes"></param>
	/// <returns></returns>
	public int WriteTo(Span<byte> bytes)
	{
		var calculatedSize = CachedCalculateSize();
		CachedSize = calculatedSize;

		var writeIndex = 0;
		
		// length
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(writeIndex, 4), (uint)calculatedSize);
		writeIndex += 4;
		
		// id
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(writeIndex, 4), (uint)Id);
		writeIndex += 4;
		
		// type number
		BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(writeIndex, 4), (uint)TypeNumber);
		writeIndex += 4;

		if (Message != null)
		{
			var length = CachedSize - HeaderSize;
			var messageBytes = bytes.Slice(writeIndex, length);
			Message.WriteTo(messageBytes);
			writeIndex += length;
		}

		return writeIndex;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Id, TypeNumber, Message);
	}

	public bool Equals(ProtoPayload other)
	{
		if (other == null) return false;
		if (other.Id != Id) return false;
		if (other.TypeNumber != TypeNumber) return false;
		if (other.Message == null && Message == null) return true;

		return Message?.Equals(other.Message) ?? false;
	}

	public override bool Equals(object other)
	{
		return Equals(other as ProtoPayload);
	}

	/// <summary>
	/// 페이로드를 바이트 배열로 변환하여 반환하는 메서드
	/// </summary>
	/// <returns></returns>
	public byte[] ToByteArray()
	{
		var size = CachedCalculateSize();
		if (size <= 0) return Array.Empty<byte>();

		var bytes = new byte[size];
		Span<byte> buffers = bytes;
		WriteTo(buffers);
		
		return bytes;
	}

	/// <summary>
	/// 페이로드의 내용을 JSON 형식의 문자열로 반환하는 메서드 -> 디버깅이나 로깅에 유용
	/// </summary>
	/// <returns></returns>
	public string ToJson()
	{
		var builder = new StringBuilder();
		builder.Append('{');
		builder.Append($"\"Id\": {Id},");
		builder.Append($"\"TypeNumber\": {TypeNumber},");
		builder.Append($"\"Body\": {(Message != null ? Message.ToJson() : "{}")}");
		builder.Append('}');
		
		return builder.ToString();
	}
}
