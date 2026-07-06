using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace PromptTrails.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "card_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "context_card",
                table: "sessions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "embedding",
                table: "prompt_entries",
                type: "vector(768)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "embedding_text",
                table: "prompt_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "enriched_at",
                table: "prompt_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "prompt_entries",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_prompt_entries_embedding",
                table: "prompt_entries",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_prompt_entries_enriched_at",
                table: "prompt_entries",
                column: "enriched_at",
                filter: "enriched_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_prompt_entries_embedding",
                table: "prompt_entries");

            migrationBuilder.DropIndex(
                name: "ix_prompt_entries_enriched_at",
                table: "prompt_entries");

            migrationBuilder.DropColumn(
                name: "card_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "context_card",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "embedding",
                table: "prompt_entries");

            migrationBuilder.DropColumn(
                name: "embedding_text",
                table: "prompt_entries");

            migrationBuilder.DropColumn(
                name: "enriched_at",
                table: "prompt_entries");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "prompt_entries");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
