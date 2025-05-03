using System.Net.Sockets;
using System.Text;

const int Port = 7878;
const int BufferSize = 2048;

using TcpClient client = new TcpClient("localhost", Port);
using NetworkStream netStream = client.GetStream();
Console.WriteLine("Connected to server. Sending message.");
netStream.Write(Encoding.UTF8.GetBytes("Hello server!"));
byte[] buffer = new byte[BufferSize];
int bytesRead = netStream.Read(buffer);
if (bytesRead == 0) Console.Error.WriteLine("Could not receive msg from server.");
string msg = Encoding.UTF8.GetString(buffer);
Console.WriteLine("Received msg from server: {0}\nTerminating...", msg);