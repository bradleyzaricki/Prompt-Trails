using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptTrails.Migrations
{
    /// <inheritdoc />
    public partial class SplitUsefulIntoProblemAndSolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "useful",
                table: "prompt_entries",
                newName: "solution_useful");

            migrationBuilder.AddColumn<double>(
                name: "problem_useful",
                table: "prompt_entries",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "problem_useful",
                table: "prompt_entries");

            migrationBuilder.RenameColumn(
                name: "solution_useful",
                table: "prompt_entries",
                newName: "useful");
        }
    }
}
