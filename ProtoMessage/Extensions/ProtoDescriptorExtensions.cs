using Google.Protobuf;
using Google.Protobuf.Collections;

namespace ProtoMessage.Extensions;

public static class ProtoDescriptorExtensions
{
	private static readonly JsonFormatter Formatter;
	private static readonly JsonParser Parser;

	static ProtoDescriptorExtensions()
	{
		Formatter = new JsonFormatter(new JsonFormatter.Settings(true));
		Parser = new JsonParser(new JsonParser.Settings(JsonParser.Settings.Default.RecursionLimit)
			.WithIgnoreUnknownFields(true));
	}

	public static string ToJson<T>(this T data, JsonFormatter formatter = null) where T : IMessage
	{
		var jsonFormatter = formatter ??= Formatter;
		return jsonFormatter.Format(data);
	}

	public static T ToProto<T>(this string json) where T : IMessage<T>, new()
	{
		// TODO: 임시 방편
		try
		{
			return Parser.Parse<T>(json);
		}
		catch (Exception)
		{
			return default;
		}
	}

	public static MapField<TKey, TValue> ToMapField<TKey, TValue>(this IDictionary<TKey, TValue> collection)
	{
		var temp = new MapField<TKey, TValue>();
		temp.Add(collection);
		
		return temp;
	}

	public static MapField<TKey, TValue> ToMapField<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> enumerable)
	{
		var temp = new MapField<TKey, TValue>();
		foreach (var knv in enumerable) temp.Add(knv.Key, knv.Value);

		return temp;
	}
}