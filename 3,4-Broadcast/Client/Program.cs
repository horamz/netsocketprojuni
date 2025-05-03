using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Broadcast;

class Client
{
    public readonly string ServerAddress;
    public readonly int Port;
    public bool Running { get; private set; }
    private bool _identified;
    private string _username = "";

    private readonly TcpClient _client;
    private NetworkStream? _netStream;
    private readonly Dictionary<PacketCommand, Func<string, Task>> _commmandHandlers = new();

    private static BlockingCollection<string> consoleQueue = new();
    private int _cursor;

    private static string inputBuffer = string.Empty;

    public Client(string serverAddress, int port)
    {
        ServerAddress = serverAddress;
        Port = port;

        _client = new TcpClient();
    }

    public void Connect()
    {
        try
        {
            _client.Connect(ServerAddress, Port);
        }
        catch (SocketException se)
        {
            Console.WriteLine("Error connecting to server: {0}", se.Message);
        }

        if (_client.Connected)
        {
            Console.WriteLine("Connected to the server at {0}.", _client.Client.RemoteEndPoint);
            Running = true;

            _netStream = _client.GetStream();

            _commmandHandlers[PacketCommand.Name] = _packetHandlerName;
            _commmandHandlers[PacketCommand.Exit] = _packetHandlerExit;
            _commmandHandlers[PacketCommand.Msg] = _packetHandlerMsg;
            _commmandHandlers[PacketCommand.PrivateMsg] = _packetHandlerPrivateMsg;
            _commmandHandlers[PacketCommand.List] = _packetHandlerList;
        }
        else
        {
            _cleanupNetworkResources();
            Console.WriteLine("Wasn't able to connect to the server at {0}:{1}.", ServerAddress, Port);
        }
    }



    private async Task _packetHandlerName(string message)
    {
        if (_identified)
        {
            await _sendPacket(new Packet(PacketCommand.Name, _username));
            return;
        }
        
        // No need for locking here since it happens before repl init.
        Console.WriteLine(message);
        string? uname = null;
        while (string.IsNullOrEmpty(uname))
            uname = Console.ReadLine();

        await _sendPacket(new Packet(PacketCommand.Name, uname));
        _identified = true;
        _username = uname;
    }

    private Task _packetHandlerExit(string message)
    {
        _redrawConsole($"The server is disconnecting us with the following message:\n{message}");
        Running = false;
        return Task.CompletedTask;
    }

    private Task _packetHandlerList(string message)
    {
        _redrawConsole("LIST OF ACTIVE USERS: \n" + message);
        return Task.CompletedTask;
    }
    private Task _packetHandlerMsg(string message)
    {
        _redrawConsole(message);
        return Task.CompletedTask;
    }

    private Task _packetHandlerPrivateMsg(string message)
    {
        // The server formats the message for us
        // so we don't need to do anything different
        // for pv messages on the client side
        return _packetHandlerMsg(message);
    }
    public void Run()
    {
        bool wasRunning = Running;

        Thread? replThread = null;

        List<Task> tasks = new();
        while (Running)
        {
            tasks.Add(_handleIncomingPackets());

            if (_identified && replThread == null)
            {
                replThread = new Thread(new ThreadStart(_sendMsgRepl));
                replThread.Start();
            }
            if (_isDisconnected(_client))
            {
                Running = false;
                _redrawConsole("The server has disconnected from us.");
            }
        }


        Task.WaitAll(tasks.ToArray(), 1000);
#pragma warning disable SYSLIB0006 // Type or member is obsolete
        replThread?.Join();
#pragma warning restore SYSLIB0006 // Type or member is obsolete

        _cleanupNetworkResources();
        if (wasRunning)
            _redrawConsole("Disconnected");

    }


