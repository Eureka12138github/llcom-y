using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using llcom.LuaEnv;

namespace llcom.LuaEnv;

/// <summary>
/// lua虚拟机对象
/// 自带luat task框架接口
/// </summary>
public class LuaEnv : IDisposable
{
    //lua虚拟机
    public XLua.LuaEnv lua;

    //报错的回调
    public event EventHandler<string> ErrorEvent;

    //打印日志的回调
    public event EventHandler<string> PrintEvent;

    //停止运行的回调
    public event EventHandler<bool> StopEvent;

    private bool stop = false; //是否停止运行
    private ConcurrentDictionary<int, CancellationTokenSource> timerPool =
        new ConcurrentDictionary<int, CancellationTokenSource>(); //timer取消标志池子
    private ConcurrentBag<LuaTaskData> toRun = new ConcurrentBag<LuaTaskData>(); //待运行的池子
    private readonly object taskLock = new object();
    private XLua.LuaFunction triggerCB = null;

    /// <summary>
    /// 发出报错信息
    /// </summary>
    /// <param name="msg">报错信息</param>
    private void error(string msg)
    {
        ErrorEvent?.Invoke(lua, msg);
    }

    /// <summary>
    /// 打印日志
    /// </summary>
    /// <param name="msg">日志信息</param>
    public void print(string msg)
    {
        PrintEvent?.Invoke(lua, msg);
    }

    /// <summary>
    /// 添加一个回调触发
    /// </summary>
    /// <param name="type">触发类型</param>
    /// <param name="data">传递数据</param>
    public void addTigger(string type, object data) => addTask(-1, type, data);

    /// <summary>
    /// 添加一个
    /// </summary>
    /// <param name="id"></param>
    /// <param name="type"></param>
    /// <param name="data"></param>
    private void addTask(int id, string type, object data)
    {
        if (!stop)
        {
            toRun.Add(
                new LuaTaskData
                {
                    id = id,
                    type = type,
                    data = data,
                }
            );
            runTask();
        }
    }

    /// <summary>
    /// 跑一遍任务池子里的任务
    /// </summary>
    private void runTask()
    {
        lock (taskLock)
            while (toRun.Count > 0)
            {
                try
                {
                    LuaTaskData task;
                    toRun.TryTake(out task); //取出来一个任务
                    triggerCB.Call(task.id, task.type, task.data); //跑
                }
                catch (Exception e)
                {
                    ErrorEvent?.Invoke(lua, e.Message);
                }
                if (stop) //任务停了
                    return;
            }
    }

    /// <summary>
    /// 新建定时器
    /// </summary>
    /// <param name="id">编号</param>
    /// <param name="time">时间(ms)</param>
    public int StartTimer(int id, int time)
    {
        CancellationTokenSource timerToken = new CancellationTokenSource();
        if (timerPool.ContainsKey(id)) //如果已经有一个一样的定时器了？
            StopTimer(id);
        timerPool.TryAdd(id, timerToken); //加到池子里
        var timer = new System.Timers.Timer(time);
        timer.Elapsed += (sender, e) =>
        {
            if (timerToken == null || timerToken.IsCancellationRequested)
                return;
            if (stop)
                return;
            timerPool.TryRemove(id, out _);
            addTask(id, "timer", null);
            ((System.Timers.Timer)sender).Dispose();
        };
        timer.AutoReset = false;
        timer.Start();
        return 1;
    }

    /// <summary>
    /// 停止定时器
    /// </summary>
    /// <param name="id">编号</param>
    public void StopTimer(int id)
    {
        if (timerPool.ContainsKey(id))
        {
            try
            {
                CancellationTokenSource tc;
                timerPool.TryRemove(id, out tc);
                tc.Cancel();
            }
            catch { /* 忽略：取消定时器失败 */ }
        }
    }

    /// <summary>
    /// 跑代码
    /// </summary>
    /// <param name="s">代码</param>
    /// <returns>返回的结果</returns>
    public object[] DoString(string s)
    {
        try
        {
            lock (taskLock)
                return lua.DoString(s);
        }
        catch (Exception e)
        {
            ErrorEvent?.Invoke(lua, e.Message);
            throw new Exception(e.Message);
        }
    }

    /// <summary>
    /// 跑文件
    /// </summary>
    /// <param name="s">文件路径</param>
    /// <returns>返回的结果</returns>
    public object[] DoFile(string f)
    {
        try
        {
            var s = File.ReadAllBytes(f);
            lock (taskLock)
                return lua.DoString(s);
        }
        catch (Exception e)
        {
            ErrorEvent?.Invoke(lua, e.Message);
            throw new Exception(e.Message);
        }
    }

    /// <summary>
    /// 初始化，加载全部接口
    /// </summary>
    /// <param name="input">输入值，一般为匿名类</param>
    public LuaEnv(object input = null)
    {
        lua = new XLua.LuaEnv();
        if (input != null)
            lua.Global.SetInPath("lua", input); //传递输入值
        lock (taskLock)
            lua.DoString(sysCode);
        triggerCB = lua.Global.Get<XLua.LuaTable>("sys").Get<XLua.LuaFunction>("tiggerCB");
        lua.Global.SetInPath("@this", this); //自己传给自己

        //加上需要require的路径
        lua.DoString(
            @"
local rootPath = '"
                + LuaApis.Utf8ToAsciiHex(LuaApis.GetPath())
                + @"'
rootPath = rootPath:gsub('[%s%p]', ''):upper()
rootPath = rootPath:gsub('%x%x', function(c)
                                    return string.char(tonumber(c, 16))
                                end)
package.path = package.path..
';'..rootPath..'core_script/?.lua'..
';'..rootPath..'?.lua'..
';'..rootPath..'user_script_run/requires/?.lua'
package.cpath = package.cpath..
';'..rootPath..'core_script/?.lua'..
';'..rootPath..'?.lua'..
';'..rootPath..'user_script_run/requires/?.lua'
"
        );
    }

    /// <summary>
    /// 销毁
    /// </summary>
    public void Dispose()
    {
        lock (taskLock)
        {
            stop = true; //停止待运行任务
            lua.Dispose(); //释放lua虚拟机
            foreach (var v in timerPool)
            {
                v.Value.Cancel(); //取消所有timer
            }
            timerPool.Clear(); //清空timer信息池子
            while (toRun.TryTake(out _))
                ; //清空待运行池子
        }
        StopEvent?.Invoke(null, true);
    }

    /// <summary>
    /// Luat 协程调度框架源码。
    /// 原为内嵌于此文件的 300+ 行字符串，现抽取为 EmbeddedResource（<c>LuaEnv/sys.lua</c>），
    /// 既让本文件回归“加载器/宿主”职责，也让 Lua 源码获得独立文件与语法高亮。
    /// </summary>
    private static readonly string sysCode = LoadSysCode();

    /// <summary>从内嵌资源读取 Lua 源码（不依赖磁盘文件）。</summary>
    private static string LoadSysCode()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames();
        var name =
            names.FirstOrDefault(n => n.EndsWith("LuaEnv.sys.lua", StringComparison.Ordinal))
            ?? names.FirstOrDefault(n => n.EndsWith("sys.lua", StringComparison.Ordinal));
        if (name == null)
            throw new FileNotFoundException("未找到内嵌 Lua 资源：LuaEnv/sys.lua");
        using (var stream = asm.GetManifestResourceStream(name))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            return reader.ReadToEnd();
    }
}

class LuaTaskData
{
    public int id { get; set; }
    public string type { get; set; }
    public object data { get; set; }
}
