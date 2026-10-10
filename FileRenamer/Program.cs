using System;
using System.Reflection;
using System.Windows.Forms;

// 用「构建.ps1」直接调用 csc 编译时也带上版本信息，与 dotnet build 的产物保持一致
[assembly: AssemblyTitle("批量文件重命名工具")]
[assembly: AssemblyDescription("批量给文件加排序序号、做字段替换；执行前可预览并拦截重名冲突")]
[assembly: AssemblyProduct("FileRenamer")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace FileRenamer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
