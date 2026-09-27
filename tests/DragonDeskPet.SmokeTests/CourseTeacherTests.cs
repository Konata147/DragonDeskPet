using DragonDeskPet.Core;
using DragonDeskPet.Services;

internal static class CourseTeacherTests
{
    public static void Run()
    {
        var original = new CourseItem
        {
            Name = "合成多教师课程", Teacher = "教师甲,教师乙,教师丙",
            DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 40),
            StartWeek = 1, EndWeek = 17, Weeks = [1, 2, 3, 14, 15, 16, 17]
        };
        void Check(string text, CourseImportDisposition expected)
        {
            var incoming = CourseDataCopy.Clone(original); incoming.Teacher = text;
            var preview = CourseScheduleImporter.Classify(
                [new CourseImportRow(CourseImportDisposition.Add, incoming, "synthetic")], [original]);
            if (preview.Rows.Single().Disposition != expected) throw new Exception("teacher classification mismatch");
            if (incoming.Teacher != text || original.Teacher != "教师甲,教师乙,教师丙") throw new Exception("display data mutated");
        }
        Check("教师丙,教师乙,教师甲", CourseImportDisposition.Duplicate);
        Check(" 教师丙，教师乙、 教师甲；教师甲 ", CourseImportDisposition.Duplicate);
        Check("教师丙\n教师甲;教师乙", CourseImportDisposition.Duplicate);
        Check("教师丙,教师甲", CourseImportDisposition.Update);
        Check("教师丙,教师甲,教师丁", CourseImportDisposition.Update);
        Check("教师甲,教师乙,教师丙,教师丁", CourseImportDisposition.Update);
        Check("", CourseImportDisposition.Update);
        Check("教师甲 教师乙 教师丙", CourseImportDisposition.Update);
        var roomChange = CourseDataCopy.Clone(original); roomChange.Location = "另一个教室";
        roomChange.Teacher = "教师丙,教师乙,教师甲";
        if (CourseScheduleImporter.Classify([new CourseImportRow(CourseImportDisposition.Add, roomChange, "synthetic")], [original])
            .Rows.Single().Disposition != CourseImportDisposition.Update) throw new Exception("real change hidden");
        var blank = CourseDataCopy.Clone(original); blank.Teacher = "";
        var spaces = CourseDataCopy.Clone(blank); spaces.Teacher = "  ";
        if (CourseScheduleImporter.Classify([new CourseImportRow(CourseImportDisposition.Add, spaces, "synthetic")], [blank])
            .Rows.Single().Disposition != CourseImportDisposition.Duplicate) throw new Exception("empty names mismatch");
    }
}
