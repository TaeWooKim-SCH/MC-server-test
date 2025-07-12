// ProtoMessage.Tests.Console/Program.cs

using System.Net.Sockets;
using Google.Protobuf;
using K4os.Compression.LZ4;
using ProtoMessage.Core; // ProtoPack, ProtoSerializer 등
using ProtoMessages;    // t_common.proto, game_message.proto 에서 생성된 클래스들

namespace MC.Server.Test
{
    public class Program
    {
        private TcpClient _client;
        private NetworkStream _stream = null;
        private bool _isConnected = false;

        public static async Task Main(string[] args)
        {
            Console.WriteLine("Protobuf Test Application Started!");

            var programInstance = new Program();

            // 1. ProtoPack 초기화
            ProtoPack.Initialize(new ProtoSerializer(x => LZ4Pickler.Pickle(x), LZ4Pickler.Unpickle));
            Console.WriteLine("ProtoPack initialized");
            
            // 2. tcp 연결 및 수신 대기
            await programInstance.ConnectToServer("127.0.0.1", 4000);

            // 3. 테스트 코드 작성
            if (programInstance._isConnected)
            {
                try
                {
                    // A. JoinRoomRequest 메시지 생성
                    var originalRequest = new JoinRoomRequest
                    {
                        RoomId = 3,
                        UserId = "a_6252622173856498496",
                    };
                    
                    await programInstance.SendMessageAsync(originalRequest);
                    Console.WriteLine($"\nOriginal JoinRoomRequest: RoomId={originalRequest.RoomId}, UserId={originalRequest.UserId}");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            
            // 무한 루프를 돌면서 키 입력을 대기
            while (true)
            {
                // Console.KeyAvailable을 사용하여 블로킹 없이 키 입력 여부 확인
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true); // true는 키 입력이 콘솔에 표시되지 않도록 함
                    if (key.Key == ConsoleKey.Q)
                    {
                        Console.WriteLine(" 'q' pressed. Exiting...");
                        break; // 'q'가 입력되면 루프 종료
                    }
                }
                await Task.Delay(100); // CPU 사용을 줄이기 위해 잠시 대기
            }
            // --- 무한 루프 대기 로직 끝 ---

            programInstance.Disconnect(); // 종료 시 연결 정리
        }

        private async Task ConnectToServer(string ip, int port)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);
                _stream = _client.GetStream();
                _isConnected = true;
                Console.WriteLine("Connected to server");

                // 메세지 수신 루프 시작
                _ = ReceiveMessage();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        
        private void Disconnect()
        {
            if (_isConnected)
            {
                _isConnected = false;
                _stream?.Close();
                _stream?.Dispose();
                _client?.Close();
                _client?.Dispose();
                Console.WriteLine("Disconnected from server.");
            }
        }

        private async Task ReceiveMessage()
        {
            var networkStream = _client.GetStream();

            while (_client.Connected &&
                !(_client.Client.Poll(1, SelectMode.SelectRead) && _client.Client.Available == 0))
            {
                // 1. GamePacket 수신 (Delimited 방식)
                GamePacket packet;
                try
                {
                    packet = await Task.Run(() => GamePacket.Parser.ParseDelimitedFrom(networkStream));
                    if (packet == null) break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[socket] Failed to parse GamePacket: {ex.Message}");
                    break;
                }

                var payload = packet.Payload;
                if (payload == null) continue;

                // 2. 타입 번호로 메세지 역직렬화
                var descriptor = ProtoPack.Shared.Registry.GetDescriptor(payload.TypeNumber);
                if (descriptor == null)
                {
                    Console.WriteLine($"[socket] Unknown payload type_number: {payload.TypeNumber}");
                    continue;
                }

                IMessage message;
                try
                {
                    message = descriptor.Parser.ParseFrom(payload.Data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[socket] Payload parse error: {ex.Message}");
                    continue;
                }

                // 3. 메세지 타입별로 분기
                switch (message)
                {
                    case JoinRoomResponse joinRoom:
                        Console.WriteLine($"{joinRoom.JackpotProb} joined");
                        break;
                    default:
                        Console.WriteLine($"[socket] Unknown or unhandled message type: {message.GetType().Name}");
                        break;
                }
            }
        }

        public async Task<bool> SendMessageAsync(IMessage iMessage, int id = 0, int header = 0)
        {
            try
            {
                if (!_client.Connected) return false;
                
                // 1. 메세지 타입에 따라 typeNumber 추출:
                // GetTypeNumber 메서드를 삭제하고, ProtoPack.Shared.Registry.GetDescriptorByClrType()를 직접 사용.
                var descriptor = ProtoPack.Shared.Registry.GetDescriptorByClrType(iMessage.GetType());
                if (descriptor == null)
                {
                    // 이 예외는 이 시점에 다시 발생하지 않아야 합니다.
                    // Main 메서드의 초기 디버그 로그에서 이미 등록된 것을 확인했기 때문입니다.
                    throw new InvalidOperationException($"Message type {iMessage.GetType().Name} not registered in registry. (Descriptor not found)");
                }
                var typeNumber = descriptor.Number; // 여기서 typeNumber가 할당됩니다.


                // 2. 패킷 생성
                var payload = ProtoPack.Shared.Pack(header, id, typeNumber, iMessage);
                var packet = new GamePacket
                {
                    Header = header,
                    Payload = payload
                };

                // 3. 직렬화 및 송신
                // var data = packet.ToByteArray();
                var stream = _client.GetStream();
                // await stream.WriteAsync(data, 0, data.Length);
                await Task.Run(() => packet.WriteDelimitedTo(stream)); // _stream은 NetworkStream 인스턴스
                await stream.FlushAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[socket] SendMessage error: {ex.Message}");
                return false;
            }
        }
        
        private int GetTypeNumber(IMessage iMessage)
        {
            // ProtoRegistry 내부 _descriptorsByClrType에 public getter가 있다고 가정
            // (없다면 Reflection 등으로 가져올 수 있음)
            var clrType = iMessage.GetType();
            
            // Registry에 타입이 등록되어 있지 않은 경우 예외 발생
            var typeNumber = ProtoPack.Shared.Registry.GetType()
                .GetMethod("Number", new[] { typeof(Type) })?
                .Invoke(ProtoPack.Shared.Registry, new object[] { clrType });

            if (typeNumber is int num) return num;

            throw new InvalidOperationException($"Type {clrType.Name} not registered in registry.");
        }
    }
}