--[[
功能：电表上下电循环测试（通过继电器控制电源通断）
策略：
  1. 发送DO闭合指令（继电器闭合，电表上电）
  2. 等待上电保持时间
  3. 发送DO断开指令（继电器断开，电表下电）
  4. 等待下电保持时间
  5. 循环执行，完成指定周期数

注意：本脚本仅控制继电器通断，不涉及电表通信验证
      如需验证电表状态，请配合其他脚本使用
      不要将其他事件和 DO 关联，否则无法控制继电器！
]]

-- ============================================================
--  【用户配置区】 - 所有可调整参数集中在此
-- ============================================================

-- 1. 测试控制参数
local TARGET_CYCLES = 100         -- 目标循环次数（上下电周期数）
local SEND_RETRY_TIMES = 3        -- 串口发送失败时的重试次数

-- 2. 时间参数（单位：秒）
local POWER_ON_TIME = 8           -- 上电保持时间（继电器闭合时间）
local POWER_OFF_TIME = 3          -- 下电保持时间（继电器断开时间）
local INTERVAL_BETWEEN_CYCLES = 0 -- 每个周期之间的额外等待时间（秒）

-- 3. Modbus 通信参数
local MODBUS_SLAVE_ID = 0x01      -- 从站设备ID（默认为1，可通过上位机修改）

-- 4. DO控制寄存器配置
local DO_REG_ADDR = 0xA822        -- DO控制寄存器地址
local DO_ON_VALUE = 0xFF01        -- DO闭合值（继电器吸合，电表上电）
local DO_OFF_VALUE = 0x0001       -- DO断开值（继电器释放，电表下电）

-- 5. DO通道选择（用于日志显示）
-- 可选值: "DO1", "DO2", "DO3", "DO4"
local DO_CHANNEL = "DO1"          -- 当前控制的是DO1通道

-- 6. 日志开关
local ENABLE_DEBUG_LOG = false     -- 是否输出详细调试日志
local ENABLE_FILE_LOG = true      -- 是否写入文件日志

-- 如需固定位置，可改为绝对路径，例如: "D:llcom-y/logs/power_cycle_test.log"
-- 获取 LLCOM 软件所在目录，并拼接日志路径
local LOG_FILE = apiGetPath() .. "/logs/power_cycle_" .. os.date("%Y%m%d") .. ".log"

-- ============================================================
--  【工具函数区】
-- ============================================================

-- CRC16 Modbus 计算
local function crc16_modbus(data)
    local crc = 0xFFFF
    for i = 1, #data do
        crc = crc ~ string.byte(data, i)
        for _ = 1, 8 do
            local lsb = crc & 0x0001
            crc = crc >> 1
            if lsb == 1 then crc = crc ~ 0xA001 end
        end
    end
    return string.char(crc & 0xFF, (crc >> 8) & 0xFF)
end

-- ============================================================
--  【日志函数】
-- ============================================================

-- 将十六进制字符串转为可读格式
local function hexDump(data)
    if not data or #data == 0 then return "(空)" end
    local hex = ""
    for i = 1, #data do
        hex = hex .. string.format("%02X ", string.byte(data, i))
    end
    return hex:sub(1, -2)
end

-- 写入文件日志
local function writeFileLog(msg)
    if not ENABLE_FILE_LOG then return end
    local f = io.open(LOG_FILE, "a")
    if f then
        f:write(os.date("%Y-%m-%d %H:%M:%S") .. " | " .. msg .. "\n")
        f:close()
    else
        log.warn("POWER_CYCLE", "无法写入日志文件: " .. LOG_FILE)
    end
end

-- ============================================================
--  【发送函数】
-- ============================================================

-- 发送 Modbus 命令（通用）
local function sendModbusFrame(frame, desc)
    local success, result = pcall(function()
        return apiSend("uart", frame)
    end)
    
    if not success then
        log.error("POWER_CYCLE", string.format("%s 发送异常: %s", desc, tostring(result)))
        return false
    end
    
    if result == nil or result == false then
        log.warn("POWER_CYCLE", string.format("%s 发送失败（设备未连接或串口未打开）", desc))
        return false
    end
    
    if ENABLE_DEBUG_LOG then
        log.info("POWER_CYCLE", string.format("%s 发送帧: %s", desc, hexDump(frame)))
    end
    
    return true
end

