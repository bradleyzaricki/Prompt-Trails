using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace PromptTrails.Migrations
{
    /// <inheritdoc />
    public partial class AddTermsTextToSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // terms_text must exist before the generated search_vector column can reference it.
            migrationBuilder.AddColumn<string>(
                name: "terms_text",
                table: "prompt_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "prompt_entries",
                type: "tsvector",
                nullable: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true)
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "problem_embedding_text", "solution_embedding_text", "terms_text", "prompt_text" })
                .OldAnnotation("Npgsql:TsVectorConfig", "english")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "problem_embedding_text", "solution_embedding_text", "prompt_text" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "prompt_entries",
                type: "tsvector",
                nullable: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true)
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "problem_embedding_text", "solution_embedding_text", "prompt_text" })
                .OldAnnotation("Npgsql:TsVectorConfig", "english")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "problem_embedding_text", "solution_embedding_text", "terms_text", "prompt_text" });

            migrationBuilder.DropColumn(
                name: "terms_text",
                table: "prompt_entries");
        }
    }
}
