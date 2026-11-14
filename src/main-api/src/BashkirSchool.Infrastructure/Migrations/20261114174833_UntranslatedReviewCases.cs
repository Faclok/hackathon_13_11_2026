using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BashkirSchool.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UntranslatedReviewCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing source-only dataset rows become reviewable without changing their IDs or provenance.
            migrationBuilder.Sql("""
                INSERT INTO "Cases" ("Id", "FragmentId", "ShownVariantId", "CreatedAt", "ExpertClosed", "Revision", "ResolutionId", "NeedsExpert")
                SELECT gen_random_uuid(), f."Id", gen_random_uuid(), now(), FALSE, 0, NULL, TRUE
                FROM "Fragments" AS f
                WHERE NOT EXISTS (SELECT 1 FROM "Cases" AS c WHERE c."FragmentId" = f."Id")
                  AND EXISTS (SELECT 1 FROM "Audit" AS a
                              WHERE a."TargetId" = f."Id" AND a."Action" = 'dataset.imported.untranslated');

                INSERT INTO "Variants" ("Id", "CaseId", "Text", "MachineGenerated", "CreatedBy", "CreatedAt")
                SELECT c."ShownVariantId", c."Id", '', FALSE, NULL, c."CreatedAt"
                FROM "Cases" AS c
                WHERE NOT EXISTS (SELECT 1 FROM "Variants" AS v WHERE v."CaseId" = c."Id")
                  AND EXISTS (SELECT 1 FROM "Audit" AS a
                              WHERE a."TargetId" = c."FragmentId" AND a."Action" = 'dataset.imported.untranslated');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Responses may reference these cases, so their data must not be deleted on rollback.
        }
    }
}
