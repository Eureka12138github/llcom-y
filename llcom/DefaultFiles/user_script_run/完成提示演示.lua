--[[
====================================================================
 完成提示演示（apiNotify + 托盘未读角标）
====================================================================

这个脚本演示什么
  1. 脚本跑完后主动调用 apiNotify 弹托盘气泡（正常完成 = 绿色角标）；
  2. 连续发多条提示时，托盘右键「最近提示」和窗口底部状态栏会保留最近 5 条；
  3. 气泡只显示几秒，错过也没关系：角标 + 闪烁会一直提醒，直到你回到主窗口；
  4. 脚本报错时会有红色角标的自动提示（见下方 DEBUG_ERROR）。

建议验证步骤
  1. 先把 LLCOM-Y 最小化，或让别的窗口挡住它，再点「运行」；
  2. 约 1 秒后开始出提示，观察：气泡 → 托盘图标绿点闪烁 30 秒；
  3. 鼠标悬浮托盘图标：闪烁停止，绿点保留；
  4. 点击托盘图标（或点底部状态栏里的提示）：回到主窗口，绿点消失；
  5. 右键托盘图标 →「最近提示」，能看到本脚本发出的几条记录。

可调参数（改完直接再运行）
  NOTIFY_COUNT   连续发几条提示（1~5，默认 3）
  STEP_DELAY_MS  每一步之间的等待（毫秒，默认 1000）
  DEBUG_ERROR    true = 最后抛一个演示错误，验证红色角标（脚本不会自动停止）

提示开关
  「更多设置 → 脚本结束提示」     总开关（关闭后不弹气泡、托盘图标消失）
  「更多设置 → 托盘图标闪烁提醒」  只控制闪烁，关闭后仍保留彩色角标

注意
  Lua 脚本是事件驱动模型，本脚本发完提示后不会自动退出，
  需要点日志视图里的「停止」结束运行。
]]

local NOTIFY_COUNT = 3
local STEP_DELAY_MS = 1000
local DEBUG_ERROR = false

local SCRIPT_NAME = "完成提示演示"
local TOTAL_STEPS = 3
local startMs = apiNowMs()

-- 统一日志格式，方便在脚本日志里数步骤
local function logStep(step, text)
    log.info(
        "notify-demo",
        string.format("[%d/%d] %s", step, TOTAL_STEPS, text)
    )
end

logStep(1, "开始：本次会发送 " .. NOTIFY_COUNT .. " 条完成提示")

sys.taskInit(function()
    -- 模拟一段需要等待的工作（换成你自己的串口收发/解析逻辑即可）
    sys.wait(STEP_DELAY_MS)
    logStep(2, "模拟任务执行中（等待 " .. STEP_DELAY_MS .. " ms）")

    sys.wait(STEP_DELAY_MS)
    local elapsed = apiNowMs() - startMs
    logStep(3, string.format("模拟任务完成，用时 %d ms", elapsed))

    -- 第 1 条：真正的“完成”提示，内容可以按实际结果拼接
    apiNotify(
        SCRIPT_NAME,
        string.format("任务完成：%d 个步骤，用时 %d ms", TOTAL_STEPS, elapsed)
    )

    -- 第 2 条起：用于验证「最近提示」列表和连续气泡
    for i = 2, NOTIFY_COUNT do
        sys.wait(STEP_DELAY_MS)
        apiNotify(
            SCRIPT_NAME,
            string.format("第 %d/%d 条：用于验证托盘「最近提示」和状态栏留痕", i, NOTIFY_COUNT)
        )
    end

    log.info(
        "notify-demo",
        "提示已发送：看托盘图标角标 / 右键「最近提示」/ 窗口底部状态栏"
    )

    if DEBUG_ERROR then
        apiNotify(SCRIPT_NAME, "接下来会抛一个演示错误，用于验证红色角标")
        sys.wait(STEP_DELAY_MS)
        -- 任务回调里的错误不会终止脚本：运行环境会记录日志，并弹一次红色“脚本出错”提示
        error("这是演示错误：用于验证脚本报错时的红色角标提示")
    end
end)