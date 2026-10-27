--[[
功能：按月高效测试整点冻结数据（新协议 0x0500）
策略：每次将时间设置为 "当月最后一天 23:59:58"，固定等待 4 秒让电表跨过零点触发月冻结
支持断点续传：中断后可继续从上次月份发送
注意：响应数据由独立的接收脚本处理，本脚本只负责发送

协议说明：
  起始地址 0x0500，共 7 个寄存器（14 字节），每个寄存器为 U16 大端 BCD：
    0x0500 年（BCD，仅后两位）
    0x0501 月（BCD）
    0x0502 日（BCD）
    0x0503 时（BCD）
    0x0504 分（BCD）
    0x0505 秒（BCD）
    0x0506 周（BCD，0~6，0 代表周日）
]]

-- ============================================================
--  【用户配置区】
-- ============================================================

-- 1. 测试控制参数
local TARGET_COUNT = 120         -- 目标发送次数（按月测试，120 次即 10 年）
local SEND_RETRY_TIMES = 3       -- 串口发送失败时的重试次数
local WAIT_AFTER_SEND = 4        -- 发送后等待秒数（让电表跨过零点）

-- 2. 起始日期（脚本会自动计算该月的最后一天进行发送）
local START_YEAR = 2026          -- 起始年份（完整4位）
local START_MONTH = 1            -- 起始月份 (1-12)

-- ⚠️ 按月测试时，时间固定为月底 23:59:58，一般不需要改
local FIXED_HOUR = 23            -- 固定小时 (23)
local FIXED_MINUTE = 59          -- 固定分钟 (59)
local FIXED_SECOND = 58          -- 固定秒 (58)

-- 3. 文件路径配置（Windows 路径，注意使用 /）
local RESUME_FILE = apiGetPath() .. "/logs/freeze_monthly_resume_0500.dat"
local LOG_FILE = apiGetPath() .. "/logs/freeze_monthly_test_0500_" .. os.date("%Y%m%d") .. ".log"    
local ENABLE_RESUME = false      -- 是否启用断点续传

-- 4. Modbus 通信参数
local MODBUS_SLAVE_ID = 0x01     -- 从站设备ID (默认1)
local MODBUS_REG_ADDR = 0x0500   -- 时间寄存器的起始地址 (0x0500)
local MODBUS_REG_COUNT = 0x07    -- 寄存器数量 (7个寄存器 = 14字节)

-- 5. 日志开关（调试用）
local ENABLE_DEBUG_LOG = false   -- 是否输出详细调试日志
local ENABLE_FILE_LOG = true    -- 是否写入文件日志

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

