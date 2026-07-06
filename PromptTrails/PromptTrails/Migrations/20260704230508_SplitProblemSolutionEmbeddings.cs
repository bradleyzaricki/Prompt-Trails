using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace PromptTrails.Migrations
{
    /// <inheritdoc />
    public partial class SplitProblemSolutionEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "embedding_text",
                table: "prompt_entries",
                newName: "solution_embedding_text");

            migrationBuilder.RenameColumn(
                name: "embedding",
                table: "prompt_entries",
                newName: "solution_embedding");

            migrationBuilder.RenameIndex(
                name: "ix_prompt_entries_embedding",
                table: "prompt_entries",
                newName: "ix_prompt_entries_solution_embedding");

            migrationBuilder.AddColumn<Vector>(
                name: "problem_embedding",
                table: "prompt_entries",
                type: "vector(768)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "problem_embedding_text",
                table: "prompt_entries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_prompt_entries_problem_embedding",
                table: "prompt_entries",
                column: "problem_embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_prompt_entries_problem_embedding",
                table: "prompt_entries");

            migrationBuilder.DropColumn(
                name: "problem_embedding",
                table: "prompt_entries");

            migrationBuilder.DropColumn(
                name: "problem_embedding_text",
                table: "prompt_entries");

            migrationBuilder.RenameColumn(
                name: "solution_embedding_text",
                table: "prompt_entries",
                newName: "embedding_text");

            migrationBuilder.RenameColumn(
                name: "solution_embedding",
                table: "prompt_entries",
                newName: "embedding");

            migrationBuilder.RenameIndex(
                name: "ix_prompt_entries_solution_embedding",
                table: "prompt_entries",
                newName: "ix_prompt_entries_embedding");
        }
    }
}
