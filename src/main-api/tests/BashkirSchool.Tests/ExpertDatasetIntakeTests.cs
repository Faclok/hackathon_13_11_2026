using System.Net;
using System.Net.Http.Json;
using BashkirSchool.Application;
using BashkirSchool.Domain;
using BashkirSchool.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BashkirSchool.Tests.CoreFactory;

namespace BashkirSchool.Tests;

public sealed class ExpertDatasetIntakeTests
{
    [Fact]
    public async Task ImportedVerifiedTranslationHasNoNamedExpertAndIsIdempotent()
    {
        using var factory = new CoreFactory();
        using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        var row = new ExpertDatasetRow("phr-1", "Һаумыһығыҙ!", "ba-ru", "Здравствуйте!", true,
            new SourceReference("phrasebook", "Разговорник", "source_record_id=1"));

        var receipt = await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports", row);
        Assert.Equal("expert_verified", receipt.Status);
        Assert.Equal(receipt, await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports", row));
        Assert.Equal(HttpStatusCode.NoContent, (await student.Client.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Single(await db.Fragments.ToArrayAsync());
        Assert.True((await db.Cases.SingleAsync()).ExpertClosed);
        var decision = await db.Resolutions.SingleAsync();
        Assert.Equal(ResolutionKind.Expert, decision.Kind);
        Assert.Null(decision.DecidedBy);
        Assert.Equal(receipt.VariantId, decision.PrimaryVariantId);
    }

    [Fact]
    public async Task UnknownFragmentCanLaterReceiveAnExpertTranslation()
    {
        using var factory = new CoreFactory();
        using var admin = await factory.Bootstrap();
        var row = new ExpertDatasetRow("unknown-1", "Күпме тауға менгән?", "ba-ru", null, false,
            new SourceReference("periodicals", "Аманат"));
        var receipt = await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports", row);
        Assert.Equal("untranslated", receipt.Status);
        Assert.NotNull(receipt.CaseId);
        Assert.NotNull(receipt.VariantId);
        Assert.Equal(receipt, await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports", row));
        var pending = await admin.GetFromJsonAsync<UntranslatedFragment[]>("/api/expert/untranslated");
        Assert.Equal(receipt.FragmentId, Assert.Single(pending!).FragmentId);

        using var expert = await ResolutionFlowTests.Staff(factory, admin, "expert");
        Assert.Contains((await expert.GetFromJsonAsync<ExpertQueueItem[]>("/api/expert/cases"))!, x => x.CaseId == receipt.CaseId);
        var translated = await Post<ExpertDatasetReceipt>(expert, $"/api/expert/fragments/{receipt.FragmentId}/translation",
            new VerifiedFragmentTranslation("Сколько гор он покорил?"));
        Assert.Equal("expert_verified", translated.Status);
        Assert.Empty((await admin.GetFromJsonAsync<UntranslatedFragment[]>("/api/expert/untranslated"))!);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Single(await db.Fragments.ToArrayAsync());
        Assert.Single(await db.Cases.ToArrayAsync());
        Assert.NotNull((await db.Resolutions.SingleAsync()).DecidedBy);
    }

    [Fact]
    public async Task StudentCreatesTranslationAndBlankCandidateCannotBeConfirmed()
    {
        using var factory = new CoreFactory();
        using var admin = await factory.Bootstrap();
        var first = await factory.Student(admin);
        var second = await factory.Student(admin, "2");
        var row = new ExpertDatasetRow("unknown-2", "Һаумыһығыҙ!", "ba-ru", null, false, new SourceReference("periodicals"));
        var imported = await Post<ExpertDatasetReceipt>(admin, "/api/expert/imports", row);
        Assert.Equal(HttpStatusCode.NoContent, (await first.Client.PostAsJsonAsync("/api/reviews/next", new { })).StatusCode);

        var assignment = await Post<AssignmentResponse>(first.Client, "/api/reviews/next?kind=translation-create", new { });
        Assert.Equal("translation-create", assignment.Kind);
        Assert.Equal("", assignment.Translation);
        Assert.Empty(assignment.Options);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.Client.PostAsJsonAsync($"/api/reviews/{assignment.Id}/response",
            new SubmitReviewRequest(ReviewAction.Confirm))).StatusCode);
        await Post<ReviewReceipt>(first.Client, $"/api/reviews/{assignment.Id}/response",
            new SubmitReviewRequest(ReviewAction.Correct, "Здравствуйте!"));

        Assert.Equal(HttpStatusCode.NoContent, (await second.Client.PostAsJsonAsync("/api/reviews/next?kind=translation-create", new { })).StatusCode);
        var next = await Post<AssignmentResponse>(second.Client, "/api/reviews/next", new { });
        Assert.Equal(imported.CaseId, next.CaseId);
        Assert.Equal("Здравствуйте!", next.Translation);
        Assert.NotEqual(imported.VariantId, next.VariantId);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        Assert.Equal(2, await db.Variants.CountAsync());
        Assert.Equal(1, await db.Judgments.CountAsync());
    }

    [Fact]
    public async Task StudentCannotImportOrMarkUnknownTextAsVerified()
    {
        using var factory = new CoreFactory();
        using var admin = await factory.Bootstrap();
        var student = await factory.Student(admin);
        var row = new ExpertDatasetRow("unknown-1", "Һаумы", "ba-ru", null, false, new SourceReference("book"));
        Assert.Equal(HttpStatusCode.Forbidden, (await student.Client.PostAsJsonAsync("/api/expert/imports", row)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/expert/imports", row with { ExpertVerified = true })).StatusCode);
    }
}
