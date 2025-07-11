using Google.Protobuf;

namespace ProtoMessage.Core;

internal static class ProtoEmpty<T> where T : IMessage<T>, new()
{
	public const string StatusFieldName = "status";
	public static readonly T Empty;

	static ProtoEmpty()
	{
		var empty = new T();
		
		// status 필드는 -1로 셋팅
		var statusField = empty.Descriptor.FindFieldByName(StatusFieldName);
		statusField?.Accessor.SetValue(empty, -1);
		
		// 접미사가 Response로 끝나는데 status 필드가 없는 경우 예외 발생
		if (statusField == null && empty.Descriptor.Name.EndsWith("Response"))
			throw new ArgumentException($"status field is not found in proto {empty.Descriptor.Name} message");

		Empty = empty;
	}
}