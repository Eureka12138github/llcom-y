using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace llcom.LuaEnv
{
    class LuaLoader
    {
        /// <summary>
        /// 初始化lua对象
        /// </summary>
        /// <param name="lua"></param>
        public static void Initial(XLua.LuaEnv lua, string t = "script")
        {
            //utf8转gbk编码的hex值
            lua.DoString("apiUtf8ToHex = CS.llcom.LuaEnv.LuaApis.Utf8ToAsciiHex");
            lua.DoString("apiAscii2Utf8 = CS.llcom.LuaEnv.LuaApis.Ascii2Utf8");
            //获取软件目录路径
            lua.DoString("apiGetPath = CS.llcom.LuaEnv.LuaApis.GetPath");
            //输出日志
            lua.DoString("apiPrintLog = CS.llcom.LuaEnv.LuaApis.PrintLog");
            //获取快捷发送区数据
            lua.DoString("apiQuickSendList = CS.llcom.LuaEnv.LuaApis.QuickSendList");
            //输入框
            lua.DoString("apiInputBox = CS.llcom.LuaEnv.LuaApis.InputBox");
            //加点
            lua.DoString("apiAddPoint = CS.llcom.LuaEnv.LuaApis.AddPoint");

            //发送数据到通用通道
            lua.DoString("apiSend = CS.llcom.LuaEnv.LuaApis.Send");

            if (t != "send")
            {
                //定时器
                lua.DoString("apiStartTimer = CS.llcom.LuaEnv.LuaRunEnv.StartTimer");
                lua.DoString("apiStopTimer = CS.llcom.LuaEnv.LuaRunEnv.StopTimer");
            }

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

            //运行初始化文件
            lua.DoString("require 'core_script.head'");

            if (t == "send")
            {
                lua.DoString(
                    @"
--只运行一次的代码
local rootPath = apiUtf8ToHex(apiGetPath()):fromHex()
--读到的文件
local script = {}
_G[""!once!""] = function()
    runLimitStart(3)
    if not script[_G[""!file!""]] then
        script[_G[""!file!""]] = load(CS.System.IO.File.ReadAllText(_G[""!file!""]))
    end
    local result = script[_G[""!file!""]]()
    runLimitStop()
    return result
end
"
                );
            }
        }

        private static XLua.LuaEnv luaRunner = null;

        /// <summary>
        /// 运行lua文件并获取结果
        /// </summary>
        /// <param name="file"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static byte[] Run(
            string file,
            ArrayList args = null,
            string path = "user_script_send_convert/"
        )
        {
            //文件不存在
            if (!File.Exists(Tools.Global.ProfilePath + path + file))
                return new byte[] { };

            if (luaRunner == null)
            {
                luaRunner = new XLua.LuaEnv();
                lock (luaRunner)
                {
                    luaRunner.Global.SetInPath("runType", "send"); //一次性处理标志
                    Initial(luaRunner, "send");
                }
            }
            lock (luaRunner)
            {
                var pathIn = Tools.Global.ProfilePath + path + file;
                luaRunner.Global.SetInPath("!file!", pathIn);
                //防御性重试（原为无界 while，设置失败会死循环）
                for (
                    int i = 0;
                    i < 100 && luaRunner.Global.GetInPath<string>("!file!") != pathIn;
                    i++
                )
                    luaRunner.Global.SetInPath("!file!", pathIn);

                if (args != null)
                    for (int i = 0; i < args.Count; i += 2)
                    {
                        luaRunner.Global.SetInPath((string)args[i], args[i + 1]);
                    }

                XLua.LuaFunction f = null;
                try
                {
                    //有界重试，避免原无界 while 死循环
                    for (int i = 0; i < 100 && f == null; i++)
                        f = luaRunner.Global.Get<XLua.LuaFunction>("!once!");
                    if (f == null)
                        throw new Exception("Lua 运行环境未就绪：未找到 !once! 函数");
                    var lr = f.Call(null, new Type[] { typeof(byte[]) });
                    //脚本可能没有 return / 返回 nil：此时结果为空字节数组，避免上层拿到 null 后空引用
                    var r = (lr != null && lr.Length > 0) ? lr[0] as byte[] : null;
                    return r ?? new byte[] { };
                }
                catch (Exception e)
                {
                    luaRunner.Dispose();
                    luaRunner = null;
                    throw new Exception(e.ToString());
                }
            }
        }

        /// <summary>
        /// 清除运行用的脚本虚拟机，实现重新加载所有文件的功能
        /// </summary>
        public static void ClearRun()
        {
            luaRunner = null;
        }
    }
}