    private void _redrawConsole(string? message)
    {
        lock (consoleQueue)
        {
            Console.Clear();
            if (!string.IsNullOrEmpty(message))
            {
                consoleQueue.Add(message);
                _cursor = 0;
            }

            Console.SetCursorPosition(0, 0);
            string commandsSection = string.Format(
                "Commands:\n" +
                "  {0,-18}Disconect from the chat\n" +
                "  {1,-18}List all active users\n" +
                "  {2,-18}Send private message to another user\n" +
                "{3}\n",
                "/exit", "/list", "/pm <user> <msg>",
                new string('-', Console.WindowWidth - 1)
            );
            Console.Write(commandsSection);
            var linesRev = consoleQueue.SelectMany(s => s.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
                .Reverse().ToList();
            int displayLineCount = Console.WindowHeight - 8;

            bool cursorTop = linesRev.Count - _cursor <= displayLineCount;
            if (cursorTop)
                _cursor--;

            var consoleLines = cursorTop
                ? linesRev 
                : linesRev.Skip(_cursor).Take(displayLineCount);

            foreach (string s in consoleLines.Reverse())
                Console.WriteLine(s);

            if (Running)
            {
                Console.SetCursorPosition(0, Console.WindowHeight - 1);
                Console.Write("> {0}", inputBuffer);
            }
        }
    }

    private void _sendMsgRepl()
    {
        _redrawConsole($"Connected to the server as {_username}. Listening...");
        while (Running)
        {
            var key = Console.ReadKey();
            if (key.Key == ConsoleKey.Enter)
            {
                if (inputBuffer == "/exit")
                    Disconnect();
                else if (inputBuffer == "/list")
                {
                    _sendPacket(new Packet(PacketCommand.List))
                        .GetAwaiter().GetResult();
                }
                else if (inputBuffer.StartsWith("/pm"))
                {
                    string[] parts = inputBuffer.Split(' ', 3);

                    string username = parts[1];
                    string message = parts[2];

                    _sendPacket(new Packet(PacketCommand.PrivateMsg, message, username))
                        .GetAwaiter().GetResult();
                }
                else if (inputBuffer != string.Empty)
                    _sendPacket(new Packet(PacketCommand.Msg, inputBuffer))
                        .GetAwaiter().GetResult();
                inputBuffer = string.Empty;
            }
            else if (key.Key == ConsoleKey.UpArrow)
            {
                _cursor++;
                _redrawConsole(null);
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                _cursor = Math.Max(_cursor - 1, 0);
                _redrawConsole(null);
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (inputBuffer.Length > 0)
                    inputBuffer = inputBuffer[0..^1];
            }
            else
            {
                inputBuffer += key.KeyChar;
            }
            _redrawConsole(null);
        }
    }

    private async Task _sendPacket(Packet packet)
    {
        try
        {
            byte[] jsonBuffer = Encoding.UTF8.GetBytes(packet.ToJson());
            byte[] lengthBuffer = BitConverter.GetBytes(Convert.ToUInt16(jsonBuffer.Length));

            byte[] packetBuffer = new byte[lengthBuffer.Length + jsonBuffer.Length];
            lengthBuffer.CopyTo(packetBuffer, 0);
            jsonBuffer.CopyTo(packetBuffer, lengthBuffer.Length);

            await _netStream!.WriteAsync(packetBuffer, 0, packetBuffer.Length);
        }
        catch (Exception) { }
    }
    private bool _isDisconnected(TcpClient client)
    {
        try
        {
            Socket s = client.Client;
            return s.Poll(10 * 1000, SelectMode.SelectRead) && (s.Available == 0);
        }
        catch (SocketException)
        {
            return true;
        }
    }
    private async Task _handleIncomingPackets()
    {
        try
        {
            if (_client.Available > 0)
            {
                byte[] lengthBuffer = new byte[2];
                _ = await _netStream!.ReadAsync(lengthBuffer, 0, 2);
                ushort packetByteSize = BitConverter.ToUInt16(lengthBuffer, 0);

                byte[] jsonBuffer = new byte[packetByteSize];
                _ = await _netStream.ReadAsync(jsonBuffer, 0, jsonBuffer.Length);

                string jsonStr = Encoding.UTF8.GetString(jsonBuffer);
                Packet? packet = Packet.FromJson(jsonStr);

                if (packet == null) return;

                try
                {
                    await _commmandHandlers[packet.Command](packet.Message);
                }
                catch (KeyNotFoundException) { }
            }
        }
        catch (Exception) { }
    }
    public void Disconnect()
    {
        Console.WriteLine("Press any key to disconnect...");
        Running = false;
        //_sendPacket(bye);
    }

    //private async Task _sendPacket(Packet)
    private void _cleanupNetworkResources()
    {
        _netStream?.Close();
        _netStream = null;
        _client.Close();
    }

    private static Client? _broadcastClient = null;
    public static void Main(string[] args)
    {
        string host = "localhost";
        int port = 7878;

        _broadcastClient = new Client(host, port);

        Console.CancelKeyPress += Interrupt_Handler;

        _broadcastClient.Connect();
        _broadcastClient.Run();
    }

    private static void Interrupt_Handler(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _broadcastClient?.Disconnect();
    }
}