using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace llcom.Tools;

class InputDialog
{
    public static Tuple<bool, string> OpenDialog(
        string prompt,
        string? defaultInput = "",
        string? title = null,
        IEnumerable<string>? suggestions = null
    )
    {
        InputDialogWindow dialog = new InputDialogWindow(prompt, defaultInput, title, suggestions);
        bool ret = dialog.ShowDialog() ?? false;
        return Tuple.Create(ret, dialog.Value);
    }
}

class MessageBox
{
    public static void Show(string s)
    {
        try
        {
            // 纯提示：不显示输入框（defaultInput 传 null 时 InputDialogWindow 会隐藏输入区）
            InputDialog.OpenDialog(s, null);
        }
        catch
        {
            System.Windows.Forms.MessageBox.Show(
                s,
                "Notice",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.ServiceNotification
            );
        }
    }
}
