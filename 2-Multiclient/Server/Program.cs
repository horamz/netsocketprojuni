using System.Net;
using System.Net.Sockets;
using System.Text;

class Server
{
    private readonly TcpListener _listener;

    private static readonly int BufferSize = 2 * 1048;

    public bool Running { get; private set; }
    private int Port;

    public Server(int port)
    {
        Port = port;
        _listener = new TcpListener(IPAddress.Any, port);
        Running = true;
    }

    public async Task Run()
    {
        _listener.Start();
        Console.WriteLine("Server started at port {0}...", Port);
        while (Running)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync();
            _ = _handleTcpConnection(client);
        }
    }

    private async Task _handleTcpConnection(TcpClient client)
    {
        client.SendBufferSize = BufferSize;
        client.ReceiveBufferSize = BufferSize;

        EndPoint? endPoint = client.Client.RemoteEndPoint;
        Console.WriteLine("Client {0} connected.", endPoint);

        using NetworkStream netStream = client.GetStream();

        bool clientConnected = true;

        while (clientConnected)
        {
            if (client.Available > 0)
            {
                byte[] msgBuffer = new byte[BufferSize];
                int bytesRead = await netStream.ReadAsync(msgBuffer, 0, msgBuffer.Length);
                if (bytesRead > 0)
                {
                    string msg = Encoding.UTF8.GetString(msgBuffer);
                    Console.WriteLine($"Received message from {endPoint} : {msg}");

                    string resp = $"Server ACK: Received {bytesRead} bytes.";
                    await netStream.WriteAsync(Encoding.UTF8.GetBytes(resp));
                }
            }

            await Task.Delay(10);

            clientConnected &= !_isDisconnected(client);
        }

        Console.WriteLine("Client {0} disconneted.", endPoint);
        client.Close();
    }
    public void Shutdown()
    {
        Console.WriteLine("Shutting server down...");
        Running = false;
    }

    private static bool _isDisconnected(TcpClient client)
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
    private static Server? multiclientServer;

    public static void Main(string[] args)
    {
        Console.CancelKeyPress += InterruptHandler;

        multiclientServer = new Server(7878);
        multiclientServer.Run().GetAwaiter().GetResult();
    }

    private static void InterruptHandler(object? sender, ConsoleCancelEventArgs e)
    {
        multiclientServer?.Shutdown();
        e.Cancel = true;
    }
}