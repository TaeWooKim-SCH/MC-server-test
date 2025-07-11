using System.Data;
using System.Runtime.CompilerServices;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Force.Crc32;

using ProtoMessage.Core.Primitives;
using ProtoMessage.Extensions;

namespace ProtoMessage.Core;

/// <summary>
/// 메세지 메타 데이터를 저장하는 내부 클래스
/// </summary>
public class ProtoMessageDescriptor
{
	public int Idx { get; } // 내부 인덱스 (배열 접근용)
	public int Number { get; } // proto 파일의 message 이름을 CRC32로 계산된 고유 번호 -> 네트워크 통신 등에서 효율적으로 식별 가능
	public string Name { get; } // proto 파일에 정의된 실제 message 이름
	public Type ClrType { get; } // 이 메세지에 해당하는 C# 클래스(타입) 정보
	public ProtoMessageType ProtoMessageType { get; }
	public MessageParser Parser { get; } // 해당 메세지를 바이트 배열이나 JSON 등에서 파싱(역직렬화)하는 데 사용되는 파서 객체

	public ProtoMessageDescriptor(int idx, int number, MessageDescriptor descriptor)
	{
		Idx = idx;
		Number = number;
		Name = descriptor.Name;
		ClrType = descriptor.ClrType; // Google.Protobuf 3.x에서 추가된 속성
		ProtoMessageType = ProtoMessageType.OfMessageName(descriptor.Name);
		// .NET 8.0의 GetTypeInfo().DeclaredProperties 같은 것이 필요할 수 있지만,
		// Google.Protobuf가 생성하는 Parser는 static 속성이므로 직접 접근
		Parser = descriptor.Parser;
	}
}

/// <summary>
/// 제네릭(TMessage) 기반으로 ProtoMessageDescriptor 객체를 한 번만 캐싱하여 조회 효율성을 높이는 정적 헬퍼 클래스
/// - GetOrCreate 메서드를 통해 특정 메세지 타입에 대한 ProtoMessageDescriptor를 요청하면, CachedDescriptor에 해당 정보 저장
/// - 이후 같은 메세지 타입에 대해 재요청이 들어오면 캐시된 CachedDescriptor를 즉시 반환
///		=> ProtoRegistry를 매번 조회하는 오버헤드를 줄임
/// </summary>
/// <typeparam name="TMessage"></typeparam>
public class ProtoMessageDescriptorStatic<TMessage> where TMessage : IMessage<TMessage>
{
	private static ProtoMessageDescriptorStatic<TMessage> _shared;
	
	public ProtoMessageDescriptor CachedMessageDescriptor { get; private set; }
	public MessageParser<TMessage> CachedMessageParser { get; private set; }

	private ProtoMessageDescriptorStatic(ProtoMessageDescriptor messageDescriptor)
	{
		CachedMessageDescriptor = messageDescriptor;
		CachedMessageParser = CachedMessageDescriptor.Parser as MessageParser<TMessage>;
	}

	public int Idx => CachedMessageDescriptor.Idx;
	public int Number => CachedMessageDescriptor.Number;
	public string Name => CachedMessageDescriptor.Name;
	public MessageParser Parser => CachedMessageDescriptor.Parser;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ProtoMessageDescriptorStatic<TMessage> GetOrCreate(ProtoRegistry protoRegistry)
	{
		if (_shared != null) return _shared;

		var descriptor = protoRegistry.GetDescriptorByClrType(typeof(TMessage));
		_shared = new ProtoMessageDescriptorStatic<TMessage>(descriptor);
		
		return _shared!; // null이 아님을 가정
	}
}

/// <summary>
/// 모든 Protobuf 메세지들의 메타데이터(ProtoMessageDescriptor 객체들)를 관리하는 중앙 레지스트리
/// </summary>
public class ProtoRegistry
{
	// 메세지 디스크럽터들을 효율적으로 조회할 수 있도록 세 가지 형태의 서로 다른 데이터 구조에 저장
	private readonly ProtoMessageDescriptor[] _descriptorByIdx; // 내부 인덱스로 메세지 디스크럽터를 빠르게 조회하기 위한 배열
	private readonly Dictionary<int, ProtoMessageDescriptor> _descriptorsByNumber; // CRC32 번호로 조회
	private readonly Dictionary<Type, ProtoMessageDescriptor> _descriptorsByClrType; // C# Type으로 조회

