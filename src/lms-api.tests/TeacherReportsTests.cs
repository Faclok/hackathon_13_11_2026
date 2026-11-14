public sealed class TeacherReportsTests
{
    [Theory]
    [InlineData("Здравствуйте!", "здравствуйте", true)]
    [InlineData("Һаумыһығыҙ!", " һаумыһығыҙ ", true)]
    [InlineData("Добрый день", "Здравствуйте", false)]
    public void TextMatchingIgnoresFormattingButNotDifferentWords(string answer, string reference, bool expected)
    {
        Assert.Equal(expected, TeacherReports.SameTranslation(answer, reference));
    }

    [Fact]
    public void OnlyCoreDecisionsGradeAnswersAndTeacherFlagsAreIgnored()
    {
        var submission = new TaskSubmission { Answer = "Добрый день", TeacherCorrect = true };
        Assert.Equal("review", TeacherReports.Verdict(submission, "pending", ["Здравствуйте"]));
        Assert.Equal("review", TeacherReports.Verdict(submission, "expert_review", ["Здравствуйте"]));
        Assert.Equal("incorrect", TeacherReports.Verdict(submission, "community", ["Здравствуйте"]));
        Assert.Equal("correct", TeacherReports.Verdict(submission, "expert", ["Здравствуйте", "Добрый день"]));
        Assert.Equal("missing", TeacherReports.Verdict(null, "expert", ["Добрый день"]));
    }

    [Fact]
    public void CsvEscapesQuotesAndSpreadsheetFormulas()
    {
        Assert.Equal("\"' =SUM(1,2)\",\"Он сказал \"\"да\"\"\"", TeacherReports.Csv(" =SUM(1,2)", "Он сказал \"да\""));
    }
}