-- 带重试的发送函数
local function sendWithRetry(sendFunc, desc, ...)
    for retry = 1, SEND_RETRY_TIMES do
        if retry > 1 then
            log.info("POWER_CYCLE", string.format("%s 发送失败，1秒后重试 (%d/%d)...", 
                        desc, retry, SEND_RETRY_TIMES))
            sys.wait(1000)
        end
        
        local success = sendFunc(...)
        if success then
            return true
        end
    end
    return false
end

-- ============================================================
--  【DO控制函数】
-- ============================================================

-- 控制DO输出（使用 10 功能码写单个寄存器）
local function setDO(doValue, doDesc)
    local regAddrHigh = (DO_REG_ADDR >> 8) & 0xFF
    local regAddrLow = DO_REG_ADDR & 0xFF
    
    -- 将DO值拆分为字节（2字节）
    local valueHigh = (doValue >> 8) & 0xFF
    local valueLow = doValue & 0xFF
    
    local frame = string.char(
        MODBUS_SLAVE_ID,
        0x10,                      -- 功能码：写多个寄存器
        regAddrHigh,
        regAddrLow,
        0x00, 0x01,                -- 寄存器数量（1个寄存器）
        0x02,                      -- 字节数（2字节）
        valueHigh,
        valueLow
    )
    
    -- 添加 CRC
    local crc = crc16_modbus(frame)
    frame = frame .. crc
    
    return sendModbusFrame(frame, string.format("控制%s %s", DO_CHANNEL, doDesc))
end

-- ============================================================
--  【等待函数】
-- ============================================================

local function waitSeconds(seconds, desc)
    if seconds <= 0 then return end
    
    if ENABLE_DEBUG_LOG then
        log.info("POWER_CYCLE", string.format("等待 %d 秒，%s...", seconds, desc or ""))
    end
    
    sys.wait(seconds * 1000)
    
    if ENABLE_DEBUG_LOG then
        log.info("POWER_CYCLE", "等待完成")
    end
end

-- ============================================================
--  【主业务逻辑】
-- ============================================================

