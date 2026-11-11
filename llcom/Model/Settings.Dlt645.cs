namespace llcom.Model;

/// <summary>
/// DL/T 645 工具相关设置（Settings 分部类）。
/// 写数据时自动附加的密码 / 操作者代码，以及是否默认自动添加。
/// </summary>
partial class Settings
{
    private string _dlt645WritePassword = "";

    /// <summary>
    /// 写数据密码（4 字节 HEX：PA 密码权限 + P0P1P2 密码，高位在前；组帧时低位先行）。
    /// 默认留空：密码由电表厂家 / 供电局设定，不写死默认值，首次使用时按现场表填写。
    /// </summary>
    public string dlt645WritePassword
    {
        get => _dlt645WritePassword;
        set
        {
            if (SetProperty(ref _dlt645WritePassword, value))
                Save();
        }
    }

    private string _dlt645WriteOperator = "12345678";

    /// <summary>
    /// 写数据操作者代码（4 字节 HEX，高位在前；组帧时低位先行）。
    /// 默认值仅为占位示例，实际按现场要求填写。
    /// </summary>
    public string dlt645WriteOperator
    {
        get => _dlt645WriteOperator;
        set
        {
            if (SetProperty(ref _dlt645WriteOperator, value))
                Save();
        }
    }

    private bool _dlt645WriteAutoInfo = true;

    /// <summary>写数据时是否自动在 DI 后添加密码和操作者代码</summary>
    public bool dlt645WriteAutoInfo
    {
        get => _dlt645WriteAutoInfo;
        set
        {
            if (SetProperty(ref _dlt645WriteAutoInfo, value))
                Save();
        }
    }
}
