using Microsoft.EntityFrameworkCore;

static class DemoSeed
{
    public static async Task Apply(LmsDbContext db)
    {
        if (await db.Courses.AnyAsync()) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var course = new LmsCourse { Title = "Башкирский язык: первые шаги",
            Description = "Короткий вводный курс к ежедневной практике перевода." };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        db.Lessons.AddRange(
            new LmsLesson { CourseId = course.Id, Position = 1, Title = "Приветствие", Body = "Һаумыһығыҙ — «Здравствуйте». Повторите фразу и перейдите к заданию дня." },
            new LmsLesson { CourseId = course.Id, Position = 2, Title = "Благодарность", Body = "Рәхмәт — «Спасибо». Попробуйте узнать эту фразу в задании." },
            new LmsLesson { CourseId = course.Id, Position = 3, Title = "Практика", Body = "Проверяйте предложенный перевод и исправляйте его, если нужно. Ответы нескольких людей помогают согласовать перевод." }
        );
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
