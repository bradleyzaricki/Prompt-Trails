using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace PromptTrails.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchVectorFullText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "prompt_entries",
                type: "tsvector",
                nullable: true)
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "problem_embedding_text", "solution_embedding_text", "prompt_text" });

            migrationBuilder.CreateIndex(
                name: "ix_prompt_entries_search_vector",
                table: "prompt_entries",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_prompt_entries_search_vector",
                table: "prompt_entries");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "prompt_entries");
        }
    }
}
