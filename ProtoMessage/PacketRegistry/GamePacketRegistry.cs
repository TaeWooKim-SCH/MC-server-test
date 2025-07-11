using Google.Protobuf;
using ProtoMessage.Core;
using ProtoMessages;

namespace ProtoMessage.PacketRegistry;

public class GamePacketRegistry
{
	private readonly ProtoPack _pack;

	public GamePacketRegistry(ProtoPack pack)
	{
		_pack = pack;
	}

	public GamePacket Encode<TMessage>(int header, int id, int typeNumber, TMessage message) where TMessage : IMessage<TMessage>
	{
		var packet = new GamePacket
		{
			Header = header,
			Payload = _pack.Pack(header, id, typeNumber, message)
		};
		return packet;
	}

	public TMessage Decode<TMessage>(GamePacket packet) where TMessage : IMessage<TMessage>
	{
		return _pack.Unpack<TMessage>(packet.Header, packet.Payload);
	}
}