namespace Sms.Application.Services.AiSearch;

public interface IAiAnswerTemplateService
{
    string RenderDailyAttendanceSummary(string language, int total, int present, int absent, decimal pct);
    string RenderClassAttendance(string language, string className, int total, int present, int absent, decimal pct);
    string RenderStudentAttendance(string language, string studentName, decimal pct);
    /// <summary>
    /// Time-of-day greeting for a resolved name. <paramref name="hour"/> is the already-resolved
    /// hour-of-day (0-23) the caller determined from a <see cref="TimeProvider"/> — this method never
    /// computes "now" itself, keeping it a pure function of its inputs like every other Render* method
    /// here. Buckets: hour &lt; 12 morning, 12-16 afternoon, &gt;= 17 evening.
    /// </summary>
    string RenderGreeting(string language, string name, int hour);
    string RenderWriteBlocked(string language);
    string RenderUnsupported(string language);
    string RenderForbidden(string language);
    string RenderNoMatch(string language);
}

public sealed class AiAnswerTemplateService : IAiAnswerTemplateService
{
    public string RenderDailyAttendanceSummary(string language, int total, int present, int absent, decimal pct) =>
        language switch
        {
            "hi" => $"आज {total} में से {present} बच्चे उपस्थित हैं। अनुपस्थित: {absent}, उपस्थिति: {pct}%.",
            "hinglish" => $"Aaj {total} mein se {present} bachche school aaye hain. Absent: {absent}, attendance: {pct}%.",
            _ => $"Today, {present} students are present out of {total}. Absent: {absent}, attendance: {pct}%."
        };

    public string RenderClassAttendance(string language, string className, int total, int present, int absent, decimal pct) =>
        language switch
        {
            "hi" => $"कक्षा {className}: {total} में से {present} उपस्थित, {absent} अनुपस्थित ({pct}%).",
            "hinglish" => $"Class {className}: {total} mein se {present} present, {absent} absent ({pct}%).",
            _ => $"Class {className}: {present} of {total} present, {absent} absent ({pct}%)."
        };

    public string RenderStudentAttendance(string language, string studentName, decimal pct) =>
        language switch
        {
            "hi" => $"{studentName} की उपस्थिति {pct}% है।",
            "hinglish" => $"{studentName} ki attendance {pct}% hai.",
            _ => $"{studentName}'s attendance is {pct}%."
        };

    public string RenderGreeting(string language, string name, int hour)
    {
        var bucket = hour switch
        {
            < 12 => "morning",
            < 17 => "afternoon",
            _ => "evening",
        };
        return (language, bucket) switch
        {
            ("hi", "morning") => $"सुप्रभात, {name}",
            ("hi", "afternoon") => $"नमस्कार, {name}",
            ("hi", "evening") => $"शुभ संध्या, {name}",
            ("hinglish", "morning") => $"Good morning, {name}",
            ("hinglish", "afternoon") => $"Good afternoon, {name}",
            ("hinglish", "evening") => $"Good evening, {name}",
            (_, "morning") => $"Good morning, {name}",
            (_, "afternoon") => $"Good afternoon, {name}",
            _ => $"Good evening, {name}",
        };
    }

    public string RenderWriteBlocked(string language) =>
        language switch
        {
            "hi" => "मैं केवल डेटा खोज और प्रदर्शित कर सकता हूँ। मैं स्कूल डेटा को संशोधित नहीं कर सकता।",
            "hinglish" => "Main sirf data search aur display kar sakta hoon. Main school data ko modify nahi kar sakta.",
            _ => "I can only search and display information. I cannot modify school data."
        };

    public string RenderUnsupported(string language) =>
        language switch
        {
            "hi" => "मुझे यह समझ नहीं आया। कृपया उपस्थिति, छात्र, परीक्षा, होमवर्क, विषय या बस के बारे में पूछें।",
            "hinglish" => "Yeh samajh nahi aaya. Attendance, students, exams, homework, subjects, ya bus ke baare mein pucho.",
            _ => "I couldn't understand that as a supported search. Try asking about attendance, students, exams, homework, subjects, or bus location."
        };

    public string RenderForbidden(string language) =>
        language switch
        {
            "hi" => "आपको यह जानकारी देखने की अनुमति नहीं है।",
            "hinglish" => "Aapko yeh dekhne ki permission nahi hai.",
            _ => "You don't have permission to view this information."
        };

    public string RenderNoMatch(string language) =>
        language switch
        {
            "hi" => "कोई मेल खाता रिकॉर्ड नहीं मिला।",
            "hinglish" => "Koi matching record nahi mila.",
            _ => "No matching records were found."
        };
}
