using System.IO;

namespace DragonDeskPet.Services;

public static class CourseImportErrors
{
    public static string Describe(Exception exception) => exception switch
    {
        IOException io when (io.HResult & 0xffff) is 32 or 33 =>
            "课表文件正被其他程序占用。请在 WPS/Excel 中关闭这个文件，再点击“导入文件”重试；现有课表未改变。",
        FileNotFoundException or DirectoryNotFoundException => "找不到课表文件，请重新选择文件。现有课表未改变。",
        UnauthorizedAccessException => "没有权限读取课表文件，请将文件复制到可访问的目录后重试。现有课表未改变。",
        ArgumentException or FormatException => exception.Message,
        _ => "无法读取课表，请检查文件是否损坏、被加密或正在被占用。现有课表未改变。"
    };
}