	/// <summary>
	/// 생성자
	/// </summary>
	/// <param name="fileDescriptors">하나 이상의 FileDescriptor 객체(.proto 파일의 메타데이터)들을 입력받음</param>
	/// <exception cref="DuplicateNameException"></exception>
	/// <exception cref="InvalidOperationException"></exception>
	public ProtoRegistry(params FileDescriptor[] fileDescriptors)
	{
		var registries = fileDescriptors
			.SelectMany(x => x.MessageTypes) // 모든 FileDescriptor에서 MessageType을 가져옴
			.GroupBy(x => ComputeCrc32(x.Name)) // CRC32 값으로 그룹화
			.ToDictionary(x => x.Key, x => x.ToList()); // Key: CRC32, Value: 해당 CRC32를 가진 MessageDescriptor 리스트
		
		// CRC32 해시 충돌 검사
		var duplicatedNames = registries
			.Where(x => x.Value.Count > 1) // 2개 이상의 MessageType이 동일한 CRC32를 가질 경우
			.SelectMany(x => x.Value.Select(y => $"{y.Name}:{x.Key}"))
			.ToList();

		// 같은 CRC32 값을 갖는 메세지 이름이 두 개 이상이면 예외를 발생시켜 충돌 방지 -> CRC32 번호를 메세지의 고유 식별자로 사용하기 위함
		if (duplicatedNames.Count != 0)
		{
			var names = string.Join(", ", duplicatedNames);
			throw new DuplicateNameException($"CRC32 hash collision detected for message names: {names}");
		}
		
		// _descriptorsByNumber 저장소 초기화 -> Number(CRC32)를 기준으로 정렬하고 인덱스를 부여
		_descriptorsByNumber = registries
			.OrderBy(x => x.Key) // 이진 탐색을 위해 번호(CRC32)로 정렬
			.Select((x, idx) => new ProtoMessageDescriptor(idx, x.Key, x.Value[0])) // idx 부여
			.ToDictionary(x => x.Number); // Key: CRC32 Number
		_descriptorsByNumber.TrimExcess();
		
		// _descriptorByIdx 저장소 초기화 -> 인덱스 기반 배열 생성 (빠른 idx 접근용)
		var tempDescriptorByIdx = new ProtoMessageDescriptor[_descriptorsByNumber.Count];
		foreach (var descriptor in _descriptorsByNumber.Values) tempDescriptorByIdx[descriptor.Idx] = descriptor;
		
		_descriptorByIdx = tempDescriptorByIdx;

		// 마지막 인덱스 확인 -> 모든 번호가 순차적으로 할당되었는지 검증
		if (_descriptorByIdx.Length > 0 && _descriptorByIdx[^1].Idx + 1 != _descriptorsByNumber.Count)
		{
			throw new InvalidOperationException($"Packet index overflow or gaps detected: Expected {_descriptorsByNumber.Count}, got {_descriptorByIdx[^1].Idx + 1}");
		}
		
		// _descriptorsByClrType 저장소 초기화 -> C# Type으로 조회하기 위한 딕셔너리
		_descriptorsByClrType = _descriptorsByNumber
			.Values
			.ToDictionary(x => x.ClrType);
		_descriptorsByClrType.TrimExcess();
	}

	/// <summary>
	/// 주어진 문자열(메세지 이름)에 대한 CRC32 해시 값을 계산하는 메서드
	/// </summary>
	/// <param name="name"></param>
	/// <returns></returns>
	public int ComputeCrc32(string name)
	{
		var bytes = Encoding.UTF8.GetBytes(name.Trim());
		var crc32 = (int)Crc32Algorithm.Compute(bytes);
		return crc32;
	}

	/// <summary>
	/// 등록된 총 메세지 개수를 조회하는 메서드
	/// </summary>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetDescriptorCount() => _descriptorByIdx.Length;

	/// <summary>
	/// CRC32 번호로 메세지 디스크립터를 조회하는 메서드
	/// </summary>
	/// <param name="number"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ProtoMessageDescriptor? GetDescriptor(int number) => _descriptorsByNumber.GetValueOrDefault(number);

