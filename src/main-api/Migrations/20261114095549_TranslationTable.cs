using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainApi.Migrations
{
    /// <summary>Renames the consensus table without losing existing votes or accepted translations.</summary>
    public partial class TranslationTable : Migration
    {
        /// <summary>Backfills new columns before making them required.</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "consensus_decisions", newName: "translations");
            migrationBuilder.RenameColumn(name: "confirmations", table: "translations", newName: "confirmed");
            migrationBuilder.AddColumn<string>(name: "text_ba", table: "translations", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "source", table: "translations", type: "jsonb", nullable: true);
            // Derive source coordinates from the fragment's source item.
            migrationBuilder.Sql("""
                UPDATE translations AS t
                SET text_ba = f.text_ba,
                    source = jsonb_build_object(
                        'document', s.title, 'file', s.source_url,
                        'chapter', NULL, 'time', NULL,
                        'dataset', s.dataset, 'original_id', s.original_id)
                FROM fragments AS f
                JOIN source_items AS s ON s.id = f.source_id
                WHERE t.fragment_id = f.id;
                """);
            migrationBuilder.AlterColumn<string>(name: "text_ba", table: "translations", type: "text", nullable: false, oldClrType: typeof(string), oldType: "text", oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "source", table: "translations", type: "jsonb", nullable: false, oldClrType: typeof(string), oldType: "jsonb", oldNullable: true);
            // PostgreSQL does not rename constraint identifiers with the table.
            migrationBuilder.Sql("""
                ALTER TABLE translations RENAME CONSTRAINT "PK_consensus_decisions" TO "PK_translations";
                ALTER TABLE translations RENAME CONSTRAINT "FK_consensus_decisions_fragments_fragment_id" TO "FK_translations_fragments_fragment_id";
                """);
        }

        /// <summary>Restores the prior shape while retaining translation values.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "text_ba", table: "translations");
            migrationBuilder.DropColumn(name: "source", table: "translations");
            migrationBuilder.RenameColumn(name: "confirmed", table: "translations", newName: "confirmations");
            migrationBuilder.Sql("""
                ALTER TABLE translations RENAME CONSTRAINT "PK_translations" TO "PK_consensus_decisions";
                ALTER TABLE translations RENAME CONSTRAINT "FK_translations_fragments_fragment_id" TO "FK_consensus_decisions_fragments_fragment_id";
                """);
            migrationBuilder.RenameTable(name: "translations", newName: "consensus_decisions");
        }
    }
}
