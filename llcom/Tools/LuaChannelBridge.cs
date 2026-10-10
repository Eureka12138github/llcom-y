using System;
using llcom.LuaEnv;

namespace llcom.Tools;

/// <summary>
/// Lua 通用通道（<c>LuaApis.SendChannels</c>）的注册与上报封装。
/// 各工具页原先各自内联一段结构相同的 lambda（判空 + 判连接 + 调用发送），
/// 现统一到 <see cref="RegisterSend(string, Func{bool}, Func{byte[], bool})"/>。
/// </summary>
/// <remarks>
/// 本项目各工具页在 <c>MainWindow</c> 中只 Navigate 一次、随后常驻，
/// 因此一般无需反注册；<see cref="Unregister"/> 供将来页面可销毁时使用。
/// </remarks>
internal static class LuaChannelBridge
{
    /// <summary>
    /// 注册“Lua → 本通道”的发送回调：仅当 <paramref name="canSend"/> 为真且数据非空时，
    /// 才调用 <paramref name="send"/>，其返回值即发送是否成功。
    /// </summary>
    /// <param name="channel">通道名，如 "tcp-server"</param>
    /// <param name="canSend">当前是否可发送（通常是“已连接/服务已开启”）</param>
    /// <param name="send">实际的发送实现</param>
    public static void RegisterSend(string channel, Func<bool> canSend, Func<byte[], bool> send)
    {
        LuaApis.SendChannelsRegister(
            channel,
            (data, _) => data != null && canSend() && send(data)
        );
    }

    /// <summary>
    /// 注册“Lua → 本通道”的发送回调（需要访问 Lua 端 table 参数时使用，如 MQTT 的 topic/qos）。
    /// </summary>
    public static void RegisterSend(
        string channel,
        Func<bool> canSend,
        Func<byte[], XLua.LuaTable, bool> send
    )
    {
        LuaApis.SendChannelsRegister(
            channel,
            (data, table) => table != null && canSend() && send(data, table)
        );
    }

    /// <summary>本通道 → Lua 的数据上报。</summary>
    public static void RaiseReceived(string channel, object data) =>
        LuaApis.SendChannelsReceived(channel, data);

    /// <summary>反注册通道（页面可销毁时调用，释放对页面的引用）。</summary>
    public static void Unregister(string channel) => LuaApis.SendChannelsUnregister(channel);
}