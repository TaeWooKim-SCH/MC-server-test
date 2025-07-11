namespace ProtoMessage.Core;

public class ProtoMessageType
{
	public static readonly ProtoMessageType None = new(nameof(None), "모름");
	public static readonly ProtoMessageType Request = new(nameof(Request), "요청");
	public static readonly ProtoMessageType Response = new(nameof(Response), "응답");
	public static readonly ProtoMessageType Notify = new(nameof(Notify), "알림");
	public static readonly ProtoMessageType Serializer = new(nameof(Serializer), "직렬화");

	public readonly string Name;
	public readonly string Desc;

	private ProtoMessageType(string name, string desc)
	{
		Name = name;
		Desc = desc;
	}

	public static ProtoMessageType OfMessageName(string messageName)
	{
		if (messageName.EndsWith(Request.Name)) return Request;
		if (messageName.EndsWith(Response.Name)) return Response;
		if (messageName.EndsWith(Notify.Name)) return Notify;
		if (messageName.StartsWith('T')) return Serializer;

		return None;
	}
}