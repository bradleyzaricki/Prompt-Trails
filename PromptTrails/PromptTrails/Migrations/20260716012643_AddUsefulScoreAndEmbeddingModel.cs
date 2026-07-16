using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PromptTrails.Migrations
{
    /// <inheritdoc />
    public partial class AddUsefulScoreAndEmbeddingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "embedding_model",
                table: "prompt_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "useful",
                table: "prompt_entries",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embedding_model",
                table: "prompt_entries");

            migrationBuilder.DropColumn(
                name: "useful",
                table: "prompt_entries");
        }
    }
}
