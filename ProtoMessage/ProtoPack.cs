using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoMessage.PacketRegistry;
using ProtoMessages;

namespace ProtoMessage.Core;

/// <summary>
/// IPacketPayload를 래핑/언래핑하는 클래스
/// </summary>
public class ProtoPack
{
    private static readonly object Lock = new(); // 싱글톤 초기화 락
    private readonly ProtoSerializer _serializer;
    public readonly ProtoRegistry Registry; // 외부에서 Registry에 접근할 수 있도록 공개
    public readonly GamePacketRegistry GameRegistry;
    
    private ProtoPack(ProtoSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        Registry = new ProtoRegistry(TCommonReflection.Descriptor, GameMessageReflection.Descriptor);
        GameRegistry = new GamePacketRegistry(this);
    }

    // Shared 인스턴스는 반드시 Initialize 메서드를 통해 초기화되어야 함
    public static ProtoPack Shared { get; private set; }

    public static void Initialize(ProtoSerializer serializer)
    {
        lock (Lock)
        {
            if (Shared != null) return;

            var shared = new ProtoPack(serializer);
            shared.Validate();
            Shared = shared;
        }
    }

    private void Validate()
    {
        // 기본 체크
        var messageDescriptors = new List<FileDescriptor>
        {
            GameMessageReflection.Descriptor
        };
        
        // 메세지 타입이면서 아래와 같이 4개의 접미사가 없을 경우 에러
        var invalidNames = messageDescriptors
            .SelectMany(x => x.MessageTypes)
            .Select(x => x.Name)
            .Where(x => !x.EndsWith("Request"))
            .Where(x => !x.EndsWith("Response"))
            .Where(x => !x.EndsWith("Notify"))
            .Where(x => !x.EndsWith("Packet"))
            .Where(x => !x.EndsWith("_Deprecated"))
            .ToList();
        
        if (0 < invalidNames.Count) throw new ArgumentException($"message name is invalid - {string.Join(", ", invalidNames)}");
        
        // response 이면서 status 필드가 없을 경우
        invalidNames = messageDescriptors
            .SelectMany(x => x.MessageTypes)
            .Where(x => x.Name.EndsWith("Response"))
            .Where(x => x.FindFieldByName("status") == null)
            .Select(x => x.Name)
            .ToList();
        
        if (0 < invalidNames.Count)
            throw new ArgumentException($"status field is not found in proto - {string.Join(", ", invalidNames)}");
        
        // common은 이후 검증에만 포함되도록
        messageDescriptors.Add(TCommonReflection.Descriptor);
        
        // 대문자가 들어간 필드 검색
        invalidNames = messageDescriptors
            .SelectMany(x => x.MessageTypes)
            .SelectMany(x => x.Fields.InDeclarationOrder())
            .Where(x => x.Name.Any(char.IsUpper))
            .Select(x => x.Name)
            .ToList();
        
        if (0 < invalidNames.Count)
            // 필드의 이름에 대문자를 넣지 못한다
            throw new ArgumentException($"upper case field in proto - {string.Join(", ", invalidNames)}");

        invalidNames.Clear();
    }

    public TPacketPayload Pack<T>(int id, T message) where T : IMessage<T>
    {
        var number = Registry.Number<T>();
        return Pack(0, id, number, message);
    }

    public TPacketPayload Pack(int header, int id, int typeNumber, IMessage message)
    {
        var byteString = _serializer.Serialize(header, message);
        var payload = new TPacketPayload
        {
            TypeNumber = typeNumber,
            Data = byteString,
        };

        if (0 < id) payload.Id = id;

        return payload;
    }

    // 네트워크 패킷 핸들러처럼 다양한 타입의 메시지가 들어올 수 있고, 각 메시지의 TypeNumber를 기반으로만 메시지 타입을 식별
    public IMessage Unpack(int header, TPacketPayload payload)
    {
        var descriptor = Registry.GetDescriptor(payload.TypeNumber);
        if (descriptor == null) return null;

        var source = _serializer.Deserialize(header, payload.Data);
        return descriptor.Parser.ParseFrom(source);
    }
    
    // 특정 메시지 타입만 처리하는 로직에서, 언팩하려는 메시지 타입을 명확히 알고 있을 때
    public T Unpack<T>(int header, TPacketPayload payload) where T : IMessage<T>
    {
        var parser = Registry.Parser<T>();
        if (parser == null) return default;

        var source = _serializer.Deserialize(header, payload.Data);
        return parser.ParseFrom(source);
    }

    public T Empty<T>() where T : IMessage<T>, new()
    {
        return ProtoEmpty<T>.Empty;
    }
}