using Broadcast;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace Broadcast;

class Server
{
    private readonly TcpListener _listener;
    private readonly List<TcpClient> _clients = new();
    private readonly Dictionary<TcpClient, string> _names = new();

    private readonly int Port;

    public bool Running { get; private set; }


    Server(int port)
    {
        Port = port;
        _listener = new TcpListener(IPAddress.Any, Port);
    }

    public void Run()
    {
        _listener.Start();
        Running = true;

        Console.WriteLine("Starting the broadcast server on port {0}.", Port);
        Console.WriteLine("Press Ctrl-C to shutdown the server at any time.");

        List<Task> clientConnectionTasks = new();
        while (Running)
        {
            if (_listener.Pending())
                clientConnectionTasks.Add(_handleTcpConnection());

            Parallel.ForEach(_clients.ToArray(), (c) =>
            {
                if (_isDisconnected(c))
                    _handleDisconnectedClient(c);
            });

            Thread.Sleep(10);
        }

        Task.WaitAll(clientConnectionTasks.ToArray(), 1000);

        Parallel.ForEach(_clients, (c) =>
        {
            _disconnectClient(c, "The server is being shut down.");
        });

        _listener.Stop();
        Console.WriteLine("The server has been shut down.");
    }

    private void _disconnectClient(TcpClient client, string message = "")
    {
        Console.WriteLine("Disconnecting the client from {0}.", client.Client.RemoteEndPoint);

        if (message == "")
            message = "Goodbye.";

        Task byePacket = _sendPacket(client, new Packet(PacketCommand.Exit, message));

        // Give the client some time to send and proccess the graceful disconnect
        Thread.Sleep(100);

        byePacket.GetAwaiter().GetResult();
        _handleDisconnectedClient(client);
    }
    private async Task _handleTcpConnection()
    {
        Console.WriteLine("Trying to handle new connection...");
        TcpClient client = await _listener.AcceptTcpClientAsync();
        Console.WriteLine("New connection from {0}.", client.Client.RemoteEndPoint);

        _clients.Add(client);
 
        
        Packet nameInputPacket = new Packet(PacketCommand.Name, "Enter the username you want to use: ");
        await _sendPacket(client, nameInputPacket);

        Packet? answerPacket = null;
        while (answerPacket == null)
        {
            answerPacket = await _receivePacket(client);
            // Check if disconnected in the meantime
            if (!_clients.Contains(client) || !Running) return;
            await Task.Delay(100);
            await _sendPacket(client, nameInputPacket);
        }

        if (answerPacket.Command != PacketCommand.Name)
            throw new InvalidOperationException("Expected Name packet");

        string username = answerPacket.Message;
        Console.WriteLine("Adding client {0} as \"{1}\"", client.Client.RemoteEndPoint, username);
        _names.Add(client, username);

        bool runningClient = true;
        bool gracefulDisconnection = false;

        while (runningClient)
        {
            Packet? p = await _receivePacket(client);

            switch (p?.Command)
            {
                case PacketCommand.Exit:
                    runningClient = false;
                    break;
                case PacketCommand.Msg:
                    string broadcastMessage =
                        $"{_names[client]}[{client.Client.RemoteEndPoint}]: {p.Message}";
                    Packet broadcastPacket =
                        new Packet(PacketCommand.Msg, broadcastMessage);
                    foreach (TcpClient c in _clients.Where(_c => _c != client))
                        await _sendPacket(c, broadcastPacket);
                    break;
                case PacketCommand.PrivateMsg:
                    TcpClient? targetClient = _clients.Find(_c =>
                    {
                        return _names.ContainsKey(_c) ?
                            _names[_c] == p.MetaData :
                            false;
                    });
                    if (targetClient == null)
                        break;
                    string privateMessage =
                      $"{_names[client]}[{client.Client.RemoteEndPoint}](PRIVATE): {p.Message}";
                    Packet privatePacket =
                        new Packet(PacketCommand.PrivateMsg, privateMessage, username);
                    await _sendPacket(targetClient, privatePacket);
                    break;
                case PacketCommand.List:
                    string clientsDesc =
                        string.Join(
                            Environment.NewLine,
                            _clients.Select(_c => string.Format("{0,-18}{1}", _names[_c], _c.Client.RemoteEndPoint))
                        );
                    Packet listPacket =
                        new Packet(PacketCommand.List, clientsDesc);
                    await _sendPacket(client, listPacket);
                    break;
                default:
                    break;
            }

            runningClient &= _clients.Contains(client);
            runningClient &= Running;
            gracefulDisconnection = !Running;

            Thread.Sleep(10);
        }

        if (!gracefulDisconnection)
            _handleDisconnectedClient(client);
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
    private void _handleDisconnectedClient(TcpClient client)
    {
        if (!_clients.Contains(client))
        {
            // Already removed
            return;
        }

        Console.WriteLine($"Client {client.Client.RemoteEndPoint} disconnected.");
        _clients.Remove(client);
        _names.Remove(client);
        _cleanupClient(client);
    }

    private void _cleanupClient(TcpClient client)
    {
        client.GetStream().Close();
        client.Close();
    }

    private async Task _sendPacket(TcpClient client, Packet packet)
    {
        try
        {
            byte[] jsonData = Encoding.UTF8.GetBytes(packet.ToJson());
            byte[] lengthData = BitConverter.GetBytes(Convert.ToUInt16(jsonData.Length));

            byte[] packetData = new byte[jsonData.Length + lengthData.Length];
            lengthData.CopyTo(packetData, 0);
            jsonData.CopyTo(packetData, 2);

            await client.GetStream().WriteAsync(packetData, 0, packetData.Length);

            Console.WriteLine($"Sent Packet To Client: \n{packet.ToString()}");
        }
        catch (Exception e)
        {
            Console.WriteLine("There was an issue sending a packet.");
            Console.WriteLine("Reason: {0}", e.Message);
        }
    }

    private async Task<Packet?> _receivePacket(TcpClient client)
    {
        if (client.Available == 0) return null;
        try
        {
            NetworkStream netStream = client.GetStream();

            byte[] lengthBuffer = new byte[2];
            // TODO: check for number of bytes read.
            _ = await netStream.ReadAsync(lengthBuffer, 0, 2);
            ushort packetSize = BitConverter.ToUInt16(lengthBuffer);

            byte[] jsonBuffer = new byte[packetSize];
            _ = await netStream.ReadAsync(jsonBuffer, 0, jsonBuffer.Length);

            string jsonString = Encoding.UTF8.GetString(jsonBuffer);
            return Packet.FromJson(jsonString);
        }
        catch (Exception e)
        {
            Console.WriteLine("There was an issue receiving a packet.");
            Console.WriteLine("Reason: {0}", e.Message);
            return null;
        }
    }
    public void Shutdown()
    {
        Console.WriteLine("Shutting down server...");
        Running = false;
    }

    private static Server? broadcastServer;
    public static void Main(string[] args)
    {
        broadcastServer = new Server(7878);
        Console.CancelKeyPress += Interrupt_Handler;

        broadcastServer.Run();
    }

    private static void Interrupt_Handler(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        broadcastServer?.Shutdown();
    }
}