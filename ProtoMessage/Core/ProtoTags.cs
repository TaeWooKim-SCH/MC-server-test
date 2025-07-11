namespace ProtoMessage.Core;

/// <summary>
/// 압축/암호화 태그 정의
/// - 메세지 헤더 내에서 압축이나 암호화 여부를 나타내는 비트 플래그 정의
/// </summary>
public static class ProtoTags
{
	public const int None = 0;
	public const int Lz4 = 1 << 0; // 비트 0 (0001)
	public const int Encrypted = 1 << 1; // 비트 1 (0010)
	// ... 필요한 다른 태그 추가
}