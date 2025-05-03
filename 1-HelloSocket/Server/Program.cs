using System.Net;
using System.Net.Sockets;
using System.Text;

const int Port = 7878;
const int BufferSize = 2 * 1024;

using TcpListener server = new TcpListener(IPAddress.Any, Port);
server.Start(1);
Console.WriteLine("Listnening on port {0}...", Port);
using TcpClient client = server.AcceptTcpClient();
using NetworkStream netStream = client.GetStream();
Console.WriteLine("Client {0} connected.", client.Client.LocalEndPoint);
byte[] buffer = new byte[BufferSize];
int bytesRead = netStream.Read(buffer);
if (bytesRead == 0) Console.Error.WriteLine("Did not received anything from client.");
string msg = Encoding.UTF8.GetString(buffer);
Console.WriteLine("Received message from client: {0}", msg);
netStream.Write(Encoding.UTF8.GetBytes("Hello client!"));
Console.WriteLine("Sent message to client.\nTerminating...");