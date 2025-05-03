using System.Net;
using System.Net.Sockets;
using System.Text;

class Client
{
    private readonly string ServerAddress = string.Empty;
    private readonly int Port;
    public bool Running { get; private set; }

    private TcpClient _client;
    private NetworkStream? _netStream;

    private static readonly int BufferSize = 2 * 1024;

    public Client(string serverAddress, int port)
    {
        ServerAddress = serverAddress;
        Port = port;

        _client = new TcpClient();

        _client.ReceiveBufferSize = BufferSize;
        _client.SendBufferSize = BufferSize;
    }

    public void Connect()
    {
        _client.Connect(ServerAddress, Port);
        EndPoint? endPoint = _client.Client.RemoteEndPoint;

        if (_client.Connected)
        {
            Console.WriteLine("Connected to the server at {0}", endPoint);
            _netStream = _client.GetStream();
            Running = true;
        }
        else
        {
            _releaseNetworkResources();
            Console.WriteLine("Wasn't able to connect to the server at {0}", endPoint);
        }
    }

    public void StartRepl()
    {
        while (Running)
        {
            string? msg = null;
            while (string.IsNullOrEmpty(msg))
            {
                Console.Write("> ");
                msg = Console.ReadLine();
            }
            if (msg.ToLower() == "exit" || msg.ToLower() == "quit")
            {
                Console.WriteLine("Disconnecting...");
                Running = false;
            }

            try
            {
                Console.WriteLine("Delivering...");
                byte[] msgBuffer = Encoding.UTF8.GetBytes(msg);
                _netStream?.Write(msgBuffer, 0, msgBuffer.Length);

                byte[] respBuffer = new byte[BufferSize];
                _netStream?.Read(respBuffer, 0, BufferSize);
                Console.WriteLine(Encoding.UTF8.GetString(respBuffer));
            }
            catch (IOException ex)
                when (ex.InnerException is SocketException se
                && se.SocketErrorCode == SocketError.ConnectionReset)
            {
                Console.WriteLine("Server got disconnected.");
                Running = false;
            }

            Thread.Sleep(10);

            if (_isDisconnected(_client))
            {
                Running = false;
                Console.WriteLine("Server has disconnected from us.");
            }
        }
    }
    private void _releaseNetworkResources()
    {
        _netStream?.Close();
        _netStream = null;
        _client.Close();
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
    private static Client? multiclientClient;


    public static void Main(string[] args)
    {
        multiclientClient = new Client("localhost", 7878);
        multiclientClient.Connect();
        multiclientClient.StartRepl();
    }

}