-- 十进制转 BCD（单字节）
local function dec2bcd(dec)
    return ((dec // 10) << 4) | (dec % 10)
end

-- 十进制转 16 位大端 BCD（高字节 0x00，低字节 BCD）
local function dec2bcd_u16(dec)
    local v = dec % 100
    return string.char(0x00, dec2bcd(v))
end

-- 判断闰年
local function isLeapYear(year)
    return (year % 400 == 0) or (year % 4 == 0 and year % 100 ~= 0)
end

-- 获取某月天数
local function getDaysInMonth(year, month)
    local daysInMonth = {31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31}
    if month == 2 and isLeapYear(year) then
        return 29
    end
    return daysInMonth[month]
end

-- 月份加 1（如果超过 12 月，则年份 +1，月份变 1）
local function addOneMonth(year, month)
    month = month + 1
    if month > 12 then
        month = 1
        year = year + 1
    end
    return year, month
end

-- 计算星期（蔡勒公式，0=周日, 1=周一 ... 6=周六）
local function getWeekDay(y, m, d)
    if m < 3 then
        m = m + 12
        y = y - 1
    end
    local c = math.floor(y / 100)
    local yy = y % 100
    local w = (yy + math.floor(yy/4) + math.floor(c/4) - 2*c + math.floor(13*(m+1)/5) + d) % 7
    local weekMap = {6, 0, 1, 2, 3, 4, 5}
    return weekMap[w + 1]
end

-- 将十六进制字符串转为可读格式（用于日志）
local function hexDump(data)
    if not data or #data == 0 then return "(空)" end
    local hex = ""
    for i = 1, #data do
        hex = hex .. string.format("%02X ", string.byte(data, i))
    end
    return hex:sub(1, -2)
end

-- ============================================================
--  【发送、日志和断点函数】
-- ============================================================

-- 发送命令（只发送，不等待响应）
local function sendCommand(frame)
    local success, result = pcall(function()
        return apiSend("uart", frame)
    end)

    if not success then
        log.error("TEST", "串口发送异常: " .. tostring(result))
        return false
    end

    if result == nil or result == false then
        log.warn("TEST", "串口发送失败（设备未连接或串口未打开）")
        return false
    end

    if ENABLE_DEBUG_LOG then
        log.info("TEST", "发送帧: " .. hexDump(frame))
    end

    return true
end

-- 写入文件日志
local function writeFileLog(msg)
    if not ENABLE_FILE_LOG then return end
    local f = io.open(LOG_FILE, "a")
    if f then
        f:write(os.date("%Y-%m-%d %H:%M:%S") .. " | " .. msg .. "\n")
        f:close()
    end
end

-- 保存断点信息（按月测试时，只保存年、月 和 序号）
local function saveResume(year, month, nextIndex)
    if not ENABLE_RESUME then return end
    local f = io.open(RESUME_FILE, "w")
    if f then
        local data = string.format("%d,%02d,%d", year, month, nextIndex)
        f:write(data)
        f:close()
        if ENABLE_DEBUG_LOG then
            log.info("RESUME", string.format("断点已保存: 第 %d 次 (月份: %d-%02d)", 
                        nextIndex, year, month))
        end
    else
        log.warn("RESUME", "无法保存断点文件: " .. RESUME_FILE)
    end
end

-- 加载断点信息
local function loadResume()
    if not ENABLE_RESUME then return nil end
    local f = io.open(RESUME_FILE, "r")
    if not f then return nil end
    local line = f:read("*l")
    f:close()

    if not line or line == "" then return nil end

    local year, month, nextIndex = line:match("(%d+),(%d+),(%d+)")
    if year and month and nextIndex then
        return tonumber(year), tonumber(month), tonumber(nextIndex)
    end
    return nil
end

-- 删除断点文件
local function clearResume()
    if not ENABLE_RESUME then return end

    log.info("RESUME", string.format("尝试删除断点文件: %s", RESUME_FILE))

    local checkFile = io.open(RESUME_FILE, "r")
    if checkFile then
        checkFile:close()
    else
        return
    end

    local success, err = os.remove(RESUME_FILE)
    if success then
        log.info("RESUME", "断点文件已删除: " .. RESUME_FILE)
    else
        log.warn("RESUME", string.format("断点文件删除失败: %s", tostring(err)))
        local f = io.open(RESUME_FILE, "w")
        if f then
            f:write("")
            f:close()
            log.info("RESUME", "断点文件内容已清空")
        end
    end
end

-- 固定等待函数
local function waitForFreeze(seconds)
    seconds = seconds or WAIT_AFTER_SEND
    if ENABLE_DEBUG_LOG then
        log.info("WAIT", string.format("等待 %d 秒，让电表跨过零点触发冻结...", seconds))
    end
    sys.wait(seconds * 1000)
    if ENABLE_DEBUG_LOG then
        log.info("WAIT", "等待完成")
    end
end

-- ============================================================
--  【主业务逻辑】
-- ============================================================

local function runTest()
    local startTime = os.date("%Y-%m-%d %H:%M:%S")
    log.info("TEST", "========================================")
    log.info("TEST", "按月测试整点冻结数据开始（新协议 0x0500）")
    log.info("TEST", string.format("系统当前时间: %s", startTime))
    log.info("TEST", string.format("起始配置月份: %d-%02d, 固定时间: %02d:%02d:%02d (每月最后一天)", 
                START_YEAR, START_MONTH, FIXED_HOUR, FIXED_MINUTE, FIXED_SECOND))
    log.info("TEST", string.format("目标次数: %d 次, 发送后等待: %d 秒", TARGET_COUNT, WAIT_AFTER_SEND))
    log.info("TEST", string.format("断点续传: %s", ENABLE_RESUME and "启用" or "禁用"))
    log.info("TEST", "========================================")

    -- 启动时清理"已完成"的断点
    if ENABLE_RESUME then
        local ry, rm, rIndex = loadResume()
        if ry and rIndex and rIndex > TARGET_COUNT then
            log.info("RESUME", string.format("检测到上一轮测试已完成，自动清理"))
            clearResume()
        end
    end

    -- 检查是否有断点
    local currentYear, currentMonth
    local startIndex = 1

    if ENABLE_RESUME then
        local ry, rm, rIndex = loadResume()
        if ry and rm and rIndex and rIndex <= TARGET_COUNT then
            currentYear = ry
            currentMonth = rm
            startIndex = rIndex
            log.info("RESUME", string.format("发现断点，从第 %d 次继续", startIndex))
            log.info("RESUME", string.format("断点月份: %d-%02d", currentYear, currentMonth))
        else
            currentYear = START_YEAR
            currentMonth = START_MONTH
            log.info("RESUME", "未发现有效断点，从初始配置开始")
        end
    else
        currentYear = START_YEAR
        currentMonth = START_MONTH
        log.info("RESUME", "断点续传已禁用，从初始配置开始")
    end

    -- 预计算 Modbus 命令的固定前缀
    local cmdPrefix = string.char(
        MODBUS_SLAVE_ID,
        0x10,
        (MODBUS_REG_ADDR >> 8) & 0xFF,
        MODBUS_REG_ADDR & 0xFF,
        (MODBUS_REG_COUNT >> 8) & 0xFF,
        MODBUS_REG_COUNT & 0xFF,
        MODBUS_REG_COUNT * 2
    )

    local successCount = startIndex - 1
    local failed = false

    for i = startIndex, TARGET_COUNT do
        -- 核心逻辑：动态计算当前月份的最后一天
        local currentDay = getDaysInMonth(currentYear, currentMonth)
        local weekDay = getWeekDay(currentYear, currentMonth, currentDay)

        if ENABLE_DEBUG_LOG then
            log.info("DEBUG", string.format("准备发送月份: %d-%02d, 月末日期: %d, 星期: %d", 
                        currentYear, currentMonth, currentDay, weekDay))
        end

        -- 构造时间数据（年、月 变化；日 为当月最后一天；时、分、秒 固定）
        local timeData =
            dec2bcd_u16(currentYear % 100) ..
            dec2bcd_u16(currentMonth) ..
            dec2bcd_u16(currentDay) ..
            dec2bcd_u16(FIXED_HOUR) ..
            dec2bcd_u16(FIXED_MINUTE) ..
            dec2bcd_u16(FIXED_SECOND) ..
            dec2bcd_u16(weekDay)

        local payload = cmdPrefix .. timeData
        local frame = payload .. crc16_modbus(payload)

        local logMsg = string.format("[%d/%d] %d-%02d-%02d 周%d %02d:%02d:%02d (月末)", 
                    i, TARGET_COUNT, currentYear, currentMonth, currentDay, 
                    weekDay, FIXED_HOUR, FIXED_MINUTE, FIXED_SECOND)
        log.info("SEND", logMsg)
        writeFileLog("SEND: " .. logMsg)

        -- ====== 发送命令 ======
        local sendSuccess = false
        for retry = 1, SEND_RETRY_TIMES do
            if retry > 1 then
                log.info("TEST", string.format("发送失败，1 秒后重试 (%d/%d)...", retry, SEND_RETRY_TIMES))
                sys.wait(1000)
            end
            sendSuccess = sendCommand(frame)
            if sendSuccess then break end
        end

        if not sendSuccess then
            log.error("TEST", string.format("第 %d 次发送失败，终止测试", i))
            if ENABLE_RESUME then
                saveResume(currentYear, currentMonth, i)
            end
            failed = true
            break
        end

        successCount = successCount + 1

        -- 保存断点（推进到下个月）
        if i < TARGET_COUNT and ENABLE_RESUME then
            local nextYear, nextMonth = addOneMonth(currentYear, currentMonth)
            saveResume(nextYear, nextMonth, i + 1)
        end

        -- 等待电表跨过零点进入下个月
        waitForFreeze(WAIT_AFTER_SEND)

        -- 核心不同点：月份加 1
        currentYear, currentMonth = addOneMonth(currentYear, currentMonth)
    end

    if failed then
        log.info("TEST", "=== 测试异常终止 ===")
        log.info("TEST", string.format("已成功发送 %d 条，第 %d 条发送失败", successCount, successCount + 1))
    else
        clearResume()
        log.info("TEST", "=== 测试任务成功完成 ===")
        log.info("TEST", string.format("共发送 %d 条月冻结数据", successCount))
    end
    log.info("TEST", "========================================")
end

-- ============================================================
--  【启动入口】
-- ============================================================

sys.taskInit(function()
    sys.wait(2000)
    local success, err = pcall(runTest)
    if not success then
        log.error("TEST", "脚本执行异常: " .. tostring(err))
    end
end)