	/// <summary>
	/// 내부 인덱스로 메세지 디스크럽터를 조회하는 메서드
	/// </summary>
	/// <param name="idx"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ProtoMessageDescriptor GetDescriptorByIdx(int idx) => _descriptorByIdx[idx];
	
	/// <summary>
	/// C# Type으로 메세지 디스크럽터를 조회하는 메서드
	/// </summary>
	/// <param name="clrType"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ProtoMessageDescriptor? GetDescriptorByClrType(Type clrType)
	{
		_descriptorsByClrType.TryGetValue(clrType, out var descriptor);
		return descriptor;
	}

	/// <summary>
	/// 제네릭 Type으로 메세지 디스크럽터를 조회하는 메서드 (Static 헬퍼 사용 -> 캐싱을 통해 성능 최적화)
	/// </summary>
	/// <typeparam name="TMessage"></typeparam>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ProtoMessageDescriptorStatic<TMessage> GetDescriptor<TMessage>() where TMessage : IMessage<TMessage>
	{
		return ProtoMessageDescriptorStatic<TMessage>.GetOrCreate(this);
	}

	/// <summary>
	/// 특정 메세지의 CRC32 번호를 조회하는 메서드
	/// </summary>
	/// <typeparam name="TMessage"></typeparam>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Number<TMessage>() where TMessage : IMessage<TMessage>
	{
		return GetDescriptor<TMessage>().Number;
	}

	/// <summary>
	/// 메세지 인스턴스로 CRC32 번호를 조회하는 메서드
	/// </summary>
	/// <param name="message"></param>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Number<T>(T message) where T : IMessage<T>
	{
		return Number<T>();
	}

	/// <summary>
	/// 특정 메세지 타입의 이름을 조회하는 메서드
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public string Name<T>() where T : IMessage<T>
	{
		var descriptor = GetDescriptor<T>();
		return descriptor?.Name ?? "n/a";
	}

	/// <summary>
	/// 특정 메세지 타입의 MessageParser를 조회하는 메서드
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public MessageParser<T>? Parser<T>() where T : IMessage<T>
	{
		var descriptor = GetDescriptor<T>();
		return descriptor?.CachedMessageParser;
	}

	/// <summary>
	/// JSON 문자열을 특정 Protobuf 메세지 타입으로 파싱하는 메서드
	/// </summary>
	/// <param name="json"></param>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T ParseJson<T>(string json) where T : IMessage<T>, new()
	{
		return json.ToProto<T>();
	}

	/// <summary>
	/// 바이트 데이터(Protobuf 이진 데이터)를 특정 Protobuf 메세지 타입으로 파싱하는 메서드 -> ReadOnlySpan을 이용해 메모리 효율성 증가
	/// </summary>
	/// <param name="bytes"></param>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T ParseFrom<T>(ReadOnlySpan<byte> bytes) where T : IMessage<T>
	{
		var descriptor = GetDescriptor<T>();
		
		if (descriptor == null) return default;
		
		return descriptor.CachedMessageParser.ParseFrom(bytes);
	}

	/// <summary>
	/// 모든 메세지 디스크럽터 객체들을 리스트 형태로 반환하는 메서드
	/// </summary>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public List<ProtoMessageDescriptor> ToMessageDescriptors()
	{
		return new List<ProtoMessageDescriptor>(_descriptorByIdx);
	}
	
	/// <summary>
	/// 모든 메세지 디스크럽터 객체들을 IEnumerable 형태로 반환하는 메서드
	/// </summary>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public IEnumerable<ProtoMessageDescriptor> AsMessageDescriptors()
	{
		return _descriptorByIdx;
	}
	
	// (선택 사항) ProtoPack으로 메시지 팩킹 (여기에 넣을지 ProtoPack에만 둘지 결정)
	// 현재 ProtoPack에 이 기능이 있으므로 제거하거나, ProtoPack에서 ProtoRegistry를 의존하도록 변경 권장
	public ProtoPayload Pack<TMessage>(int id, TMessage message) where TMessage : IMessage<TMessage>
	{
	    var typeNumber = Number<TMessage>();
	    return new ProtoPayload(id, typeNumber, message); 
	}

	public ProtoPayload Pack(int id, int typeNumber, IMessage message)
	{
	    return new ProtoPayload(id, typeNumber, message);
	}
}