local function runTest()
    -- 计算预计总耗时
    local cycleTime = POWER_ON_TIME + POWER_OFF_TIME + INTERVAL_BETWEEN_CYCLES
    local totalSeconds = cycleTime * TARGET_CYCLES
    local totalMinutes = math.floor(totalSeconds / 60)
    local remainingSeconds = totalSeconds % 60
    local totalHours = math.floor(totalMinutes / 60)
    totalMinutes = totalMinutes % 60
    
    local startTime = os.date("%Y-%m-%d %H:%M:%S")
    log.info("POWER_CYCLE", "========================================")
    log.info("POWER_CYCLE", "电表上下电循环测试开始")
    log.info("POWER_CYCLE", string.format("系统当前时间: %s", startTime))
    log.info("POWER_CYCLE", string.format("目标循环次数: %d 次", TARGET_CYCLES))
    log.info("POWER_CYCLE", string.format("控制通道: %s", DO_CHANNEL))
    log.info("POWER_CYCLE", string.format("从站地址: 0x%02X", MODBUS_SLAVE_ID))
    log.info("POWER_CYCLE", string.format("DO寄存器地址: 0x%04X", DO_REG_ADDR))
    log.info("POWER_CYCLE", string.format("DO闭合值: 0x%04X (%s上电)", DO_ON_VALUE, DO_CHANNEL))
    log.info("POWER_CYCLE", string.format("DO断开值: 0x%04X (%s下电)", DO_OFF_VALUE, DO_CHANNEL))
    log.info("POWER_CYCLE", string.format("上电保持时间: %d 秒", POWER_ON_TIME))
    log.info("POWER_CYCLE", string.format("下电保持时间: %d 秒", POWER_OFF_TIME))
    if INTERVAL_BETWEEN_CYCLES > 0 then
        log.info("POWER_CYCLE", string.format("周期间隔时间: %d 秒", INTERVAL_BETWEEN_CYCLES))
    end
    log.info("POWER_CYCLE", string.format("每个周期耗时: %d 秒", cycleTime))
    log.info("POWER_CYCLE", string.format("预计总耗时: %d小时 %d分 %d秒", totalHours, totalMinutes, remainingSeconds))
    log.info("POWER_CYCLE", "========================================")
    log.info("POWER_CYCLE", "注意：请确保继电器模块已正确连接电表电源！")
    log.info("POWER_CYCLE", "========================================")
    log.info("POWER_CYCLE", "")
    
    writeFileLog("===== 测试开始 =====")
    writeFileLog(string.format("目标循环次数: %d, 上电%d秒/下电%d秒", TARGET_CYCLES, POWER_ON_TIME, POWER_OFF_TIME))
    
    local successCount = 0
    local failed = false
    
    for i = 1, TARGET_CYCLES do
        log.info("POWER_CYCLE", string.format("========== 第 %d/%d 个周期 ==========", i, TARGET_CYCLES))
        
        -- ====== 步骤1：闭合DO（电表上电） ======
        log.info("POWER_CYCLE", string.format("[步骤1] %s闭合（电表上电）", DO_CHANNEL))
        writeFileLog(string.format("[第%d周期] %s闭合，电表上电", i, DO_CHANNEL))
        
        if not sendWithRetry(setDO, string.format("%s闭合", DO_CHANNEL), DO_ON_VALUE, "闭合") then
            log.error("POWER_CYCLE", string.format("第 %d 周期控制%s闭合失败，终止测试", i, DO_CHANNEL))
            failed = true
            break
        end
        
        -- 上电保持
        log.info("POWER_CYCLE", string.format("[步骤2] 上电保持 %d 秒...", POWER_ON_TIME))
        waitSeconds(POWER_ON_TIME, "上电保持")
        
        -- ====== 步骤3：断开DO（电表下电） ======
        log.info("POWER_CYCLE", string.format("[步骤3] %s断开（电表下电）", DO_CHANNEL))
        writeFileLog(string.format("[第%d周期] %s断开，电表下电", i, DO_CHANNEL))
        
        if not sendWithRetry(setDO, string.format("%s断开", DO_CHANNEL), DO_OFF_VALUE, "断开") then
            log.error("POWER_CYCLE", string.format("第 %d 周期控制%s断开失败，终止测试", i, DO_CHANNEL))
            failed = true
            break
        end
        
        -- 下电保持
        log.info("POWER_CYCLE", string.format("[步骤4] 下电保持 %d 秒...", POWER_OFF_TIME))
        waitSeconds(POWER_OFF_TIME, "下电保持")
        
        -- 周期间隔
        if INTERVAL_BETWEEN_CYCLES > 0 then
            log.info("POWER_CYCLE", string.format("[步骤5] 周期间隔等待 %d 秒...", INTERVAL_BETWEEN_CYCLES))
            waitSeconds(INTERVAL_BETWEEN_CYCLES, "周期间隔")
        end
        
        successCount = successCount + 1
        
        log.info("POWER_CYCLE", string.format("第 %d 个周期完成", i))
        log.info("POWER_CYCLE", "")
    end
    
    -- ====== 测试结束，确保DO处于断开状态 ======
    log.info("POWER_CYCLE", "测试结束，确保DO处于断开状态...")
    sendWithRetry(setDO, string.format("%s断开（安全关闭）", DO_CHANNEL), DO_OFF_VALUE, "断开")
    
    if failed then
        log.info("POWER_CYCLE", "========================================")
        log.info("POWER_CYCLE", "=== 测试异常终止 ===")
        log.info("POWER_CYCLE", string.format("已成功完成 %d 个周期，第 %d 个周期失败", successCount, successCount + 1))
        log.info("POWER_CYCLE", string.format("结束时间: %s", os.date("%Y-%m-%d %H:%M:%S")))
        log.info("POWER_CYCLE", "========================================")
        writeFileLog("===== 测试异常终止，已成功 " .. successCount .. " 个周期 =====")
    else
        -- 计算实际耗时
        local endTime = os.date("%Y-%m-%d %H:%M:%S")
        log.info("POWER_CYCLE", "========================================")
        log.info("POWER_CYCLE", "=== 测试任务成功完成 ===")
        log.info("POWER_CYCLE", string.format("共完成 %d 个上下电周期", successCount))
        log.info("POWER_CYCLE", string.format("上电时间: %d 秒/次", POWER_ON_TIME))
        log.info("POWER_CYCLE", string.format("下电时间: %d 秒/次", POWER_OFF_TIME))
        log.info("POWER_CYCLE", string.format("开始时间: %s", startTime))
        log.info("POWER_CYCLE", string.format("结束时间: %s", endTime))
        log.info("POWER_CYCLE", "========================================")
        writeFileLog("===== 测试成功完成，共完成 " .. successCount .. " 个周期 =====")
    end
end

-- ============================================================
--  【启动入口】 - 带全局错误捕获
-- ============================================================

sys.taskInit(function()
    sys.wait(2000)
    
    local success, err = pcall(runTest)
    if not success then
        log.error("POWER_CYCLE", "脚本执行异常: " .. tostring(err))
        log.error("POWER_CYCLE", "提示：请检查串口是否已打开，继电器模块是否已上电")
    end
end)