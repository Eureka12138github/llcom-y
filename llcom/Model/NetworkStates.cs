using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace llcom.Model;

/// <summary>
/// TCP / SSL 异步读取的缓冲状态。
/// 原为 <c>SocketClientPage.StateObject</c>（被 TcpLocalPage 通过 <c>using static</c> 跨页借用），
/// 现移到 Model 层，消除页面之间的隐藏耦合。
/// </summary>
public class StateObject
{
    public Socket workSocket = null;
    public SslStream workStream = null;
    public const int BUFFER_SIZE = 204800;
    public byte[] buffer = new byte[BUFFER_SIZE];
    public bool isSSL = false;
}

/// <summary>
/// Socket / SslStream 的统一发送与关闭封装（原 <c>SocketClientPage.SocketObj</c>）。
/// </summary>
public class SocketObj
{
    Socket socket;
    SslStream sslStream;

    public SocketObj(Socket s)
    {
        socket = s;
    }

    public SocketObj(SslStream ssl)
    {
        sslStream = ssl;
    }

    public void Send(byte[] buff)
    {
        if (socket != null)
            socket.Send(buff);
        else if (sslStream != null)
        {
            sslStream.Write(buff);
        }
    }

    public void Close()
    {
        if (socket != null)
        {
            socket.Close();
            socket.Dispose();
        }
        else if (sslStream != null)
        {
            sslStream.Close();
            sslStream.Dispose();
        }
    }
}

/// <summary>
/// UDP 异步接收状态（原 <c>UdpLocalPage.UdpState</c>）。
/// </summary>
public struct UdpState
{
    public UdpClient u;
    public IPEndPoint e;
}